using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Gs;

public sealed record GeneralGsChange(
    string Field, int Before, int After, string BeforeHex, string AfterHex, int? BytesWritten);

public sealed class GeneralGsTransaction
{
    public const string FormatName = "nba2k27-general-gs-edit/1";

    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Mode { get; init; } = "native_general_gs";
    public string Time { get; init; } = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
    public string Operation { get; set; } = "apply";
    public GsEpoch Epoch { get; set; } = null!;
    public long Root { get; set; }
    public IReadOnlyList<(long Start, long Length, long Allocation, uint AllocationProtect, uint State, uint Protect, uint Type)> Allocation { get; set; } = Array.Empty<(long, long, long, uint, uint, uint, uint)>();
    public int Copy { get; init; } = 1;
    public List<GeneralGsChange> Changes { get; set; } = new();
    public string Status { get; set; } = "prepared";
    public string? Error { get; set; }
    public List<string> BlocksBeforeHex { get; set; } = new();
    public List<string> BlocksAfterHex { get; set; } = new();
    public bool GameSettingsSaved { get; init; }
    public List<string> TargetFieldsRestored { get; set; } = new();
    public List<(string Field, string Error)> RestorationErrors { get; set; } = new();
    public string? UndoJournal { get; set; }
    public string? JournalPath { get; set; }
}

/// <summary>Recoverable edits to the mapped primary MyNBA GS array.</summary>
public sealed class NativeGeneralGsSession : IDisposable
{
    private readonly string _dataDirectory;
    private readonly List<(string Path, GeneralGsTransaction Transaction)> _undo = new();
    private NativeGeneralGsReader? _reader;
    private NativeGeneralGsWriter? _writer;

    public NativeGeneralGsSession(string? dataDirectory = null)
    {
        _dataDirectory = dataDirectory is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NBA2K27PlayerEditor", "general-gs")
            : Path.Combine(dataDirectory, "general-gs");
    }

    public GsEpoch? Epoch { get; private set; }
    public bool NeedsReview { get; private set; }
    public string LastWarning { get; private set; } = string.Empty;
    public IReadOnlyList<(string Path, GeneralGsTransaction Transaction)> UndoStack => _undo;
    public int WriteAttempts => _writer?.WriteAttempts ?? 0;

    public GeneralGsSnapshot Connect()
    {
        Close();
        var reader = new NativeGeneralGsReader();
        try
        {
            var snapshot = reader.Read();
            _reader = reader;
            _writer = new NativeGeneralGsWriter(reader);
            Epoch = snapshot.Epoch;
            _undo.Clear();
            NeedsReview = false;
            LastWarning = string.Empty;
            return snapshot;
        }
        catch (Exception)
        {
            reader.Dispose();
            throw;
        }
    }

    private NativeGeneralGsReader Reader =>
        _reader ?? throw new InvalidOperationException("Connect to NBA 2K27 first");

    private NativeGeneralGsWriter Writer =>
        _writer ?? throw new InvalidOperationException("Connect to NBA 2K27 first");

    public void Guard(GeneralGsSnapshot snapshot) => Reader.Guard(snapshot);

    public GeneralGsSnapshot Read() => Reader.Read();

