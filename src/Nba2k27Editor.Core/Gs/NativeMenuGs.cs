using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;

namespace Nba2k27Editor.Core.Gs;

/// <summary>
/// The in-match GS array, ported from native_gs.py and widened to the full general GS field
/// list. The array base (<c>LegacyBlockRva0</c>) sits at general id 27, so a field's offset is
/// <c>id * 8 + (cpu ? 4 : 0)</c> measured from <c>LegacyBlockRva0 + WindowStart</c>. Only the
/// menu-verified CPU three-point values 56 and 57 are enabled on the verified path; every other
/// field is writable through the explicitly unverified path.
/// </summary>
public static class NativeMenuGs
{
    /// <summary>General GS id that sits at the array base; verified against the live game.</summary>
    public const int FirstGeneralId = 27;

    /// <summary>Lowest general id the read window covers; ids 0 and 1 act as layout sentinels.</summary>
    public const int WindowFirstGeneralId = 0;

    /// <summary>Highest general id the read window covers; 150..162 are layout sentinels.</summary>
    public const int WindowLastGeneralId = 162;

    /// <summary>Lowest editable general id (stealing).</summary>
    public const int FieldFirstGeneralId = 2;

    /// <summary>Highest editable general id (acceleration_min).</summary>
    public const int FieldLastGeneralId = 149;

    /// <summary>Bytes from the array base back to the first window slot.</summary>
    public const int WindowStart = (WindowFirstGeneralId - FirstGeneralId) * 8;

    /// <summary>Bytes read from each of the two copies.</summary>
    public const int BlockBytes = (WindowLastGeneralId - WindowFirstGeneralId + 1) * 8;

    /// <summary>The 70 general GS fields, ordered by their internal id.</summary>
    public static readonly string[] Fields = GsFieldIds.Ids
        .OrderBy(pair => pair.Value)
        .Select(pair => pair.Key)
        .ToArray();

    public static readonly int[] TestedValues = { 56, 57 };
    public const string TargetField = "cpu.three_point";

    public static IReadOnlyList<string> Keys { get; } = Fields
        .SelectMany(field => new[] { $"user.{field}", $"cpu.{field}" })
        .ToList();

