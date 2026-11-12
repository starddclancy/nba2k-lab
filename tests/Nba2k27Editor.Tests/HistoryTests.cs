using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Tests;

internal static class HistoryTests
{
    public static void Run(Harness h)
    {
        var memory = new SimulatedMemory(0x80000) { BaseAddress = 0x100 };
        var session = new PlayerSession(memory: memory);
        var model = session.Model;
        model.RecoveredIdentity = (memory.Pid, memory.BaseAddress);
        model.RecoveredRoots["GameDataStore"] = 0x100;
        memory.Load(0x200, BitConverter.GetBytes(0x1000L));
        memory.Load(0x1000 + 168, BitConverter.GetBytes(0x2000L));
        foreach (var (key, stride, pointer, countOffset, pointerOffset) in new[]
        {
            ("NBAHistory", 168, 0x8000L, 736, 744),
            ("TeamHistory", 364, 0x10000L, 1296, 1304),
            ("TeamStatsData", 64, 0x18000L, 528, 536),
        })
        {
            memory.Load(0x2000 + countOffset, BitConverter.GetBytes(10));
            memory.Load(0x2000 + pointerOffset, BitConverter.GetBytes(pointer));
            var table = HistorySession.Table(model, key, stride);
            h.Equal($"{key} resolves independent table", pointer, table.Address);
            h.Equal($"{key} uses actual stride", stride, table.Stride);
            memory.Load(0x2000 + countOffset, BitConverter.GetBytes(0));
            h.Throws($"{key} rejects invalid count", () => HistorySession.Table(model, key, stride));
            memory.Load(0x2000 + countOffset, BitConverter.GetBytes(10));
        }
        var teams = new[] { new RecordListItem("Teams", 0, 0x40000, "Fixture Team") };
        memory.WriteWStringFixed(teams[0].Address + 762, "Bulls", 25);
        memory.WriteWStringFixed(teams[0].Address + 812, "Chicago", 18);

        HistorySnapshot Snapshot(string kind, byte[] bytes, long address = 0x8000) =>
            new("token", new HistoryItem($"{kind}:0", kind, 0, null, "Fixture", null, "Fixture", "", ""),
                new HistoryTable("", address, 1, bytes.Length), address, bytes, HistorySession.Fields(kind, bytes).ToArray(),
                new(), Array.Empty<HistoryBinding>(), Array.Empty<string>(), teams, new(0, 0, 0, 0, 0, 0, 0));
        void RoundTrip(HistorySnapshot snapshot, Dictionary<string, object?> changes, string label)
        {
            memory.Load(snapshot.Address, snapshot.Bytes);
            var baseline = memory.Snapshot();
            var overlay = new Overlay(memory);
            HistorySession.Plan(overlay, snapshot, changes);
            var rows = overlay.Patches();
            h.Check($"{label} patches actual bytes", rows.Count > 0);
            Action guard = () => HistorySession.CheckUnchangedBytes(memory, snapshot.Address, snapshot.Bytes, rows);
            HistorySession.ApplyRecord(memory, snapshot.Address,
                overlay.ReadBytes(snapshot.Address, snapshot.Bytes.Length), rows, guard);
            h.Sequence($"{label} after matches plan", overlay.ReadBytes(snapshot.Address, snapshot.Bytes.Length),
                memory.ReadBytes(snapshot.Address, snapshot.Bytes.Length));
            var unresolved = PatchApplier.RestorePatches(memory, rows, guard);
            h.Equal($"{label} undo no unresolved bytes", 0, unresolved.Count);
            h.Sequence($"{label} restores whole memory", baseline, memory.Snapshot());
        }
        var league = new byte[168];
        league[16] = 1;
        var champion = Snapshot("league", league);
        RoundTrip(champion, new() { ["TEAM"] = HistorySession.TeamOption(teams[0]), ["SCORE1"] = 4 }, "champion pointer and caches");
        var planned = new Overlay(memory);
        HistorySession.Plan(planned, champion, new Dictionary<string, object?> { ["TEAM"] = HistorySession.TeamOption(teams[0]) });
        h.Equal("team name cache uses correct offset", "Bulls", planned.ReadAscii(0x8000 + 36, 25));
        h.Equal("team city cache uses correct offset", "Chicago", planned.ReadAscii(0x8000 + 61, 18));
        h.Throws("arbitrary team pointer rejected", () => HistorySession.Plan(new Overlay(memory), champion,
            new Dictionary<string, object?> { ["TEAM"] = "0x123" }));
        h.Throws("history string refuses lossy Unicode", () => HistorySession.Plan(new Overlay(memory), champion,
            new Dictionary<string, object?> { ["FIRSTNAME"] = "中文" }));
        h.Throws("history string refuses overflow", () => HistorySession.Plan(new Overlay(memory), champion,
            new Dictionary<string, object?> { ["FIRSTNAME"] = new string('A', 20) }));
        h.Throws("classification code immutable", () => HistorySession.Plan(new Overlay(memory), champion,
            new Dictionary<string, object?> { ["TYPE"] = 3 }));
        h.Check("champion exposes no overlapping DATA field", champion.Fields.All(f => f.Key != "DATA"));
        h.Check("classification map uses verified 2K27 MVP", HistorySession.Categories[9] == "常规赛 MVP");
        h.Check("classification map uses verified 2K27 leaders", HistorySession.Categories[3] == "得分王");
        league[16] = 3;
        var leader = Snapshot("league", league);
        RoundTrip(leader, new() { ["DATA"] = 31.25, ["FIRSTNAME"] = "Fixture" }, "leader float and name");
        foreach (var bad in new[] { double.NaN, double.PositiveInfinity, -1, 1001 })
        {
            h.Throws($"leader rejects {bad}", () => HistorySession.Plan(new Overlay(memory), leader,
                new Dictionary<string, object?> { ["DATA"] = bad }));
        }
        RoundTrip(Snapshot("totals", new byte[5456]), new() { ["CHAMPIONSHIPS"] = 10, ["HISTORICWIN"] = 60000 }, "team totals");
        RoundTrip(Snapshot("totals", new byte[5456]), new() { ["LASTPLAYOFFYEAR"] = 2008, ["LASTLOTTERYYEAR"] = 2001 }, "team totals: playoff year");
        h.Check("team totals: last playoff opponent is a team choice", HistorySession.Fields("totals", new byte[5456]).Any(f => f.Key == "LASTYEARPLAYOFFSOPPONENT" && f.Type == "team" && f.Writable));
        h.Throws("team totals: playoff year bound", () => HistorySession.Plan(new Overlay(memory), Snapshot("totals", new byte[5456]), new Dictionary<string, object?> { ["LASTPLAYOFFYEAR"] = 70000 }));
        var jerseyBytes = new byte[120];
        jerseyBytes[22] = 2; // two tenures
        h.Check("retired jersey: tenure slots follow NUMTENURES", HistorySession.Fields("jersey", jerseyBytes).Count(f => f.Key.StartsWith("START", StringComparison.Ordinal)) == 2);
        h.Check("retired jersey: links are never editable fields", HistorySession.Fields("jersey", jerseyBytes).All(f => f.Offset >= 18));
        RoundTrip(Snapshot("jersey", jerseyBytes), new() { ["JERSEYNUMBER"] = 33, ["RETIREDYEAR"] = 1993, ["UNLOCALIZEDLASTNAME"] = "Bird", ["START1"] = 1979, ["END1"] = 1992 }, "retired jersey");
        h.Throws("retired jersey: tenure start after end refused", () => HistorySession.Plan(new Overlay(memory), Snapshot("jersey", jerseyBytes), new Dictionary<string, object?> { ["START1"] = 1995, ["END1"] = 1990 }));
        h.Throws("championship storage overflow refused", () => HistorySession.Plan(new Overlay(memory),
            Snapshot("totals", new byte[5456]), new Dictionary<string, object?> { ["CHAMPIONSHIPS"] = 256 }));
        h.Throws("integer count refuses fractional value", () => HistorySession.Plan(new Overlay(memory),
            Snapshot("totals", new byte[5456]), new Dictionary<string, object?> { ["HISTORICWIN"] = 0.5 }));
        var season = Snapshot("season", new byte[64]);
        memory.Load(0x8000, season.Bytes);
        h.Throws("shooting made cannot exceed attempts", () => HistorySession.Plan(new Overlay(memory), season,
            new Dictionary<string, object?> { ["FIELDGOALSMADE"] = 1 }));
        RoundTrip(season, new() { ["TOTALPOINTS"] = 7000, ["FIELDGOALSMADE"] = 500, ["FIELDGOALSATTEMPTED"] = 1000 }, "season");
        h.Equal("second chance stat skips unknown offset 40", 42, season.Fields.Single(f => f.Key == "SECONDCHANCEPOINTS").Offset);
        h.Equal("last team stat stays within 64 bytes", 60, season.Fields.Single(f => f.Key == "POSSESSIONS").Offset);
        var franchiseBytes = new byte[364];
        BitConverter.GetBytes((ushort)1950).CopyTo(franchiseBytes, 360);
        BitConverter.GetBytes((ushort)1963).CopyTo(franchiseBytes, 362);
        var franchise = Snapshot("franchise", franchiseBytes);
        RoundTrip(franchise, new() { ["CITYNAME"] = "New City", ["STARTYEAR"] = 1951 }, "franchise");
        memory.Load(0x8000, franchise.Bytes);
        h.Throws("franchise year range checked", () => HistorySession.Plan(new Overlay(memory), franchise,
            new Dictionary<string, object?> { ["STARTYEAR"] = 1964 }));
        h.Throws("franchise resource name read-only", () => HistorySession.Plan(new Overlay(memory), franchise,
            new Dictionary<string, object?> { ["FILENAME"] = "fake.iff" }));
        var overlayGuard = new Overlay(memory);
        HistorySession.Plan(overlayGuard, franchise, new Dictionary<string, object?> { ["STARTYEAR"] = 1951 });
        memory.Load(0x8000 + 100, new byte[] { 1 });
        h.Throws("guard detects identity and unrelated byte changes", () =>
            HistorySession.CheckUnchangedBytes(memory, 0x8000, franchise.Bytes, overlayGuard.Patches()));
        memory.Load(0x2000 + 128, BitConverter.GetBytes(1));
        memory.Load(0x2000 + 136, BitConverter.GetBytes(0x40000L));
        var bindings = new[] { new HistoryBinding(-1, BitConverter.GetBytes(0x40000L).Concat(BitConverter.GetBytes(1)).ToArray()),
            new HistoryBinding(0x40000 + 4552, memory.ReadBytes(0x40000 + 4552, 52)) };
        HistorySession.ValidateBindings(model, bindings);
        h.Check("unchanged owner table and selectors accepted", true);
        memory.Load(0x40000 + 4552, BitConverter.GetBytes((ushort)2));
        h.Throws("changed selector ownership rejected", () => HistorySession.ValidateBindings(model, bindings));
        memory.Load(0x40000 + 4552, new byte[52]);
        memory.Load(0x2000 + 136, BitConverter.GetBytes(0x50000L));
        h.Throws("relocated owner table rejected", () => HistorySession.ValidateBindings(model, bindings));
        memory.Load(0x2000 + 136, BitConverter.GetBytes(0x40000L));
        memory.Load(0x2000 + 128, BitConverter.GetBytes(2));
        h.Throws("new owner slots invalidate shared acknowledgement", () => HistorySession.ValidateBindings(model, bindings));
        h.Throws("missing numeric value rejected", () => HistorySession.Plan(new Overlay(memory), leader,
            new Dictionary<string, object?> { ["DATA"] = null }));
        memory.Load(0x8000, new byte[168]);
        var baselineFinal = memory.ReadBytes(0x8000, 168);
        var finalRows = new[] { new PatchRow(0x8000 + 24, 0, 1, 1) };
        var wrongExpected = new byte[168];
        wrongExpected[24] = 2;
        h.Throws("whole-record postcondition mismatch rolls back", () =>
            HistorySession.ApplyRecord(memory, 0x8000, wrongExpected, finalRows, () => { }));
        h.Sequence("failed final readback restores pre-write bytes", baselineFinal, memory.ReadBytes(0x8000, 168));
        h.Check("generic team edit excludes all independent-table sections",
            model.GroupedFields("Teams").Where(e => e.Section is "Team History" or "Team Season Stats"
                or "Team Stats Edit" or "Team Latest Record").All(entry =>
                    HistorySession.IsIndependentTeamField(entry, model.FieldVersionPayload(entry.Field))));
        foreach (string kind in new[] { "records", "teamRecords" })
        {
            var fields = HistorySession.Fields(kind, new byte[64]).ToArray();
            h.Check($"{kind} exposes record value and names", fields.Any(f => f.Key == "DATA" && f.Offset == 52 && f.Writable)
                && fields.Any(f => f.Key == "FIRSTNAME" && f.Offset == 12 && f.Writable)
                && fields.Any(f => f.Key == "LASTNAME" && f.Offset == 32 && f.Writable));
            h.Check($"{kind} protects identity and packed date", fields.Any(f => f.Key == "SIGNATUREID" && !f.Writable)
                && fields.Any(f => f.Key == "YEAR" && !f.Writable));
            var recordBytes = new byte[64];
            BitConverter.GetBytes(0xC0000000U | 1962U << 9 | 3U << 5 | 2U).CopyTo(recordBytes, 56);
            var record = Snapshot(kind, recordBytes);
            RoundTrip(record, new() { ["DATA"] = 40283, ["FIRSTNAME"] = "Record" }, $"{kind} large career total and name");
            RoundTrip(record, new() { ["TEAM"] = HistorySession.TeamOption(teams[0]) }, $"{kind} pointer without league caches");
            h.Equal($"{kind} packed year reads independently", 1962,
                (int)HistorySession.ReadValue(recordBytes, record.Fields.Single(f => f.Key == "YEAR"), teams));
            h.Equal($"{kind} packed month preserves raw code", 3,
                (int)HistorySession.ReadValue(recordBytes, record.Fields.Single(f => f.Key == "MONTH"), teams));
            h.Throws($"{kind} year code cannot be edited", () => HistorySession.Plan(new Overlay(memory), record,
                new Dictionary<string, object?> { ["YEAR"] = 2001 }));
            foreach (var invalid in new[] { double.NaN, double.PositiveInfinity, -1D, (double)float.MaxValue * 2 })
            {
                h.Throws($"{kind} rejects nonfinite or out of storage value {invalid}", () =>
                    HistorySession.Plan(new Overlay(memory), record, new Dictionary<string, object?> { ["DATA"] = invalid }));
            }
        }
        var recordSlots = HistoryRecords.League(4000);
        h.Equal("league record prefix includes every raw slot", 3100, recordSlots.Count);
        h.Equal("league regular single game starts at raw zero", 0, recordSlots[0].Index);
        h.Equal("league playoffs use separate rank block", "单场纪录（季后赛）", recordSlots[50].Group);
        h.Equal("league season starts at raw 100", "单赛季纪录", recordSlots[100].Group);
        h.Equal("league season final triple double rank preserved", 10, recordSlots[349].Rank);
        h.Equal("league career starts at raw 350", "生涯累计纪录", recordSlots[350].Group);
        h.Equal("league career total has 100 rows per metric", 100, recordSlots[2849].Rank);
        h.Equal("league unused classification gap retains raw identity", "分类待核对", recordSlots[2850].Group);
        h.Equal("league all typed rows belong to 70 categories", 70,
            recordSlots.Where(s => s.Notice is null).Select(s => (s.Group, s.Metric)).Distinct().Count());
        h.Throws("league routing refuses truncated table", () => HistoryRecords.League(3099));
        var teamSlots = HistoryRecords.Team(model, 5000, 2);
        h.Equal("team record block begins from authored source", 4120, teamSlots[0].Index);
        h.Equal("team record block includes all 510 rows", 510, teamSlots.Count);
        h.Equal("team record block stops before next team", 4629, teamSlots[^1].Index);
        h.Equal("team season points use documented raw offset", "单赛季纪录", teamSlots[100].Group);
        h.Equal("team season points keep the documented metric", "得分", teamSlots[100].Metric);
        h.Equal("team season points preserve all ten ranks", 10, teamSlots[109].Rank);
        h.Equal("team season remaining metric order stays unresolved", "分类待核对", teamSlots[110].Group);
        h.Equal("team career points use documented raw offset", "生涯累计纪录", teamSlots[300].Group);
        h.Equal("team career points keep the documented metric", "得分", teamSlots[300].Metric);
        h.Equal("team career points preserve all ten ranks", 10, teamSlots[309].Rank);
        h.Equal("team career remaining metric order stays unresolved", "分类待核对", teamSlots[310].Group);
        h.Equal("team known rows include both directly evidenced points tables", 120,
            teamSlots.Count(slot => slot.Notice is null));
        h.Equal("team raw block contains no repeated physical row", 510,
            teamSlots.Select(slot => slot.Index).Distinct().Count());
        h.Check("team raw block keeps its exact physical order",
            Enumerable.Range(4120, 510).SequenceEqual(teamSlots.Select(slot => slot.Index)));
        h.Check("team unresolved metric has a concrete notice", teamSlots[110].Notice?.Contains("顺序", StringComparison.Ordinal) == true);
        h.Throws("team routing refuses negative team index", () => HistoryRecords.Team(model, 5000, -1));
        h.Throws("team routing refuses truncated block", () => HistoryRecords.Team(model, 4629, 2));
        memory.Load(0x2000 + 704, BitConverter.GetBytes(40000));
        memory.Load(0x2000 + 712, BitConverter.GetBytes(0x8000L));
        h.Equal("record table supports counts beyond award table bound", 40000, HistorySession.Table(model, "Record", 64).Count);
    }
}
