using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

/// <summary>Per-byte transactional writes with precondition, rollback and identity guards.</summary>
public static class PatchApplier
{
    public static void WritePatchByte(IMemory memory, PatchRow row, PlayerPatchGuard? playerGuard = null)
    {
        playerGuard?.Begin(row);
        try
        {
            memory.WriteBytes(row.Address, new[] { row.After });
        }
        finally
        {
            // A failed one-byte write may still have changed this exact name byte.
            playerGuard?.ResolvePending(playerGuard.OwnerOf(row.Address));
        }
    }

    public static IReadOnlyList<(long Address, string Error)> RestorePatches(
        IMemory memory, IReadOnlyList<PatchRow> rows, Action? guard = null, PlayerPatchGuard? playerGuard = null)
    {
        var unresolved = new List<(long Address, string Error)>();
        foreach (var row in rows.Reverse())
        {
            try
            {
                guard?.Invoke();
                playerGuard?.Check(row);
                byte current = memory.ReadBytes(row.Address, 1)[0];
                if ((current & row.Mask) == (row.Before & row.Mask))
                {
                    continue;
                }
                if ((current & row.Mask) != (row.After & row.Mask))
                {
                    throw new InvalidOperationException("Changed bits no longer match the transaction");
                }
                byte restored = (byte)((current & ~row.Mask) | (row.Before & row.Mask));
                playerGuard?.Check(row);
                WritePatchByte(memory, new PatchRow(row.Address, current, restored, row.Mask), playerGuard);
                if (memory.ReadBytes(row.Address, 1)[0] != restored)
                {
                    throw new InvalidOperationException("Rollback readback failed");
                }
                guard?.Invoke();
                playerGuard?.Check(row);
            }
            catch (Exception exc)
            {
                unresolved.Add((row.Address, exc.Message));
            }
        }
        return unresolved;
    }

    public static void ApplyPatches(
        IMemory memory, IReadOnlyList<PatchRow> rows, Action guard, PlayerPatchGuard? playerGuard = null)
    {
        guard();
        foreach (var row in rows)
        {
            guard();
            if (memory.ReadBytes(row.Address, 1)[0] != row.Before)
            {
                throw new InvalidOperationException("Game data changed during preparation; read this player again");
            }
        }
        var completed = new List<PatchRow>();
        try
        {
            foreach (var row in rows)
            {
                guard();
                playerGuard?.Check(row);
                if (memory.ReadBytes(row.Address, 1)[0] != row.Before)
                {
                    throw new InvalidOperationException("Game data changed before write; operation stopped");
                }
                playerGuard?.Check(row);
                // Include the in-flight byte: a partial write may still change it.
                completed.Add(row);
                WritePatchByte(memory, row, playerGuard);
                if (memory.ReadBytes(row.Address, 1)[0] != row.After)
                {
                    throw new InvalidOperationException("Write readback failed; operation stopped");
                }
                playerGuard?.Check(row);
            }
            guard();
            playerGuard?.Check(finalValues: true);
            guard();
        }
        catch (Exception exc)
        {
            var unresolved = RestorePatches(memory, completed, guard, playerGuard);
            if (unresolved.Count > 0)
            {
                var details = string.Join("; ", unresolved.Select(row => $"0x{row.Address:X}: {row.Error}"));
                throw new InvalidOperationException($"{exc.Message}; rollback incomplete: {details}", exc);
            }
            throw new InvalidOperationException($"{exc.Message}; completed writes were rolled back", exc);
        }
    }
}

/// <summary>Track each participant's exact identity through partial name writes.</summary>
public sealed class PlayerPatchGuard
{
    private readonly Dictionary<long, PlayerTransactionRecord> _players;
    private readonly Dictionary<long, byte[]> _identities;
    private readonly Dictionary<long, long> _owners = new();
    private readonly Dictionary<long, (int Offset, PatchRow Row)> _pending = new();
    private readonly Func<IReadOnlyList<RecordListItem>> _members;
    private readonly Func<RecordListItem, byte[]> _identityReader;
    private readonly Func<string, FieldSpec> _specLookup;
    private readonly Func<FieldSpec, RecordListItem, long> _addressOf;

    public PlayerPatchGuard(
        IEnumerable<(RecordListItem Item, PlayerTransactionRecord Player)> participants,
        Func<IReadOnlyList<RecordListItem>> members,
        Func<RecordListItem, byte[]> identityReader,
        Func<string, FieldSpec> specLookup,
        Func<FieldSpec, RecordListItem, long> addressOf,
        bool undo = false)
    {
        _members = members;
        _identityReader = identityReader;
        _specLookup = specLookup;
        _addressOf = addressOf;
        IsUndo = undo;
        _players = new Dictionary<long, PlayerTransactionRecord>();
        _identities = new Dictionary<long, byte[]>();
        foreach (var (item, player) in participants)
        {
            _players[item.Address] = player;
            _identities[item.Address] = Convert.FromHexString(undo ? player.IdentityAfter : player.Identity);
            foreach (var address in player.PatchAddresses)
            {
                _owners[address] = item.Address;
            }
        }
    }

    public bool IsUndo { get; }
    public IMemory? Memory { get; set; }

    public long OwnerOf(long address) => _owners[address];

    public void Begin(PatchRow row)
    {
        long owner = _owners[row.Address];
        long offset = row.Address - owner;
        if (offset >= 0 && offset < _identities[owner].Length)
        {
            _pending[owner] = ((int)offset, row);
        }
    }

    public void ResolvePending(long owner)
    {
        if (!_pending.TryGetValue(owner, out var pending))
        {
            return;
        }
        var record = _players[owner];
        byte current = _identityReader(record.Item)[pending.Offset];
        if (current != pending.Row.Before && current != pending.Row.After)
        {
            throw new InvalidOperationException("Player identity changed during a name write");
        }
        _identities[owner][pending.Offset] = current;
        _pending.Remove(owner);
    }

    public void Check(PatchRow? row = null, bool finalValues = false)
    {
        var owners = row is null ? _players.Keys.ToList() : new List<long> { _owners[row.Address] };
        var members = _members();
        foreach (var owner in owners)
        {
            ResolvePending(owner);
            var record = _players[owner];
            if (!members.Contains(record.Item)
                || !_identityReader(record.Item).SequenceEqual(_identities[owner]))
            {
                throw new InvalidOperationException("Player identity changed during the transaction");
            }
            foreach (var pair in record.FieldAddresses)
            {
                if (_addressOf(_specLookup(pair.Key), record.Item) != pair.Value)
                {
                    throw new InvalidOperationException("Player data address changed during the transaction");
                }
            }
            if (!finalValues)
            {
                continue;
            }
            foreach (var change in record.Changes)
            {
                var spec = _specLookup(change.Field);
                var current = Model.FieldIo.ReadAuthoredValue(
                    Memory!, record.FieldAddresses[change.Field], spec.Payload);
                var expected = IsUndo ? change.Before : change.ExpectedRaw;
                if (!SameValue(current, expected))
                {
                    throw new InvalidOperationException("Edited values changed before completion");
                }
            }
        }
    }

    public static bool SameValue(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }
        if (left is byte[] leftBytes && right is byte[] rightBytes)
        {
            return leftBytes.SequenceEqual(rightBytes);
        }
        if (IsNumber(left) && IsNumber(right))
        {
            return Model.Conversions.ToDouble(left) == Model.Conversions.ToDouble(right);
        }
        return left.Equals(right) || left.ToString() == right.ToString();
    }

    private static bool IsNumber(object value) =>
        value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal;
}
