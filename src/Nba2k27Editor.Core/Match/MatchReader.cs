using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Gs;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Match;

public sealed record MatchStats(Dictionary<string, long> Total, List<Dictionary<string, long>> Periods);
public sealed record MatchPlayer(string Name, int Id, int PhotoId, bool OnCourt,
    long Entity, long PlayerData, long StatsAddress, MatchStats Stats);
public sealed record MatchTeam(int Side, string Label, string[] LogoReferences, int HistoricYearRaw,
    long Address, long RosterAddress, long StatsAddress, MatchStats Stats, List<MatchPlayer> Players);
public sealed record MatchIssue(string Owner, string Field, string Comparison, long Total, long Sum);
/// <summary>
/// The game updated the match while one sample was being read. The sample is discarded, but the
/// connection stays valid: this is expected during live play, so callers retry the next sample.
/// </summary>
public sealed class MatchSampleChangedException(
    string message = "Match changed during reading; discard this sample and refresh")
    : InvalidOperationException(message);

public sealed record MatchSnapshot(int Period, double ClockValue, bool ClockStableDuringRead,
    List<MatchTeam> Teams, List<MatchIssue> Issues, int BytesRead, int ReadCalls, double ElapsedMs)
{
    public bool Editable => false;
    public bool PauseStateVerified => false;
}

/// <summary>One bounded sample of the verified build. Only read/query delegates are accepted.</summary>
public sealed class MatchReader
{
    public const long RootRva = 0xBB25AD8;
    public const long FirstTeamRva = 0xBB25B20;
    public const int StatSize = 0x588;

    /// <summary>
    /// Bytes one sample may read, discovery and verification together. Sized for the largest
    /// list the reader accepts (18 per side, 38 statistics buffers: 120,752 bytes in the offline
    /// fixture). The former 64 KiB rejected every match with 16 or more players per side.
    /// </summary>
    public const int ReadBudget = 131072;

    // Counter windows inside one statistics buffer. Only these bytes must stay unchanged
    // between the first read and verification; other bytes in the buffer are not decoded.
    private static readonly (int Offset, int Size)[] CounterWindows = Enumerable.Range(0, 6)
        .SelectMany(slot => new[]
        {
            (slot * 0xC8, 12), (slot * 0xC8 + 0x12, 8),
            (0x4B0 + slot * 4, 4), (0x4C8 + slot * 0x24, 10),
        })
        .ToArray();

    private static readonly Encoding Wide = new UnicodeEncoding(false, false, true);
    private readonly Func<long, int, byte[]> _read;
    private readonly Func<long, MemoryBasicInformation> _query;
    private readonly long _base;
    private readonly int _budget;
    private readonly Dictionary<(long Address, int Size), byte[]> _bindings = new();
    private readonly List<(long Address, byte[] Raw)> _statBuffers = new();
    private readonly HashSet<long> _entities = new();
    private readonly HashSet<long> _stats = new();
    private readonly MetadataMemory _metadataMemory;
    private int _bytes;
    private int _calls;
    private bool _used;