    /// <summary>Decode one window into the 140 integer percentages of the 70 fields.</summary>
    public static Dictionary<string, int> DecodeBlock(byte[] raw)
    {
        if (raw.Length != BlockBytes)
        {
            throw new InvalidOperationException(
                "Expected the full in-match GS window covering general ids 0 to 162");
        }
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string field in Fields)
        {
            int id = GsFieldIds.Ids[field];
            for (int column = 0; column < 2; column++)
            {
                float number = BitConverter.ToSingle(raw, id * 8 + column * 4);
                if (!float.IsFinite(number) || number < 0f || number > 1f
                    || Math.Abs(number * 100 - MathF.Round(number * 100)) > 0.0001)
                {
                    throw new InvalidOperationException(
                        "Native GS data is uninitialized or has an unsupported layout");
                }
                string side = column == 0 ? "user" : "cpu";
                values[$"{side}.{field}"] = (int)MathF.Round(number * 100);
            }
        }
        return values;
    }

    /// <summary>True for a byte that belongs to a layout sentinel rather than an editable field.</summary>
    public static bool IsSentinelByte(int index)
    {
        int id = index / 8;
        return id < FieldFirstGeneralId || id > FieldLastGeneralId;
    }

    /// <summary>Encode one of the two menu-verified percentages.</summary>
    public static byte[] EncodeTestedValue(int value)
    {
        if (!TestedValues.Contains(value))
        {
            throw new InvalidOperationException(
                "Only the menu-verified CPU three-point values 56 and 57 are enabled");
        }
        return BitConverter.GetBytes(value / 100f);
    }

    /// <summary>Offset of one mapped percentage inside the read window.</summary>
    public static int FieldOffset(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Count(character => character == '.') != 1)
        {
            throw new ArgumentException("Expected a mapped user or CPU GS field");
        }
        int dot = key.IndexOf('.');
        string side = key[..dot];
        string name = key[(dot + 1)..];
        if (side is not ("user" or "cpu") || !GsFieldIds.Ids.TryGetValue(name, out int id))
        {
            throw new ArgumentException($"Unknown in-match GS field: {key}");
        }
        return id * 8 + (side == "cpu" ? 4 : 0);
    }

    /// <summary>Encode any 0-100 percentage for the in-match block.</summary>
    public static byte[] EncodePercent(int value)
    {
        if (value < 0 || value > 100)
        {
            throw new ArgumentException("In-match GS target must be an integer from 0 to 100");
        }
        return BitConverter.GetBytes(value / 100f);
    }

    /// <summary>
    /// The two copies are independent, so editable field slots may legitimately differ between
    /// them. The bytes of the non-editable sentinel slots (general ids 0, 1 and 150..162) must
    /// still match everywhere; that is what proves the layout is the one this mapping came from.
    /// </summary>
    public static bool ConsistentExcept(byte[] primary, byte[] secondary, int offset)
    {
        if (primary.Length != BlockBytes || secondary.Length != BlockBytes)
        {
            return false;
        }
        for (int index = 0; index < BlockBytes; index++)
        {
            if (!IsSentinelByte(index))
            {
                continue;
            }
            if (primary[index] != secondary[index])
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>Connection epoch of the legacy GS mapping: pid, start time, module base, digest.</summary>
public sealed record LegacyGsEpoch(int Pid, long Created, long Base, string Digest)
{
    /// <summary>Process start as Unix seconds, matching the Python evidence units.</summary>
    public double CreatedUnix => (Created - DateTime.UnixEpoch.Ticks) / 10_000_000d;

    public JsonArray ToJsonArray() => new(
        JsonValue.Create((long)Pid), JsonValue.Create(CreatedUnix), JsonValue.Create(Base), JsonValue.Create(Digest));

    public static LegacyGsEpoch FromJson(JsonArray array)
    {
        if (array.Count < 4)
        {
            throw new InvalidOperationException("Legacy GS journal epoch is incomplete");
        }
        double created = array[1]!.GetValue<double>();
        return new LegacyGsEpoch(
            (int)array[0]!.GetValue<long>(),
            DateTime.UnixEpoch.AddSeconds(created).Ticks,
            array[2]!.GetValue<long>(),
            array[3]!.GetValue<string>());
    }
}

/// <summary>Both 40-byte blocks as read, with the Python-derived observation status.</summary>
public sealed class LegacyGsSnapshot
{
    public LegacyGsSnapshot(LegacyGsEpoch epoch, byte[] primary, byte[] secondary)
    {
        Epoch = epoch;
        Primary = primary;
        Secondary = secondary;
        Values = NativeMenuGs.DecodeBlock(primary);
        SecondaryValues = NativeMenuGs.DecodeBlock(secondary);
    }

    public LegacyGsEpoch Epoch { get; }
    public byte[] Primary { get; }
    public byte[] Secondary { get; }
    public Dictionary<string, int> Values { get; }
    public Dictionary<string, int> SecondaryValues { get; }

    public bool AllZero => Values.Values.All(value => value == 0)
        && SecondaryValues.Values.All(value => value == 0);

    public string ObservationStatus => AllZero ? "all_zero_menu_unverified" : "memory_values_menu_unverified";

    public string ObservationNote => AllZero
        ? "Both memory blocks contain zero values; these values have not been verified against the current game menu."
        : "Values were read from memory; these values have not been verified against the current game menu.";

    /// <summary>Differences outside the one validated float make the snapshot unwritable.</summary>
    public bool Writable
    {
        get
        {
            int offset = NativeGsConstants.TargetOffset;
            if (!NativeMenuGs.ConsistentExcept(Primary, Secondary, offset))
            {
                return false;
            }
            return NativeMenuGs.TestedValues.Contains(Values[NativeMenuGs.TargetField])
                && NativeMenuGs.TestedValues.Contains(SecondaryValues[NativeMenuGs.TargetField]);
        }
    }

    /// <summary>Any of the ten sliders is writable when both copies agree outside its four bytes.</summary>
    public bool WritableField(string key) =>
        NativeMenuGs.ConsistentExcept(Primary, Secondary, NativeMenuGs.FieldOffset(key)) && !AllZero;
}

/// <summary>Read-only connection plus one bounded write to the observed primary float.</summary>
public sealed class NativeMenuMemory : IDisposable
{
    private NativeGsConnection? _connection;
    private readonly long[] _blocks;

    private NativeMenuMemory(NativeGsConnection connection, long[] blocks)
    {
        _connection = connection;
        _blocks = blocks;
        Epoch = new LegacyGsEpoch(connection.Pid, connection.Created, connection.Base, connection.Digest);
    }

    public LegacyGsEpoch Epoch { get; }
    public long[] Blocks => _blocks;
    public int WriteAttempts { get; private set; }
    public bool IsOpen => _connection is { IsOpen: true };

    public static NativeMenuMemory Open()
    {
        var connection = NativeGsConnection.Open();
        var memory = new NativeMenuMemory(connection, new[]
        {
            connection.Base + NativeGsConstants.LegacyBlockRva0 + NativeMenuGs.WindowStart,
            connection.Base + NativeGsConstants.LegacyBlockRva1 + NativeMenuGs.WindowStart,
        });
        try
        {
            memory.Guard();
        }
        catch (Exception)
        {
            memory.Dispose();
            throw;
        }
        return memory;
    }

    /// <summary>Re-check process identity and that both blocks stay inside the verified image region.</summary>
    public void Guard()
    {
        if (_connection is null)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS");
        }
        _connection.Guard();
        foreach (long address in _blocks)
        {
            var info = _connection.Query(address);
            if (info.State != NativeGsConstants.MemCommit
                || info.Type != NativeGsConstants.MemImage
                || (info.Protect & 0xFF) is not (4 or 8)
                || (info.Protect & 0x100) != 0
                || info.AllocationBase.ToInt64() != _connection.Base
                || info.BaseAddress.ToInt64() > address
                || address + NativeMenuGs.BlockBytes
                    > info.BaseAddress.ToInt64() + (long)info.RegionSize.ToUInt64())
            {
                throw new InvalidOperationException(
                    "Native GS is no longer in the verified image data region");
            }
        }
    }

    public (byte[] Primary, byte[] Secondary) ReadBlocks()
    {
        Guard();
        if (_connection is null)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS");
        }
        return (_connection.Read(_blocks[0], NativeMenuGs.BlockBytes),
                _connection.Read(_blocks[1], NativeMenuGs.BlockBytes));
    }

    /// <summary>Write four bytes at one slider offset of the primary copy.</summary>
    public void WriteAt(int offset, byte[] raw)
    {
        if (raw.Length != 4)
        {
            throw new InvalidOperationException("Write is outside the four-byte slider width");
        }
        if (offset < 0 || offset + 4 > NativeMenuGs.BlockBytes)
        {
            throw new InvalidOperationException("Write offset is outside the in-match block");
        }
        if (_connection is null)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS");
        }
        Guard();
        IntPtr handle = _connection.OpenWriteHandle();
        try
        {
            Guard();
            WriteAttempts++;
            NativeGsConnection.WriteThrough(handle, _blocks[0] + offset, raw);
        }
        finally
        {
            NativeGsConnection.CloseWriteHandle(handle);
        }
    }

    /// <summary>Write the primary copy's target float through a dedicated write handle.</summary>
    public void WriteTarget(byte[] raw)
    {
        if (raw.Length != 4
            || !NativeMenuGs.TestedValues.Any(value => NativeMenuGs.EncodeTestedValue(value).SequenceEqual(raw)))
        {
            throw new InvalidOperationException("Write is outside the verified native menu values");
        }
        WriteAt(NativeGsConstants.TargetOffset, raw);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}

