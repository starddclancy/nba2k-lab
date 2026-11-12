using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.Tests;

internal static class ProspectTests
{
    public static void Run(Harness harness)
    {
        var memory = new SimulatedMemory(0x4000);
        var schema = new Schema(memory, new Offsets("NBA2K27.exe"), "NBA2K27.exe");
        var prospect = new RecordListItem("Players", 3797, 0x100, "Kevin Durant");
        var historic = new RecordListItem("Players", 2483, 0x1000, "Kevin Durant");
        var inactive = new RecordListItem("Players", 12, 0x2000, "Other");
        void Set(RecordListItem item, string name, object value)
        {
            var entry = schema.FieldByName("Players", name)!;
            var payload = schema.FieldVersionPayload(entry.Field);
            long address = FieldIo.FieldAddress(memory, item.Address, payload,
                schema.ParentPayload("Players", payload));
            FieldIo.WriteAuthoredValue(memory, address, payload, value);
        }
        foreach (var item in new[] { prospect, historic, inactive })
        {
            Set(item, "DRAFTPROSPECTINDEX", item == historic ? 255 : 1);
            Set(item, "YEARDRAFTEDINTERNAL", 107);
            Set(item, "ROUNDDRAFTED", item == historic ? 1 : 0);
            Set(item, "DRAFTPICKNUMBER", item == historic ? 2 : 0);
            Set(item, "BIRTHYEAR", 1988);
            Set(item, "ISACTIVEPRIVATE", item == inactive ? 0 : 1);
            Set(item, "TYPE", item == historic ? 7 : 0);
        }
        int writes = memory.Writes;
        var catalog = ProspectBrowser.Read(schema, new[] { prospect, historic, inactive });
        harness.Equal("prospects: all metadata read", 3, catalog.Records.Count);
        harness.Equal("prospects: no read failures", 0, catalog.Errors.Count);
        harness.Equal("prospects: read only", writes, memory.Writes);
        var matched = catalog.Records.Where(row => row.IsProspect).ToList();
        harness.Equal("prospects: one active undrafted player", 1, matched.Count);
        harness.Equal("prospects: distinguish identical names by record", 3797, matched[0].Item.Index);
        harness.Equal("prospects: year conversion", 2007, matched[0].DraftYear);
        harness.Equal("prospects: birth year", 1988, matched[0].BirthYear);
        harness.Equal("prospects: standalone source is not fabricated", prospect, matched[0].Item);
        harness.Equal("prospects: index zero is included", true, (matched[0] with { ProspectIndex = 0 }).IsProspect);
        harness.Equal("prospects: index 149 is included", true, (matched[0] with { ProspectIndex = 149 }).IsProspect);
        harness.Equal("prospects: index outside supported class", false, (matched[0] with { ProspectIndex = 150 }).IsProspect);
        harness.Equal("prospects: selected round excludes player", false, (matched[0] with { Round = 1 }).IsProspect);
        harness.Equal("prospects: selected pick excludes player", false, (matched[0] with { Pick = 1 }).IsProspect);
        var failed = ProspectBrowser.Read(schema, new[] { prospect with { Address = 0x4000 } });
        harness.Equal("prospects: failed read is not a candidate", 0, failed.Records.Count);
        harness.Equal("prospects: read error retains record index", true, failed.Errors.ContainsKey(3797));
        var session = new PlayerSession(memory: memory);
        harness.Throws("sources: unknown source rejected", () => session.MemberItems("drafft"));
        foreach (var sample in new[] { ("HEIGHT", (object)20800L), ("WINGSPANINTENTHSOFMM", 22500L),
            ("WEIGHT", 210L), ("DRAFTPROSPECTINDEX", 1L), ("YEARDRAFTEDINTERNAL", 107L),
            ("BIRTHYEAR", 1988L), ("ROUNDDRAFTED", 0L), ("TYPE", 0L) })
        {
            var spec = session.Specs.Values.First(spec => spec.Name == sample.Item1
                && (sample.Item1 != "TYPE" || spec.Key == "Advanced/Other/TYPE"));
            object display = session.Decode(spec, sample.Item2);
            object encoded = session.Encode(spec, display);
            harness.Equal($"codec: {sample.Item1} raw/display roundtrip",
                Conversions.ToDouble(sample.Item2), Conversions.ToDouble(encoded), 0.00001);
        }
    }
}
