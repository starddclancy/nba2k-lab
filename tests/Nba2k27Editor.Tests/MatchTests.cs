using System.Text;
using Nba2k27Editor.Core.Gs;
using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.Tests;

internal static class MatchTests
{
    public static void Run(Harness harness)
    {
        var fixture = new Fixture();
        var reader = fixture.Reader();
        var snapshot = reader.Read();
        harness.Equal("match reads both sides", 2, snapshot.Teams.Count);
        harness.Equal("match reads court and bench", 12, snapshot.Teams.Sum(team => team.Players.Count));
        harness.Equal("match identifies five court players per side", 10,
            snapshot.Teams.Sum(team => team.Players.Count(player => player.OnCourt)));
        harness.Equal("match score uses team stat pointer", 2L, snapshot.Teams[0].Stats.Total["points"]);
        harness.Equal("match period and team sums agree", 0, snapshot.Issues.Count);
        harness.Check("stable clock does not enable writing", snapshot.ClockStableDuringRead
            && !snapshot.PauseStateVerified && !snapshot.Editable);
        harness.Check("match stays inside bounded read budget", snapshot.BytesRead < MatchReader.ReadBudget);
        harness.Equal("match uses live historical logo reference", "logos/logo012uta_2005.iff",
            snapshot.Teams[0].LogoReferences[0]);
        harness.Equal("match decodes historical year bitfield", 3, snapshot.Teams[0].HistoricYearRaw);
        harness.Equal("match decodes portrait bitfield", 123, snapshot.Teams[0].Players[0].PhotoId);
        harness.Throws("reader cannot reuse cached pointers", () => reader.Read());

        var raw = new byte[MatchReader.StatSize];
        for (int slot = 0; slot < 6; slot++)
        {
            Put(raw, 0x4B0 + slot * 4, BitConverter.GetBytes((ushort)(slot + 10)));
            Put(raw, 0x4B2 + slot * 4, BitConverter.GetBytes((ushort)(slot + 20)));
            Put(raw, 0x4D0 + slot * 0x24, BitConverter.GetBytes((ushort)(slot + 30)));
        }
        var decoded = MatchReader.DecodeStats(raw);
        harness.Equal("rebound OT uses four-byte stride", 15L, decoded.Periods[4]["offensiveRebounds"]);
        harness.Equal("defensive rebound OT uses four-byte stride", 25L, decoded.Periods[4]["defensiveRebounds"]);
        harness.Equal("assist OT uses 36-byte stride", 35L, decoded.Periods[4]["assists"]);
        harness.Equal("rebound total is offensive plus defensive", 30L, decoded.Total["rebounds"]);
        harness.Throws("truncated statistic buffer rejected", () => MatchReader.DecodeStats(new byte[16]));

        void Reject(string name, Action<Fixture> alter)
        {
            var sample = new Fixture();
            alter(sample);
            harness.Throws(name, () => sample.Reader().Read());
        }
        Reject("null match root rejected", sample => sample.Long(Fixture.Base + MatchReader.RootRva, 0));
        Reject("unaligned match root rejected", sample => sample.Long(Fixture.Base + MatchReader.RootRva, 0x20001));
        Reject("duplicate sides rejected", sample => sample.Long(Fixture.Base + MatchReader.FirstTeamRva + 8, Fixture.Team(0)));
        Reject("wrong opponent rejected", sample => sample.Long(Fixture.Team(0) + 0x110, Fixture.Team(0)));
        Reject("wrong side index rejected", sample => sample.Int(Fixture.Team(1) + 0x170, 0));
        Reject("unsupported lineup count rejected", sample => sample.Int(Fixture.Team(0) + 0x178, 4));
        Reject("duplicate lineup member rejected", sample => sample.Long(Fixture.Team(0) + 0x188, Fixture.Entity(0, 0)));
        Reject("wrong player team rejected", sample => sample.Long(Fixture.Entity(0, 0) + 0x998, Fixture.Team(1)));
        Reject("wrong player backlink rejected", sample => sample.Long(Fixture.Data(0, 0) + 0x4F8, Fixture.Entity(0, 1)));
        Reject("inactive match entity rejected", sample => sample.Load(Fixture.Data(0, 0) + 0x12D, new byte[] { 0 }));
        Reject("wrong roster team rejected", sample => sample.Long(Fixture.Data(0, 0) + 0x60, Fixture.Roster(1)));
        Reject("cyclic bench list rejected", sample => sample.Long(Fixture.Entity(0, 5) + 0xB90, Fixture.Entity(0, 5)));
        Reject("overlapping bench and court rejected", sample => sample.Long(Fixture.Team(0) + 0x18, Fixture.Entity(0, 0)));
        Reject("shared stat buffers rejected", sample => sample.Long(Fixture.Entity(0, 1) + 0xBA0, Fixture.Stats(0, 0)));
        Reject("unsupported period rejected", sample => sample.Int(0x31000 + 0x1C, 0));
        Reject("nonfinite clock rejected", sample => sample.Load(0x30060, BitConverter.GetBytes(double.NaN)));
        Reject("negative clock rejected", sample => sample.Load(0x30060, BitConverter.GetBytes(-1d)));
        Reject("partial process reads rejected", sample => sample.Filter = (_, bytes) => bytes[..^1]);
        foreach (uint protection in new uint[] { 0x01, 0x10, 0x20, 0x40, 0x80, 0x104 })
        {
            Reject($"page protection {protection:X} rejected", sample => sample.Protection = protection);
        }
        Reject("uncommitted pages rejected", sample => sample.State = 0x2000);
        Reject("invalid queried region rejected", sample => sample.RegionSize = 0);
        Reject("match transition during read rejected", sample =>
        {
            int count = 0;
            sample.Filter = (address, bytes) =>
                address == Fixture.Base + MatchReader.RootRva && ++count > 1
                    ? BitConverter.GetBytes(0x22000L) : bytes;
        });
        // Verification re-reads each statistics buffer once; a counter that differs on that
        // second read must still reject the sample. Each counter window is probed separately.
        foreach (var (offset, label) in new[]
        {
            (0, "points"), (0x12, "attempts"), (0x4B0, "rebounds"), (0x4C8, "steals"),
            (5 * 0xC8, "last period points"), (0x4C8 + 5 * 0x24, "last period steals"),
        })
        {
            Reject($"counter change during read rejected ({label})", sample =>
            {
                int reads = 0;
                sample.Filter = (address, bytes) =>
                {
                    if (address != Fixture.Stats(0, 0) || bytes.Length != MatchReader.StatSize || ++reads < 2)
                    {
                        return bytes;
                    }
                    var changed = (byte[])bytes.Clone();
                    changed[offset] ^= 0x01;
                    return changed;
                };
            });
        }
        var padding = new Fixture();
        int paddingReads = 0;
        padding.Filter = (address, bytes) =>
        {
            if (address != Fixture.Stats(0, 0) || bytes.Length != MatchReader.StatSize || ++paddingReads < 2)
            {
                return bytes;
            }
            var changed = (byte[])bytes.Clone();
            changed[0x0C] ^= 0x01; // between the two basic windows of period 0; never decoded
            return changed;
        };
        harness.Check("undecoded buffer bytes may change without rejecting the sample",
            padding.Reader().Read().Teams.Count == 2 && paddingReads == 2);

        var inconsistent = new Fixture();
        inconsistent.Int(Fixture.TeamStats(0), 9);
        var issues = inconsistent.Reader().Read().Issues;
        harness.Check("period and player discrepancies are reported",
            issues.Any(issue => issue.Comparison == "periodSum")
            && issues.Any(issue => issue.Comparison == "playerSum"));
        var large = new Fixture(18);
        var largeSnapshot = large.Reader().Read();
        harness.Equal("largest accepted list (18 per side) reads within default budget", 36,
            largeSnapshot.Teams.Sum(team => team.Players.Count));
        harness.Check("largest accepted list stays under default budget",
            largeSnapshot.BytesRead < MatchReader.ReadBudget);
        harness.Throws("sample cannot exceed an explicit budget",
            () => new Fixture(18).Reader(readBudget: 65536).Read(), "Match read budget exceeded");
        harness.Throws("non-positive budget rejected", () => new Fixture().Reader(readBudget: 0));
        var counted = new Fixture(13);
        int statReads = 0;
        counted.Filter = (_, bytes) =>
        {
            if (bytes.Length == MatchReader.StatSize)
            {
                statReads++;
            }
            return bytes;
        };
        var countedSnapshot = counted.Reader().Read();
        harness.Equal("each statistics buffer is read twice (sample + verification)", 56, statReads);
        harness.Check("verification no longer re-reads counter windows one by one",
            countedSnapshot.ReadCalls < 700);

        var crossPage = new Fixture();
        bool protectedPageQueried = false;
        var boundaryReader = new MatchReader(Fixture.Base, crossPage.Read, address =>
        {
            if (address >= Fixture.Roster(0) + 0x300 && address < Fixture.Roster(0) + 0x400)
            {
                protectedPageQueried = true;
                return crossPage.Region(address, 0x100, 0x20);
            }
            return crossPage.Region(address, 0x100, 0x04);
        });
        harness.Throws("range crossing into executable data rejected", () => boundaryReader.Read());
        harness.Check("query checks every crossed region", protectedPageQueried);
    }