/// <summary>Journaled legacy GS edits: one field, both copies verified, always undoable.</summary>
public sealed class NativeMenuGsSession : IDisposable
{
    private readonly List<(string Path, JsonObject Transaction, LegacyGsEpoch Epoch)> _undo = new();
    private readonly string _directory;
    private NativeMenuMemory? _memory;
    private string? _lastJournal;

    public NativeMenuGsSession(string? dataDirectory = null)
    {
        _directory = dataDirectory is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "NBA2K27PlayerEditor", "native-gs")
            : Path.Combine(dataDirectory, "native-gs");
    }

    public const string Format = "nba2k27-native-gs-edit/1";
    public LegacyGsEpoch? Epoch { get; private set; }
    public IReadOnlyList<(string Path, JsonObject Transaction)> UndoStack =>
        _undo.Select(entry => (entry.Path, entry.Transaction)).ToList();
    public string LastWarning { get; private set; } = string.Empty;
    public int WriteAttempts => _memory?.WriteAttempts ?? 0;

    public LegacyGsSnapshot Connect()
    {
        Close();
        var memory = NativeMenuMemory.Open();
        _memory = memory;
        Epoch = memory.Epoch;
        try
        {
            return Read();
        }
        catch (Exception)
        {
            Close();
            throw;
        }
    }

    public void Guard(LegacyGsEpoch epoch)
    {
        if (_memory is null || !Equals(epoch, Epoch) || !Equals(_memory.Epoch, epoch))
        {
            throw new InvalidOperationException("Native GS connection changed; read again");
        }
        _memory.Guard();
    }

    public LegacyGsSnapshot Read()
    {
        if (_memory is null || Epoch is null)
        {
            throw new InvalidOperationException("Native GS connection changed; read again");
        }
        Guard(Epoch);
        var (primary, secondary) = _memory.ReadBlocks();
        _ = NativeMenuGs.DecodeBlock(primary);
        _ = NativeMenuGs.DecodeBlock(secondary);
        var again = _memory.ReadBlocks();
        if (!again.Primary.SequenceEqual(primary) || !again.Secondary.SequenceEqual(secondary))
        {
            throw new InvalidOperationException("Native GS changed while reading; read again");
        }
        Guard(Epoch);
        return new LegacyGsSnapshot(Epoch, primary, secondary);
    }

    public LegacyGsSnapshot Apply(LegacyGsSnapshot snapshot, int value) =>
        Commit(snapshot, NativeMenuGs.TargetField, value, allowUnverified: false, "apply", record: true);

    /// <summary>
    /// Write any of the ten in-match sliders. The value itself is not menu-verified, so the
    /// caller is opting into an unverified target while the structural guards stay in force.
    /// </summary>
    public LegacyGsSnapshot ApplyField(LegacyGsSnapshot snapshot, string field, int value) =>
        Commit(snapshot, field, value, allowUnverified: true, "apply", record: true);

    public LegacyGsSnapshot Undo()
    {
        if (_undo.Count == 0)
        {
            throw new InvalidOperationException("No native GS edit to undo in this connection");
        }
        var (path, transaction, epoch) = _undo[^1];
        // The journal stores the epoch as JSON numbers, which cannot round-trip tick precision;
        // keep the exact epoch alongside the record so undo guards against the right process.
        Guard(epoch);
        string field = transaction["field"]!.GetValue<string>();
        var current = Read();
        int after = transaction["after"]!.GetValue<int>();
        if (current.Values[field] != after)
        {
            throw new InvalidOperationException($"{field} changed independently; undo stopped");
        }
        int before = transaction["before"]!.GetValue<int>();
        var fresh = Commit(current, field, before, allowUnverified: true, "undo", record: false);
        _undo.RemoveAt(_undo.Count - 1);
        transaction["status"] = "undone";
        transaction["undo_journal"] = _lastJournal;
        Save(path, transaction);
        return fresh;
    }

    private LegacyGsSnapshot Commit(
        LegacyGsSnapshot snapshot, string field, int value, bool allowUnverified, string operation, bool record)
    {
        LastWarning = string.Empty;
        byte[] raw = allowUnverified ? NativeMenuGs.EncodePercent(value) : NativeMenuGs.EncodeTestedValue(value);
        int offset = NativeMenuGs.FieldOffset(field);
        Guard(snapshot.Epoch);
        bool writable = snapshot.WritableField(field);
        if (!allowUnverified)
        {
            writable = writable
                && NativeMenuGs.TestedValues.Contains(snapshot.Values[field])
                && NativeMenuGs.TestedValues.Contains(snapshot.SecondaryValues[field]);
        }
        if (!writable)
        {
            throw new InvalidOperationException("This native GS snapshot has no verified writable field");
        }
        byte[] beforePrimary = snapshot.Primary;
        byte[] beforeSecondary = snapshot.Secondary;
        var now = Read();
        if (!now.Primary.SequenceEqual(beforePrimary) || !now.Secondary.SequenceEqual(beforeSecondary))
        {
            throw new InvalidOperationException("Native GS changed since it was read; read again");
        }
        byte[] old = beforePrimary.AsSpan(offset, 4).ToArray();
        if (raw.SequenceEqual(old))
        {
            return snapshot;
        }
        byte[] expectedPrimary = (byte[])beforePrimary.Clone();
        raw.CopyTo(expectedPrimary, offset);

        var transaction = new JsonObject
        {
            ["format"] = Format,
            ["id"] = Guid.NewGuid().ToString(),
            ["mode"] = "native_menu",
            ["time"] = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
            ["operation"] = operation,
            ["epoch"] = snapshot.Epoch.ToJsonArray(),
            ["field"] = field,
            ["offset"] = offset,
            ["copy"] = 1,
            ["before"] = snapshot.Values[field],
            ["after"] = value,
            ["value_menu_verified"] = !allowUnverified,
            ["blocks_before_hex"] = new JsonArray(
                JsonValue.Create(Convert.ToHexString(beforePrimary).ToLowerInvariant()),
                JsonValue.Create(Convert.ToHexString(beforeSecondary).ToLowerInvariant())),
            ["blocks_after_hex"] = new JsonArray(
                JsonValue.Create(Convert.ToHexString(expectedPrimary).ToLowerInvariant()),
                JsonValue.Create(Convert.ToHexString(beforeSecondary).ToLowerInvariant())),
            ["status"] = "prepared",
        };
        Directory.CreateDirectory(_directory);
        string path = Path.Combine(_directory,
            $"{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}-{transaction["id"]}.json");
        _lastJournal = path;
        Save(path, transaction);

        bool started = false;
        try
        {
            Guard(snapshot.Epoch);
            var current = Read();
            if (!current.Primary.SequenceEqual(beforePrimary) || !current.Secondary.SequenceEqual(beforeSecondary))
            {
                throw new InvalidOperationException("Native GS changed after preparation; operation stopped");
            }
            started = true;
            _memory!.WriteAt(offset, raw);
            var fresh = Read();
            if (!fresh.Primary.SequenceEqual(expectedPrimary) || !fresh.Secondary.SequenceEqual(beforeSecondary))
            {
                throw new InvalidOperationException("Native GS full-block readback differs from the planned change");
            }
            transaction["status"] = "applied";
            Save(path, transaction);
            if (record)
            {
                _undo.Add((path, transaction, snapshot.Epoch));
            }
            return fresh;
        }
        catch (Exception exc)
        {
            string? rollbackError = null;
            if (started)
            {
                try
                {
                    Guard(snapshot.Epoch);
                    byte[] current = _memory!.ReadBlocks().Primary.AsSpan(offset, 4).ToArray();
                    if (!current.SequenceEqual(old))
                    {
                        for (int index = 0; index < 4; index++)
                        {
                            if (current[index] != old[index] && current[index] != raw[index])
                            {
                                throw new InvalidOperationException(
                                    "In-match GS value changed independently; restoration stopped");
                            }
                        }
                        _memory.WriteAt(offset, old);
                        if (!_memory.ReadBlocks().Primary.AsSpan(offset, 4).SequenceEqual(old))
                        {
                            throw new InvalidOperationException("Native GS restoration readback failed");
                        }
                    }
                }
                catch (Exception restoreError)
                {
                    rollbackError = restoreError.Message;
                }
            }
            transaction["status"] = rollbackError is null ? "failed" : "needs_review";
            transaction["error"] = exc.Message;
            transaction["target_restored"] = started && rollbackError is null;
            transaction["rollback_error"] = rollbackError;
            try
            {
                Save(path, transaction);
            }
            catch (IOException logError)
            {
                LastWarning = $"Failed to update native GS log: {logError.Message}";
            }
            string suffix = rollbackError is null ? string.Empty : $"; restoration incomplete: {rollbackError}";
            throw new InvalidOperationException(exc.Message + suffix, exc);
        }
    }

    /// <summary>Write the journal through a flushed temporary file, mirroring the Python helper.</summary>
    private static void Save(string path, JsonObject document)
    {
        string temporary = Path.ChangeExtension(path, ".tmp");
        if (File.Exists(temporary))
        {
            File.Delete(temporary);
        }
        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            byte[] raw = new UTF8Encoding(false).GetBytes(document.ToJsonString(JournalJson.Options) + "\n");
            stream.Write(raw, 0, raw.Length);
            stream.Flush(flushToDisk: true);
        }
        File.Move(temporary, path, overwrite: true);
    }

    public void Close()
    {
        _memory?.Dispose();
        _memory = null;
        Epoch = null;
        _undo.Clear();
    }

    public void Dispose() => Close();
}