    public MatchReader(long moduleBase, Func<long, int, byte[]> read,
        Func<long, MemoryBasicInformation> query, int readBudget = ReadBudget)
    {
        if (readBudget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(readBudget), "Read budget must be positive");
        }
        _base = moduleBase;
        _read = read;
        _query = query;
        _budget = readBudget;
        _metadataMemory = new MetadataMemory(Bound);
    }

    /// <summary>
    /// Authored layouts of the few roster fields a sample needs. They are offsets inside a
    /// record and hold no game address, so they are resolved once per process and shared;
    /// every sample still reads the values through its own bound, verified reads. Rebuilding
    /// the embedded layouts for each sample cost about 160 ms of CPU per sample.
    /// </summary>
    private static readonly Lazy<IReadOnlyDictionary<(string Domain, string Name), MetadataField>> MetadataFields =
        new(ResolveMetadataFields, LazyThreadSafetyMode.ExecutionAndPublication);

    private sealed record MetadataField(JsonObject Payload, JsonObject? ParentPayload);

    private static IReadOnlyDictionary<(string Domain, string Name), MetadataField> ResolveMetadataFields()
    {
        var schema = new Schema(new MetadataMemory(static (_, _) =>
                throw new InvalidOperationException("Metadata resolution must not read game memory")),
            new Offsets("NBA2K27.exe"), "NBA2K27.exe");
        var fields = new Dictionary<(string, string), MetadataField>();
        foreach (var (domain, name) in new[]
        {
            ("Teams", "LOGO2"), ("Teams", "BASELOGONAME"), ("Teams", "LOGO1"),
            ("Teams", "HISTORICYEAR"), ("Players", "PHOTOID"),
        })
        {
            var entry = schema.GroupedFields(domain).Single(field => field.NormalizedName == name);
            var payload = schema.FieldVersionPayload(entry.Field);
            fields[(domain, name)] = new(payload, schema.ParentPayload(domain, payload));
        }
        return fields;
    }

    public MatchSnapshot Read()
    {
        if (_used)
        {
            throw new InvalidOperationException("Create a new match reader for each sample");
        }
        _used = true;
        var watch = Stopwatch.StartNew();
        long root = Pointer(_base + RootRva);
        long[] teams = { Pointer(_base + FirstTeamRva), Pointer(_base + FirstTeamRva + 8) };
        Require(teams[0] != teams[1], "Both match sides resolve to the same object");
        long clock = Pointer(root + 0x1160);
        long periodObject = Pointer(clock + 0xFE0);
        int period = checked((int)BitConverter.ToUInt32(Bound(periodObject + 0x1C, 4)));
        Require(period is >= 1 and <= 20, "No supported match period is active");
        byte[] clockBefore = ReadBytes(clock + 0x60, 8);
        double clockValue = BitConverter.ToDouble(clockBefore);
        Require(double.IsFinite(clockValue) && clockValue is >= 0 and <= 3600, "Invalid match clock");
        var sides = new List<MatchTeam>();
        for (int side = 0; side < 2; side++)
        {
            sides.Add(ReadTeam(side, teams[side], teams[1 - side], root));
        }
        foreach (var (key, expected) in _bindings)
        {
            if (!ReadBytes(key.Address, key.Size).SequenceEqual(expected))
            {
                throw new MatchSampleChangedException();
            }
        }
        // One re-read per statistics buffer, comparing the same 24 counter windows that used to
        // be re-read one by one. Same guarantee, about 24 times fewer cross-process calls.
        foreach (var (address, before) in _statBuffers)
        {
            byte[] after = ReadBytes(address, StatSize);
            if (!CounterWindows.All(window =>
                    after.AsSpan(window.Offset, window.Size).SequenceEqual(before.AsSpan(window.Offset, window.Size))))
            {
                throw new MatchSampleChangedException();
            }
        }
        bool clockStable = clockBefore.SequenceEqual(ReadBytes(clock + 0x60, 8));
        var issues = new List<MatchIssue>();
        foreach (var team in sides)
        {
            foreach (var (name, stats) in new[] { (team.Label, team.Stats) }
                .Concat(team.Players.Select(player => (player.Name, player.Stats))))
            {
                foreach (var (field, total) in stats.Total)
                {
                    long sum = stats.Periods.Sum(row => row[field]);
                    if (sum != total)
                    {
                        issues.Add(new(name, field, "periodSum", total, sum));
                    }
                }
            }
            foreach (var (field, total) in team.Stats.Total)
            {
                long sum = team.Players.Sum(player => player.Stats.Total[field]);
                if (sum != total)
                {
                    issues.Add(new(team.Label, field, "playerSum", total, sum));
                }
            }
        }
        return new(period, clockValue, clockStable, sides, issues, _bytes, _calls, watch.Elapsed.TotalMilliseconds);
    }

    private MatchTeam ReadTeam(int side, long team, long opponent, long root)
    {
        Require(Pointer(team + 0x110) == opponent && Pointer(team + 0x130) == root, "Invalid match side links");
        Require(BitConverter.ToUInt32(Bound(team + 0x170, 4)) == side, "Match side index changed");
        Require(BitConverter.ToUInt32(Bound(team + 0x178, 4)) == 5, "Unsupported current lineup size");
        byte[] lineup = Bound(team + 0x180, 40);
        var active = Enumerable.Range(0, 5).Select(index => BitConverter.ToInt64(lineup, index * 8)).ToHashSet();
        Require(active.Count == 5 && active.All(ValidPointer), "Invalid current lineup");
        long head = Pointer(team + 8);
        long roster = Pointer(Pointer(head + 0xB68) + 0x60);
        byte[] identity = Bound(roster + 762, 86);
        string label = $"{Name(identity[50..])} {Name(identity[..50])}".Trim();
        Require(label.Length > 0, "Empty match team name");
        var record = new RecordListItem("Teams", side, roster, label);
        string[] references = new[] { "LOGO2", "BASELOGONAME", "LOGO1" }
            .Select(field => Convert.ToString(Metadata(record, field)) ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        int year = Conversions.ToInt(Metadata(record, "HISTORICYEAR"));
        long statsAddress = Pointer(team + 0x158);
        var players = new List<MatchPlayer>();
        var visited = new HashSet<long>();
        foreach (long first in new[] { head, Pointer(team + 0x18, true) })
        {
            long entity = first;
            while (entity != 0)
            {
                Require(visited.Add(entity) && _entities.Add(entity) && visited.Count <= 18,
                    "Cyclic, shared or oversized match player list");
                Require(Pointer(entity + 0x998) == team, "Player belongs to another match side");
                long data = Pointer(entity + 0xB68);
                Require(Pointer(data + 0x4F8) == entity, "Player entity backlink does not match");
                byte[] flags = Bound(data + 0x128, 6);
                Require((flags[5] & 0x40) != 0, "Player has no active match entity");
                Require(Pointer(data + 0x60) == roster, "Player roster team does not match");
                byte[] names = Bound(data, 80);
                string name = $"{Name(names[40..])} {Name(names[..40])}".Trim();
                Require(name.Length > 0, "Empty match player name");
                int id = BitConverter.ToUInt16(flags);
                int photo = Conversions.ToInt(Metadata(new("Players", id, data, name), "PHOTOID"));
                long stats = Pointer(entity + 0xBA0);
                players.Add(new(name, id, photo, active.Contains(entity), entity, data, stats, ReadStats(stats)));
                entity = Pointer(entity + 0xB90, true);
            }
        }
        Require(active.IsSubsetOf(visited), "Lineup player is outside the match list");
        return new(side, label, references, year, team, roster, statsAddress, ReadStats(statsAddress), players);
    }

    private object Metadata(RecordListItem record, string name)
    {
        // Same decode as Schema.ReadField, with the layout resolved once instead of per sample.
        var field = MetadataFields.Value[(record.Domain, name)];
        long address = FieldIo.FieldAddress(_metadataMemory, record.Address, field.Payload, field.ParentPayload);
        return FieldIo.ReadAuthoredValue(_metadataMemory, address, field.Payload);
    }

    private MatchStats ReadStats(long address)
    {
        Require(_stats.Add(address), "Match statistics buffers unexpectedly alias");
        byte[] raw = ReadBytes(address, StatSize);
        _statBuffers.Add((address, raw));
        return DecodeStats(raw);
    }

    public static MatchStats DecodeStats(byte[] raw)
    {
        Require(raw.Length == StatSize, "Incomplete match statistics");
        var slots = new List<Dictionary<string, long>>();
        for (int slot = 0; slot < 6; slot++)
        {
            int basic = slot * 0xC8;
            int extra = slot * 0x24;
            long offensive = BitConverter.ToUInt16(raw, 0x4B0 + slot * 4);
            long defensive = BitConverter.ToUInt16(raw, 0x4B2 + slot * 4);
            slots.Add(new()
            {
                ["points"] = BitConverter.ToUInt32(raw, basic),
                ["twoPointMade"] = BitConverter.ToUInt16(raw, basic + 4),
                ["threePointMade"] = BitConverter.ToUInt16(raw, basic + 6),
                ["freeThrowMade"] = BitConverter.ToUInt16(raw, basic + 0xA),
                ["twoPointAttempted"] = BitConverter.ToUInt16(raw, basic + 0x12),
                ["threePointAttempted"] = BitConverter.ToUInt16(raw, basic + 0x14),
                ["freeThrowAttempted"] = BitConverter.ToUInt16(raw, basic + 0x18),
                ["offensiveRebounds"] = offensive, ["defensiveRebounds"] = defensive,
                ["rebounds"] = offensive + defensive,
                ["steals"] = BitConverter.ToUInt16(raw, 0x4C8 + extra),
                ["blocks"] = BitConverter.ToUInt16(raw, 0x4CA + extra),
                ["turnovers"] = BitConverter.ToUInt16(raw, 0x4CC + extra),
                ["assists"] = BitConverter.ToUInt16(raw, 0x4D0 + extra),
            });
        }
        return new(slots[0], slots.Skip(1).ToList());
    }

    private byte[] Bound(long address, int size)
    {
        byte[] raw = ReadBytes(address, size);
        var key = (address, size);
        if (_bindings.TryGetValue(key, out byte[]? previous) && !previous.SequenceEqual(raw))
        {
            throw new MatchSampleChangedException("Match identity changed during reading");
        }
        _bindings[key] = raw;
        return raw;
    }

    private long Pointer(long address, bool nullable = false)
    {
        long value = BitConverter.ToInt64(Bound(address, 8));
        Require(nullable && value == 0 || ValidPointer(value), "Invalid match object pointer");
        return value;
    }

    private byte[] ReadBytes(long address, int size)
    {
        Require(size is > 0 and <= 4096 && address >= 0x10000
            && address <= NativeGsConstants.MaxAddress - size + 1, "Invalid match read range");
        Require(_bytes <= _budget - size, "Match read budget exceeded");
        long end = address + size;
        for (long cursor = address; cursor < end;)
        {
            var info = _query(cursor);
            long start = info.BaseAddress.ToInt64();
            ulong length = info.RegionSize.ToUInt64();
            Require(start >= 0 && start <= cursor && length > 0
                && length <= (ulong)(NativeGsConstants.MaxAddress - start + 1), "Invalid match memory region");
            long next = start + (long)length;
            uint protection = info.Protect;
            Require(info.State == NativeGsConstants.MemCommit && (protection & 0x100) == 0
                && (protection & 0xFF) is 0x02 or 0x04 or 0x08 && next > cursor,
                "Match memory page is not readable data");
            cursor = Math.Min(next, end);
        }
        _bytes += size;
        _calls++;
        byte[] raw = _read(address, size);
        Require(raw.Length == size, "Partial match memory read");
        return raw;
    }

    private static bool ValidPointer(long value) =>
        value >= 0x10000 && value <= NativeGsConstants.MaxAddress && value % 8 == 0;

    private static string Name(byte[] raw)
    {
        string text = Wide.GetString(raw).Split('\0')[0];
        Require(!text.Any(char.IsControl), "Invalid match name");
        return text;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    // Schema decoding is shared with roster reads; this adapter cannot write or attach.
    private sealed class MetadataMemory(Func<long, int, byte[]> read) : IMemory
    {
        public bool Simulated => false;
        public int PointerSize => 8;
        public long BaseAddress => 0;
        public int Pid => 0;
        public bool Attached => true;
        public byte[] ReadBytes(long address, int size) => read(address, size);
        public string ReadWString(long address, int maxChars) => Name(read(address, checked(maxChars * 2)));
        public string ReadAscii(long address, int maxChars) => Encoding.Latin1.GetString(read(address, maxChars)).Split('\0')[0];
        public void WriteBytes(long address, byte[] data) => throw new NotSupportedException("Match reads are read-only");
        public void WriteUint32(long address, uint value) => throw new NotSupportedException("Match reads are read-only");
        public void WriteWStringFixed(long address, string text, int maxChars) => throw new NotSupportedException("Match reads are read-only");
        public void WriteAsciiFixed(long address, string text, int maxChars) => throw new NotSupportedException("Match reads are read-only");
        public void Close() { }
    }
}