    /// <summary>Apply integer percentages through one journaled transaction.</summary>
    public GeneralGsSnapshot? Apply(GeneralGsSnapshot snapshot, IReadOnlyDictionary<string, int> changes)
    {
        var raw = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var pair in changes)
        {
            raw[pair.Key] = GsFieldIds.PercentBytes(pair.Value);
        }
        var (transaction, fresh) = Commit(snapshot, raw, "apply");
        if (transaction is null || fresh is null)
        {
            return null;
        }
        _undo.Add((transaction.JournalPath!, transaction));
        return fresh;
    }

    public GeneralGsSnapshot Undo()
    {
        LastWarning = string.Empty;
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("There is no GS edit to undo in this connection");
        }
        var (path, transaction) = _undo[^1];
        if (transaction.Status == "needs_review" || NeedsReview)
        {
            throw new InvalidOperationException("The incomplete GS transaction needs review before another undo");
        }
        GuardEpoch(transaction.Epoch);
        var current = Read();
        var restored = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (var change in transaction.Changes)
        {
            int offset = GsFieldIds.FieldOffset(change.Field);
            var now = current.Blocks[0].AsSpan(offset, 4);
            if (Convert.ToHexString(now).ToLowerInvariant() != change.AfterHex)
            {
                throw new InvalidOperationException("A GS value changed independently; undo stopped");
            }
            restored[change.Field] = Convert.FromHexString(change.BeforeHex);
        }
        var (_, fresh) = Commit(current, restored, "undo");
        _undo.RemoveAt(_undo.Count - 1);
        transaction.Status = "undone";
        transaction.UndoJournal = null;
        try
        {
            SaveJournal(path, transaction);
        }
        catch (IOException exc)
        {
            LastWarning = $"GS undo succeeded, but log update failed: {exc.Message}";
        }
        return fresh!;
    }

    private void GuardEpoch(GsEpoch epoch)
    {
        if (Epoch != epoch)
        {
            throw new InvalidOperationException("The game process or build changed; reconnect native GS");
        }
    }

    private (GeneralGsTransaction? Transaction, GeneralGsSnapshot? Snapshot) Commit(
        GeneralGsSnapshot snapshot, Dictionary<string, byte[]> rawChanges, string operation)
    {
        LastWarning = string.Empty;
        Guard(snapshot);
        var blocks = snapshot.Blocks;
        var expected = (byte[])blocks[0].Clone();
        var changes = new List<GeneralGsChange>();
        var afterBytes = new List<byte[]>();
        foreach (var pair in rawChanges)
        {
            int offset = GsFieldIds.FieldOffset(pair.Key);
            var raw = pair.Value;
            if (raw.Length != 4)
            {
                throw new ArgumentException("Expected one native GS float");
            }
            int after = GsFieldIds.CheckRaw(raw);
            var old = blocks[0].AsSpan(offset, 4).ToArray();
            int before = GsFieldIds.CheckRaw(old);
            if (old.SequenceEqual(raw))
            {
                continue;
            }
            raw.CopyTo(expected, offset);
            changes.Add(new GeneralGsChange(
                pair.Key, before, after,
                Convert.ToHexString(old).ToLowerInvariant(),
                Convert.ToHexString(raw).ToLowerInvariant(), null));
            afterBytes.Add(raw);
        }
        if (changes.Count == 0)
        {
            return (null, snapshot);
        }
        var confirmed = Read();
        if (!confirmed.Blocks[0].SequenceEqual(blocks[0]) || !confirmed.Blocks[1].SequenceEqual(blocks[1]))
        {
            throw new InvalidOperationException("Native general GS changed since reading; read again");
        }
        var transaction = new GeneralGsTransaction
        {
            Operation = operation,
            Epoch = snapshot.Epoch,
            Root = snapshot.Root,
            Allocation = snapshot.Allocation,
            Changes = changes,
            BlocksBeforeHex = blocks.Select(Convert.ToHexString).Select(text => text.ToLowerInvariant()).ToList(),
            BlocksAfterHex = new List<string>
            {
                Convert.ToHexString(expected).ToLowerInvariant(),
                Convert.ToHexString(blocks[1]).ToLowerInvariant(),
            },
        };
        Directory.CreateDirectory(_dataDirectory);
        var path = Path.Combine(
            _dataDirectory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{transaction.Id[..8]}.json");
        transaction.JournalPath = path;
        SaveJournal(path, transaction);

        var completed = new List<GeneralGsChange>();
        var planned = (byte[])blocks[0].Clone();
        try
        {
            for (int index = 0; index < changes.Count; index++)
            {
                var change = changes[index];
                Guard(snapshot);
                var current = Read();
                if (!current.Blocks[0].SequenceEqual(planned) || !current.Blocks[1].SequenceEqual(blocks[1]))
                {
                    throw new InvalidOperationException("Native general GS changed before writing");
                }
                int offset = GsFieldIds.FieldOffset(change.Field);
                var beforeBytes = Convert.FromHexString(change.BeforeHex);
                try
                {
                    Writer.Write(snapshot, change.Field, beforeBytes, afterBytes[index]);
                }
                catch (GeneralGsWriteException exc)
                {
                    completed.Add(change with { BytesWritten = exc.BytesWritten });
                    throw new InvalidOperationException(exc.Message, exc);
                }
                completed.Add(change with { BytesWritten = 4 });
                afterBytes[index].CopyTo(planned, offset);
                var fresh = Read();
                Guard(snapshot);
                if (!fresh.Blocks[0].SequenceEqual(planned) || !fresh.Blocks[1].SequenceEqual(blocks[1]))
                {
                    throw new InvalidOperationException("Native general GS changed while writing");
                }
            }
            var final = Read();
            Guard(snapshot);
            if (!final.Blocks[0].SequenceEqual(expected) || !final.Blocks[1].SequenceEqual(blocks[1]))
            {
                throw new InvalidOperationException("Native general GS final values differ from the plan");
            }
            transaction.Status = "applied";
            transaction.Changes = completed;
            transaction.TargetFieldsRestored.Clear();
            SaveJournal(path, transaction);
            return (transaction, final);
        }
        catch (Exception exc)
        {
            var (restored, errors) = Restore(snapshot, completed);
            transaction.Status = errors.Count > 0 ? "needs_review_restore_failed" : "restored_after_error";
            transaction.Error = exc.Message;
            transaction.TargetFieldsRestored = restored;
            transaction.RestorationErrors = errors;
            NeedsReview = errors.Count > 0;
            try
            {
                SaveJournal(path, transaction);
            }
            catch (IOException logError)
            {
                throw new GeneralGsTransactionException(
                    $"{exc.Message}; failed to update the GS log: {logError.Message}", path, NeedsReview);
            }
            throw new GeneralGsTransactionException(exc.Message, path, NeedsReview);
        }
    }

    /// <summary>Restore each completed change; partial writes are honoured byte by byte.</summary>
    private (List<string> Restored, List<(string Field, string Error)> Errors) Restore(
        GeneralGsSnapshot snapshot, List<GeneralGsChange> completed)
    {
        var restored = new List<string>();
        var errors = new List<(string, string)>();
        for (int index = completed.Count - 1; index >= 0; index--)
        {
            var change = completed[index];
            try
            {
                Guard(snapshot);
                var current = Writer.ReadTarget(snapshot, change.Field);
                var beforeBytes = Convert.FromHexString(change.BeforeHex);
                var after = Convert.FromHexString(change.AfterHex);
                if (current.SequenceEqual(beforeBytes))
                {
                    continue;
                }
                var expected = (byte[])after.Clone();
                if (change.BytesWritten is int written && written >= 0 && written < 4)
                {
                    expected = after.AsSpan(0, written).ToArray()
                        .Concat(beforeBytes.AsSpan(written).ToArray()).ToArray();
                }
                if (!current.SequenceEqual(expected))
                {
                    throw new InvalidOperationException("Native GS field changed; automatic restore stopped");
                }
                Writer.Write(snapshot, change.Field, current, beforeBytes);
                if (!Writer.ReadTarget(snapshot, change.Field).SequenceEqual(beforeBytes))
                {
                    throw new InvalidOperationException("Native GS restore readback failed");
                }
                restored.Add(change.Field);
            }
            catch (Exception exc)
            {
                errors.Add((change.Field, exc.Message));
            }
        }
        return (restored, errors);
    }

    public void SaveJournal(string path, GeneralGsTransaction transaction)
    {
        var document = new JsonObject
        {
            ["format"] = GeneralGsTransaction.FormatName,
            ["id"] = transaction.Id,
            ["mode"] = transaction.Mode,
            ["time"] = transaction.Time,
            ["operation"] = transaction.Operation,
            ["epoch"] = transaction.Epoch.ToJsonArray(),
            ["root"] = transaction.Root,
            ["allocation_identity"] = AllocationNode(transaction.Allocation),
            ["copy"] = transaction.Copy,
            ["status"] = transaction.Status,
            ["error"] = transaction.Error,
            ["game_settings_saved"] = transaction.GameSettingsSaved,
            ["target_fields_restored"] = new JsonArray(
                transaction.TargetFieldsRestored.Select(field => (JsonNode?)JsonValue.Create(field)).ToArray()),
            ["restoration_errors"] = new JsonArray(
                transaction.RestorationErrors
                    .Select(error => (JsonNode?)new JsonObject { ["field"] = error.Field, ["error"] = error.Error })
                    .ToArray()),
            ["undo_journal"] = transaction.UndoJournal,
            ["blocks_before"] = Strings(transaction.BlocksBeforeHex),
            ["blocks_after"] = Strings(transaction.BlocksAfterHex),
        };
        var changes = new JsonArray();
        foreach (var change in transaction.Changes)
        {
            changes.Add(new JsonObject
            {
                ["field"] = change.Field,
                ["before"] = change.Before,
                ["after"] = change.After,
                ["before_hex"] = change.BeforeHex,
                ["after_hex"] = change.AfterHex,
                ["bytes_written"] = change.BytesWritten,
            });
        }
        document["changes"] = changes;
        var temporary = Path.ChangeExtension(path, ".tmp");
        File.WriteAllText(temporary, document.ToJsonString(Editing.JournalJson.Options), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static JsonArray Strings(IEnumerable<string> values) =>
        new(values.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());

    private static JsonArray AllocationNode(
        IReadOnlyList<(long Start, long Length, long Allocation, uint AllocationProtect, uint State, uint Protect, uint Type)> allocation)
    {
        var array = new JsonArray();
        foreach (var row in allocation)
        {
            array.Add(new JsonArray(
                JsonValue.Create(row.Start),
                JsonValue.Create(row.Length),
                JsonValue.Create(row.Allocation),
                JsonValue.Create((long)row.AllocationProtect),
                JsonValue.Create((long)row.State),
                JsonValue.Create((long)row.Protect),
                JsonValue.Create((long)row.Type)));
        }
        return array;
    }

    public void Close()
    {
        _reader?.Dispose();
        _reader = null;
        _writer = null;
        Epoch = null;
        _undo.Clear();
        NeedsReview = false;
        LastWarning = string.Empty;
    }

    public void Dispose() => Close();
}
