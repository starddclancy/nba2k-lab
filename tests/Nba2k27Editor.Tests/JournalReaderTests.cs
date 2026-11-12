using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;

namespace Nba2k27Editor.Tests;

internal static class JournalReaderTests
{
    public static void Run(Harness harness)
    {
        var player = JournalReader.Parse("""
            {"format":"nba2k27-player-edit/1","status":"applied","mode":"single","time":"2026-11-13",
             "players":[{"player":"Sample","index":2,"changes":[
                {"field":"Vitals/General/JERSEYNUMBER","before":7,"after":8},
                {"field":"Vitals/General/FLAG","before":false,"after":true}]}]}
            """);
        harness.Equal("journal reader: supported", "supported", player["state"]!.GetValue<string>());
        harness.Equal("journal reader: status", "applied", player["status"]!.GetValue<string>());
        harness.Equal("journal reader: subject", "Sample", player["changes"]![0]!["subject"]!.GetValue<string>());
        harness.Equal("journal reader: exact before", "7", player["changes"]![0]!["before"]!.GetValue<string>());
        harness.Equal("journal reader: exact after", "8", player["changes"]![0]!["after"]!.GetValue<string>());
        harness.Equal("journal reader: false preserved", "false", player["changes"]![1]!["before"]!.GetValue<string>());
        foreach (string format in new[] { "nba2k27-general-gs-edit/1", "nba2k27-history-edit/1", "nba2k27-team-edit/1" })
        {
            string member = format.Contains("-gs-") ? "changes" : "fields";
            string field = format.Contains("-gs-") ? "field" : "key";
            var parsed = JournalReader.Parse($$"""
                {"format":"{{format}}","status":"failed","error":"write refused",
                 "{{member}}":[{"{{field}}":"POINTS","before":1,"after":2}]}
                """);
            harness.Equal($"journal reader: {format}", 1, parsed["changes"]!.AsArray().Count);
            harness.Equal($"journal reader: error {format}", "write refused", parsed["error"]!.GetValue<string>());
        }
        var native = JournalReader.Parse("""
            {"format":"nba2k27-native-gs-edit/1","status":"undone","field":"user.speed","before":50,"after":60}
            """);
        harness.Equal("journal reader: native single field", "user.speed",
            native["changes"]![0]!["field"]!.GetValue<string>());
        var text = JournalReader.Parse("""
            {"format":"nba2k27-team-edit/1","team":"Sample Team","fields":[
              {"key":"TEXT","before":"Line\n\"quoted\"","after":null}]}
            """);
        harness.Equal("journal reader: text decoded once", "Line\n\"quoted\"", text["changes"]![0]!["before"]!.GetValue<string>());
        harness.Equal("journal reader: null explicit", "null", text["changes"]![0]!["after"]!.GetValue<string>());
        harness.Equal("journal reader: team identity", "Sample Team", text["changes"]![0]!["subject"]!.GetValue<string>());
        harness.Equal("journal reader: preset unsupported", "unsupported",
            JournalReader.Parse("""{"format":"nba2k27-player-preset/1"}""")["state"]!.GetValue<string>());
        foreach (string malformed in new[] { "{", "[]", "null", """{"format":42}""",
            """{"format":"nba2k27-player-edit/1","players":[{"changes":"bad"}]}""",
            """{"format":"nba2k27-general-gs-edit/1","changes":[{"field":"x","after":2}]}""" })
        {
            harness.Equal($"journal reader: rejects malformed {malformed}", "invalid",
                JournalReader.Parse(malformed)["state"]!.GetValue<string>());
        }
        string directory = Path.Combine(Path.GetTempPath(), $"nba2k27-journal-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "good.json"), """{"format":"unknown/1"}""");
            harness.Equal("journal reader: reads listed JSON", "unsupported",
                JournalReader.Read(directory, "good.json")["state"]!.GetValue<string>());
            foreach (string name in new[] { "../good.json", "..\\good.json", "sub/good.json", "good.txt", "", "good.json:other" })
            {
                harness.Throws($"journal reader: rejects path {name}", () => JournalReader.Read(directory, name));
            }
            File.WriteAllText(Path.Combine(directory, "large.json"), new string(' ', JournalReader.MaximumBytes + 1));
            harness.Equal("journal reader: size bounded", "invalid",
                JournalReader.Read(directory, "large.json")["state"]!.GetValue<string>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