    private static void Put(byte[] data, int offset, byte[] value) => value.CopyTo(data, offset);

    private sealed class Fixture
    {
        public const long Base = 0x10000000;
        private readonly Dictionary<long, byte> _bytes = new();
        public uint Protection = 0x04;
        public uint State = 0x1000;
        public ulong RegionSize = 0x1000;
        public Func<long, byte[], byte[]>? Filter;
        public static long Team(int side) => 0x40000 + side * 0x2000;
        public static long Roster(int side) => 0x50000 + side * 0x2000;
        public static long Entity(int side, int player) => 0x100000 + (side * 32 + player) * 0x2000;
        public static long Data(int side, int player) => 0x200000 + (side * 32 + player) * 0x2000;
        public static long Stats(int side, int player) => 0x300000 + (side * 32 + player) * 0x2000;
        public static long TeamStats(int side) => 0x400000 + side * 0x2000;

        public Fixture(int players = 6)
        {
            Long(Base + MatchReader.RootRva, 0x20000);
            Long(0x21160, 0x30000);
            Long(0x30FE0, 0x31000);
            Int(0x3101C, 1);
            Load(0x30060, BitConverter.GetBytes(585d));
            for (int side = 0; side < 2; side++)
            {
                long team = Team(side);
                Long(Base + MatchReader.FirstTeamRva + side * 8, team);
                Long(team + 0x110, Team(1 - side));
                Long(team + 0x130, 0x20000);
                Int(team + 0x170, side);
                Int(team + 0x178, 5);
                Long(team + 8, Entity(side, 0));
                Long(team + 0x18, Entity(side, 5));
                Long(team + 0x158, TeamStats(side));
                Load(Roster(side) + 762, Encoding.Unicode.GetBytes("Jazz"));
                Load(Roster(side) + 812, Encoding.Unicode.GetBytes("Utah"));
                Load(Roster(side) + 506, Encoding.Latin1.GetBytes("logos/logo012uta_2005.iff"));
                Int(Roster(side) + 5448, 3 << 19);
                for (int player = 0; player < players; player++)
                {
                    long entity = Entity(side, player);
                    long data = Data(side, player);
                    if (player < 5)
                    {
                        Long(team + 0x180 + player * 8, entity);
                    }
                    Long(entity + 0x998, team);
                    Long(entity + 0xB68, data);
                    Long(entity + 0xBA0, Stats(side, player));
                    Long(entity + 0xB90, player == 4 || player == players - 1 ? 0 : Entity(side, player + 1));
                    Long(data + 0x4F8, entity);
                    Long(data + 0x60, Roster(side));
                    Load(data + 0x128, BitConverter.GetBytes((ushort)(side * 32 + player)));
                    Load(data + 0x12D, new byte[] { 0x40 });
                    Load(data, Encoding.Unicode.GetBytes($"Player{player}"));
                    Load(data + 40, Encoding.Unicode.GetBytes($"Side{side}"));
                    Int(data + 1048, 123 << 16);
                }
            }
            Int(Stats(0, 0), 2);
            Int(Stats(0, 0) + 0xC8, 2);
            Int(TeamStats(0), 2);
            Int(TeamStats(0) + 0xC8, 2);
        }

        public void Load(long address, byte[] bytes)
        {
            for (int index = 0; index < bytes.Length; index++)
            {
                _bytes[address + index] = bytes[index];
            }
        }
        public void Long(long address, long value) => Load(address, BitConverter.GetBytes(value));
        public void Int(long address, int value) => Load(address, BitConverter.GetBytes(value));
        public byte[] Read(long address, int size)
        {
            byte[] result = Enumerable.Range(0, size).Select(index => _bytes.GetValueOrDefault(address + index)).ToArray();
            return Filter?.Invoke(address, result) ?? result;
        }
        public MemoryBasicInformation Region(long address, ulong length, uint protection) => new()
        {
            BaseAddress = (IntPtr)(address / (long)length * (long)length),
            RegionSize = (UIntPtr)length, State = State, Protect = protection,
        };
        public MatchReader Reader(int readBudget = MatchReader.ReadBudget) => new(Base, Read, address => RegionSize == 0
            ? new MemoryBasicInformation() : Region(address, RegionSize, Protection), readBudget);
    }
}
