using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Gs;

/// <summary>The 70 native GS slider records and their internal enum ids.</summary>
public static class GsFieldIds
{
    public static readonly IReadOnlyDictionary<string, int> Ids = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["open_shot_bonus"] = 23,
        ["covered_shot_penalty"] = 24,
        ["cpu_shot_timing"] = 22,
        ["inside_shot"] = 27,
        ["close_shot"] = 28,
        ["mid_range"] = 29,
        ["three_point"] = 30,
        ["layup"] = 31,
        ["free_throw_meter_size"] = 32,
        ["contact_dunk_frequency"] = 33,
        ["contact_dunk_success"] = 34,
        ["pass_accuracy"] = 36,
        ["alley_oop_success"] = 25,
        ["contact_shot_success"] = 26,
        ["ball_security"] = 37,
        ["body_contact_impact"] = 38,
        ["pass_speed"] = 39,
        ["driving_contact_frequency"] = 74,
        ["inside_contact_frequency"] = 75,
        ["layup_defense_takeoff"] = 76,
        ["layup_defense_release"] = 77,
        ["jump_shot_defense_takeoff"] = 78,
        ["jump_shot_defense_release"] = 79,
        ["help_defense"] = 138,
        ["steal_success"] = 73,
        ["agility"] = 13,
        ["vertical"] = 20,
        ["strength"] = 12,
        ["stamina"] = 18,
        ["speed"] = 4,
        ["durability"] = 17,
        ["hustle"] = 14,
        ["ball_handling"] = 6,
        ["hands"] = 19,
        ["dunking"] = 7,
        ["on_ball_defense"] = 21,
        ["stealing"] = 2,
        ["blocking"] = 3,
        ["offensive_awareness"] = 8,
        ["defensive_awareness"] = 9,
        ["offensive_rebounding"] = 10,
        ["defensive_rebounding"] = 11,
        ["offensive_consistency"] = 15,
        ["defensive_consistency"] = 16,
        ["fatigue_rate"] = 126,
        ["inside_shot_tendency"] = 139,
        ["close_shot_tendency"] = 140,
        ["mid_range_tendency"] = 141,
        ["three_point_tendency"] = 142,
        ["post_shot_tendency"] = 137,
        ["drive_tendency"] = 143,
        ["post_up_tendency"] = 144,
        ["alley_oop_pass_tendency"] = 145,
        ["dunk_tendency"] = 130,
        ["putback_tendency"] = 132,
        ["pass_interception_tendency"] = 134,
        ["on_ball_steal_tendency"] = 135,
        ["contest_shot_tendency"] = 136,
        ["backdoor_cut_tendency"] = 35,
        ["over_the_back_foul_frequency"] = 116,
        ["charging_foul_frequency"] = 117,
        ["blocking_foul_frequency"] = 118,
        ["reaching_foul_frequency"] = 119,
        ["shooting_foul_frequency"] = 120,
        ["loose_ball_foul_frequency"] = 121,
        ["illegal_screen_frequency"] = 122,
        ["speed_max"] = 146,
        ["speed_min"] = 147,
        ["acceleration_max"] = 148,
        ["acceleration_min"] = 149,
    };

    public static readonly string[] Sides = { "user", "cpu" };

    /// <summary>Offset of one mapped percentage inside the 1296-byte array.</summary>
    public static int FieldOffset(string key)
    {
        if (string.IsNullOrEmpty(key) || key.Count(character => character == '.') != 1)
        {
            throw new ArgumentException("Expected a mapped user or CPU GS field");
        }
        int dot = key.IndexOf('.');
        string side = key[..dot];
        string name = key[(dot + 1)..];
        if (side is not ("user" or "cpu") || !Ids.TryGetValue(name, out int id))
        {
            throw new ArgumentException("This native GS field has no confirmed menu mapping");
        }
        return id * 8 + (side == "cpu" ? 4 : 0);
    }

    public static byte[] PercentBytes(int value)
    {
        if (value < 0 || value > 100)
        {
            throw new ArgumentException("Native GS target must be an integer from 0 to 100");
        }
        return BitConverter.GetBytes((float)value / 100f);
    }

    /// <summary>Decode one float as an integer percentage; zero stays valid.</summary>
    public static int CheckRaw(byte[] raw)
    {
        if (raw.Length != 4)
        {
            throw new ArgumentException("Expected one native GS float");
        }
        float value = BitConverter.ToSingle(raw, 0);
        if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1
            || Math.Abs(value * 100 - Math.Round(value * 100)) > 0.0001)
        {
            throw new ArgumentException("Native GS float is not an integer percentage");
        }
        return (int)Math.Round(value * 100);
    }

    public static Dictionary<string, int> DecodeBlock(byte[] raw)
    {
        if (raw.Length != NativeGsConstants.BlockSize)
        {
            throw new ArgumentException("Expected one complete 1296-byte native GS array");
        }
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in Ids)
        {
            for (int side = 0; side < Sides.Length; side++)
            {
                int offset = pair.Value * 8 + side * 4;
                float value = BitConverter.ToSingle(raw, offset);
                if (float.IsNaN(value) || float.IsInfinity(value) || value < 0 || value > 1
                    || Math.Abs(value * 100 - Math.Round(value * 100)) > 0.0001)
                {
                    throw new ArgumentException($"Unsupported native GS percentage: {Sides[side]}.{pair.Key}");
                }
                values[$"{Sides[side]}.{pair.Key}"] = (int)Math.Round(value * 100);
            }
        }
        return values;
    }
}

