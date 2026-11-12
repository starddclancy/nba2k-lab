using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Tests;

internal static class DropdownMergeTests
{
    public static void Run(Harness harness)
    {
        RealActions(harness);
        UniqueAndAmbiguous(harness);
    }

    private static JsonObject Field(JsonObject layout, string group, string name, string? version = null) =>
        ((JsonArray)layout["Signature"]![group]!).OfType<JsonObject>()
            .Single(field => field["normalized_name"]!.GetValue<string>() == name
                && (version is null || ((JsonObject)field["versions"]!).Any(pair =>
                    Offsets.VersionKeyMatches(pair.Key, version))));

    private static JsonObject WithoutOptions(JsonObject payload)
    {
        var result = (JsonObject)payload.DeepClone();
        foreach (string key in new[] { "dropdown", "values", "id_prefixed_dropdown" }) result.Remove(key);
        return result;
    }

    private static void RealActions(Harness harness)
    {
        var offsets = new Offsets("NBA2K27.exe");
        var original = (JsonObject)offsets.Resource("offsets_players.json")["Players"]!;
        var dropdowns = (JsonObject)offsets.Resource("dropdowns.json")["Players"]!;
        var layout = offsets.EditorLayout("Players");
        foreach (var (name, sourceGroup, raw, expected) in new[]
        {
            ("FREETHROW", "Jump Shooting", 259, "FREE_THROW_21"),
            ("DRIBBLEPULLUP", "Ball Handling", 2, "PRO_2"),
            ("GOTOSHOT", "Misc", 0, "NONE"),
        })
        {
            var field = Field(layout, "Jump Shooting II", name);
            var payload = (JsonObject)field["versions"]!["2K27"]!;
            var source = (JsonArray)Field(dropdowns, sourceGroup, name, "2K27")["versions"]!["2K27"]!["values"]!;
            harness.Equal($"dropdown merge: authored {name} index {raw}", expected, source[raw]!.GetValue<string>());
            harness.Equal($"dropdown merge: {name} decodes numeric value", expected,
                FieldIo.RawToDisplayValue("Signature", field, payload, raw).ToString());
            var originalPayload = (JsonObject)Field(original, "Jump Shooting II", name)["versions"]!["2K27"]!;
            harness.Check($"dropdown merge: {name} preserves complete memory layout",
                JsonNode.DeepEquals(WithoutOptions(originalPayload), WithoutOptions(payload)));
        }
        var older = new Offsets("NBA2K26.exe");
        var oldLayout = older.EditorLayout("Players");
        var oldField = Field(oldLayout, "Jump Shooting", "RELEASETIMING", "2K26");
        var oldPayload = (JsonObject)oldField["versions"]!["2K26"]!;
        var oldSource = (JsonArray)Field(dropdowns, "Jump Shooting", "RELEASETIMING", "2K26")["versions"]!["2K26"]!["dropdown"]!;
        harness.Equal("dropdown merge: 2K26 retains existing same-group mapping", oldSource[0]!.ToString(),
            FieldIo.RawToDisplayValue("Signature", oldField, oldPayload, 0).ToString());
    }

    private static void UniqueAndAmbiguous(Harness harness)
    {
        var offsets = new Offsets("NBA2K27.exe");
        offsets.Resource("offsets_players.json")["Players"] = JsonNode.Parse("""
        {"Signature":{
            "New":[
                {"normalized_name":"MOVED","versions":{"2K27":{"address":8,"enum":"TEST","bit_length":3,"bit_offset":1}}},
                {"normalized_name":"AMBIGUOUS","versions":{"2K27":{"address":12}}},
                {"normalized_name":"SOURCEAMBIGUOUS","versions":{"2K27":{"address":16}}},
                {"normalized_name":"LOCAL","versions":{"2K27":{"address":20}}},
                {"normalized_name":"WRONGVERSION","versions":{"2K27":{"address":24}}},
                {"normalized_name":"OTHERSECTION","versions":{"2K27":{"address":28}}},
                {"normalized_name":"moved","versions":{"2K27":{"address":32}}}
            ],
            "Second":[{"normalized_name":"AMBIGUOUS","versions":{"2K27":{"address":36}}}],
            "Old":[{"normalized_name":"MOVED","versions":{"2K26":{"address":40}}}]
        }}
        """);
        offsets.Resource("dropdowns.json")["Players"] = JsonNode.Parse("""
        {"Signature":{
            "Old":[
                {"normalized_name":"MOVED","versions":{"2K26":{"values":["OLD"]},"2K27":{"values":["NEW"],"address":999,"enum":"WRONG"}}},
                {"normalized_name":"AMBIGUOUS","versions":{"2K27":{"values":["UNSAFE"]}}},
                {"normalized_name":"SOURCEAMBIGUOUS","versions":{"2K27":{"values":["FIRST"]}}},
                {"normalized_name":"LOCAL","versions":{"2K27":{"values":["CROSS"]}}},
                {"normalized_name":"WRONGVERSION","versions":{"2K26":{"values":["WRONG"]}}}
            ],
            "New":[{"normalized_name":"LOCAL","versions":{"2K27":{"values":["LOCAL"]}}}],
            "Duplicate":[{"normalized_name":"SOURCEAMBIGUOUS","versions":{"2K27":{"values":["SECOND"]}}}]
        },"OtherSection":{"Old":[{"normalized_name":"OTHERSECTION","versions":{"2K27":{"values":["WRONG"]}}}]}}
        """);
        var layout = offsets.EditorLayout("Players");
        JsonObject Payload(string group, string name, string version = "2K27") =>
            (JsonObject)Field(layout, group, name)["versions"]![version]!;
        harness.Equal("dropdown merge: unique moved field", "NEW", Payload("New", "MOVED")["values"]?[0]?.ToString() ?? "missing");
        harness.Equal("dropdown merge: leaves address intact", 8, Payload("New", "MOVED")["address"]!.GetValue<int>());
        harness.Equal("dropdown merge: leaves enum intact", "TEST", Payload("New", "MOVED")["enum"]!.GetValue<string>());
        harness.Equal("dropdown merge: same-group mapping wins", "LOCAL", Payload("New", "LOCAL")["values"]![0]!.ToString());
        harness.Equal("dropdown merge: original 2K26 group preserved", "OLD", Payload("Old", "MOVED", "2K26")["values"]![0]!.ToString());
        foreach (string group in new[] { "New", "Second" })
            harness.Check($"dropdown merge: ambiguous target {group} remains unmapped", Payload(group, "AMBIGUOUS")["values"] is null);
        foreach (string name in new[] { "SOURCEAMBIGUOUS", "WRONGVERSION", "OTHERSECTION", "moved" })
            harness.Check($"dropdown merge: rejects unsafe candidate {name}", Payload("New", name)["values"] is null);
    }
}