public sealed record GsEpoch(int Pid, long Created, long Base, string Digest)
{
    /// <summary>Process start as Unix seconds, matching the Python evidence units.</summary>
    public double CreatedUnix => (Created - DateTime.UnixEpoch.Ticks) / 10_000_000d;

    public JsonArray ToJsonArray() => new(
        JsonValue.Create((long)Pid), JsonValue.Create(CreatedUnix), JsonValue.Create(Base), JsonValue.Create(Digest));
}

public sealed class GeneralGsSnapshot
{
    public GeneralGsSnapshot(GsEpoch epoch, long root, IReadOnlyList<(long Start, long Length, long Allocation, uint AllocationProtect, uint State, uint Protect, uint Type)> allocation, IReadOnlyList<byte[]> blocks)
    {
        Epoch = epoch;
        Root = root;
        Allocation = allocation;
        Blocks = blocks;
    }

    public GsEpoch Epoch { get; }
    public long Root { get; }
    public IReadOnlyList<(long Start, long Length, long Allocation, uint AllocationProtect, uint State, uint Protect, uint Type)> Allocation { get; }
    public IReadOnlyList<byte[]> Blocks { get; }

    public Dictionary<string, int> Values => GsFieldIds.DecodeBlock(Blocks[0]);
}

/// <summary>Read-only connection that discovers the dynamic GS root and both arrays.</summary>
public sealed class NativeGeneralGsReader : IDisposable
{
    private readonly NativeGsConnection _connection;

    public NativeGeneralGsReader(NativeGsConnection? connection = null)
    {
        _connection = connection ?? NativeGsConnection.Open();
        Epoch = new GsEpoch(_connection.Pid, _connection.Created, _connection.Base, _connection.Digest);
        GuardAttachment();
    }

    public GsEpoch Epoch { get; }
    public NativeGsConnection Connection => _connection;
    public int WriteAttempts => _connection.WriteAttempts;

    private void GuardAttachment()
    {
        if (!_connection.IsOpen)
        {
            throw new InvalidOperationException("Native GS reader is closed; reconnect");
        }
        if (_connection.WriteAttempts != 0)
        {
            throw new InvalidOperationException("Native GS reader requires zero write attempts");
        }
        CheckAddress(_connection.Base, NativeGsConstants.RootSlotRva + 8, NativeGsConstants.PageSize);
        _connection.Guard();
    }

    public static void CheckAddress(long address, long size, int alignment = 1)
    {
        if (size <= 0 || address < 0x10000 || address > NativeGsConstants.MaxAddress
            || size - 1 > NativeGsConstants.MaxAddress - address || address % alignment != 0)
        {
            throw new InvalidOperationException(
                "Native GS address is null, noncanonical, misaligned, or out of bounds");
        }
    }

    /// <summary>Query every covered page and return the region identities in address order.</summary>
    private List<(long, long, long, uint, uint, uint, uint)> CheckWindow(
        long address, int size, uint kind, long? allocation = null)
    {
        CheckAddress(address, size);
        long cursor = address;
        long end = address + size;
        var regions = new List<(long, long, long, uint, uint, uint, uint)>();
        long? expected = allocation;
        while (cursor < end)
        {
            var info = _connection.Query(cursor);
            long start = info.BaseAddress.ToInt64();
            long allocationBase = info.AllocationBase.ToInt64();
            long length = (long)info.RegionSize.ToUInt64();
            if (info.State != NativeGsConstants.MemCommit || info.Type != kind
                || (info.Protect & 0xFF) is not (2 or 4 or 8) || (info.Protect & 0x100) != 0
                || allocationBase < 0x10000 || allocationBase > cursor
                || start < allocationBase || start > cursor || length <= 0
                || start + length <= cursor || start + length - 1 > NativeGsConstants.MaxAddress)
            {
                throw new InvalidOperationException("Native GS region is invalid, unreadable, or executable");
            }
            expected ??= allocationBase;
            if (allocationBase != expected)
            {
                throw new InvalidOperationException("Native GS window changed allocation or image ownership");
            }
            var identity = (start, length, allocationBase, info.AllocationProtect, info.State, info.Protect, info.Type);
            if (regions.Count == 0 || !regions[^1].Equals(identity))
            {
                regions.Add(identity);
            }
            long page = (cursor / NativeGsConstants.PageSize + 1) * NativeGsConstants.PageSize;
            cursor = Math.Min(end, Math.Min(start + length, page));
        }
        return regions;
    }

    private (long Root, List<(long, long, long, uint, uint, uint, uint)> Allocation) Source()
    {
        GuardAttachment();
        long slot = _connection.Base + NativeGsConstants.RootSlotRva;
        var image = CheckWindow(slot, 8, NativeGsConstants.MemImage, _connection.Base);
        var rawRoot = _connection.Read(slot, 8);
        long root = (long)BitConverter.ToUInt64(rawRoot, 0);
        CheckAddress(root, NativeGsConstants.RootWindowSize, 8);
        var allocation = CheckWindow(root, NativeGsConstants.RootWindowSize, NativeGsConstants.MemPrivate);
        if (!_connection.Read(slot, 8).SequenceEqual(rawRoot))
        {
            throw new InvalidOperationException("Native GS root changed while reading; read again");
        }
        if (!CheckWindow(slot, 8, NativeGsConstants.MemImage, _connection.Base).SequenceEqual(image))
        {
            throw new InvalidOperationException("Native GS root-slot region changed while reading");
        }
        if (!CheckWindow(root, NativeGsConstants.RootWindowSize, NativeGsConstants.MemPrivate).SequenceEqual(allocation))
        {
            throw new InvalidOperationException("Native GS allocation identity changed while reading");
        }
        GuardAttachment();
        return (root, allocation);
    }

    private void VerifySource(long root, List<(long, long, long, uint, uint, uint, uint)> allocation)
    {
        var (currentRoot, currentAllocation) = Source();
        if (currentRoot != root)
        {
            throw new InvalidOperationException("Native GS root changed while reading; read again");
        }
        if (!currentAllocation.SequenceEqual(allocation))
        {
            throw new InvalidOperationException("Native GS allocation identity changed; read again");
        }
    }

    private List<byte[]> ReadBlocks(long root, List<(long, long, long, uint, uint, uint, uint)> allocation)
    {
        var blocks = new List<byte[]>();
        foreach (int offset in new[] { NativeGsConstants.ArrayOffset, NativeGsConstants.ArrayOffset + NativeGsConstants.CopyStride })
        {
            if (!CheckWindow(root, NativeGsConstants.RootWindowSize, NativeGsConstants.MemPrivate).SequenceEqual(allocation))
            {
                throw new InvalidOperationException("Native GS allocation identity changed while reading");
            }
            blocks.Add(_connection.Read(root + offset, NativeGsConstants.BlockSize));
            if (!CheckWindow(root, NativeGsConstants.RootWindowSize, NativeGsConstants.MemPrivate).SequenceEqual(allocation))
            {
                throw new InvalidOperationException("Native GS allocation identity changed while reading");
            }
        }
        return blocks;
    }

    public GeneralGsSnapshot Read()
    {
        try
        {
            var (root, allocation) = Source();
            var blocks = ReadBlocks(root, allocation);
            VerifySource(root, allocation);
            var repeated = ReadBlocks(root, allocation);
            VerifySource(root, allocation);
            if (!blocks[0].SequenceEqual(repeated[0]) || !blocks[1].SequenceEqual(repeated[1]))
            {
                throw new InvalidOperationException("Native GS arrays changed while reading; read again");
            }
            foreach (var raw in blocks)
            {
                GsFieldIds.DecodeBlock(raw);
            }
            return new GeneralGsSnapshot(Epoch, root, allocation, blocks);
        }
        catch (Exception)
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Check source identity without requiring unchanged array contents.</summary>
    public void Guard(GeneralGsSnapshot snapshot)
    {
        try
        {
            if (snapshot.Epoch != Epoch)
            {
                throw new InvalidOperationException("Native GS snapshot belongs to another process or build");
            }
            VerifySource(snapshot.Root, snapshot.Allocation.ToList());
        }
        catch (Exception)
        {
            Dispose();
            throw;
        }
    }

    public void Dispose() => _connection.Dispose();
}
