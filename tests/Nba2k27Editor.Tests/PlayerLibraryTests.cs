using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;

namespace Nba2k27Editor.Tests;

/// <summary>Offline checks for the custom player and face library.</summary>
internal static class PlayerLibraryTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 5, 30, 0, TimeSpan.FromHours(8));

    private static KeyValuePair<string, JsonNode?>[] Sample() => new[]
    {
        Pair("Appearance/Head/HEADPRESET", 3),
        Pair("Appearance/Hair/HAIRTYPE", 12),
        Pair("Appearance/Presentation/JERSEYBAGGINESS", 2),
        Pair("Advanced/IDs/GRAPHICID", 8631),
        Pair("Advanced/IDs/PHOTOID", 5),
        Pair("Advanced/IDs/UNIQUEID", 20935),
        Pair("Advanced/Flags/LOADASCREATEDPLAYERIFARTISMISSING", 1),
        Pair("Attributes/Offense/3POINT", 200),
        Pair("Contract/Salary/SALARYYEAR1", 1000),
        Pair("Stats/History/GAMESPLAYED", 80),
        Pair("Vitals/Team/CURRENTTEAMINTERNAL", 1234),
    };

    private static KeyValuePair<string, JsonNode?> Pair(string key, long value) => new(key, JsonValue.Create(value));

    public static void Run(Harness harness)
    {
        var face = PlayerLibrary.Create("a-1", " Iverson face ", "face", "Allen Iverson", "", Sample(), Now);
        var faceValues = (JsonObject)face["values"]!;
        harness.Equal("library: face keeps appearance and face model fields", 4, faceValues.Count);
        harness.Check("library: face keeps GRAPHICID", faceValues.ContainsKey("Advanced/IDs/GRAPHICID"), "GRAPHICID");
        harness.Check("library: face drops clothing presentation",
            !faceValues.ContainsKey("Appearance/Presentation/JERSEYBAGGINESS"), "presentation");
        harness.Check("library: face drops the portrait photo", !faceValues.ContainsKey("Advanced/IDs/PHOTOID"), "photo");
        harness.Equal("library: name is trimmed", "Iverson face", face["name"]!.GetValue<string>());

        var player = PlayerLibrary.Create("a-2", "Iverson", "player", "Allen Iverson", "note", Sample(), Now);
        var playerValues = (JsonObject)player["values"]!;
        harness.Check("library: player keeps ratings and photo",
            playerValues.ContainsKey("Attributes/Offense/3POINT") && playerValues.ContainsKey("Advanced/IDs/PHOTOID"), "kept");
        harness.Check("library: player never copies identity, stats, contract or team",
            !playerValues.ContainsKey("Advanced/IDs/UNIQUEID") && !playerValues.ContainsKey("Stats/History/GAMESPLAYED")
            && !playerValues.ContainsKey("Contract/Salary/SALARYYEAR1")
            && !playerValues.ContainsKey("Vitals/Team/CURRENTTEAMINTERNAL"), "identity");

        var (entry, values) = PlayerLibrary.Parse(player.ToJsonString(), "fallback");
        harness.Equal("library: round trip keeps the id", "a-2", entry.Id);
        harness.Equal("library: round trip keeps the kind", "player", entry.Kind);
        harness.Equal("library: round trip keeps the values", playerValues.Count, values.Count);

        var export = new JsonObject
        {
            ["format"] = PlayerLibrary.ExportFormat, ["name"] = "Allen Iverson", ["time"] = "2026-11-08T05:00:00+08:00",
            ["values"] = new JsonObject { ["Appearance/Head/HEADPRESET"] = 3 },
        };
        var (imported, _) = PlayerLibrary.Parse(export.ToJsonString(), "new-id");
        harness.Check("library: an editor export imports as a player entry",
            imported.Kind == "player" && imported.Id == "new-id" && imported.Name == "Allen Iverson", imported.ToString());

        harness.Check("library: ids from NewId are valid", PlayerLibrary.ValidId(PlayerLibrary.NewId(Now)), "NewId");
        harness.Check("library: path-like ids are rejected",
            !PlayerLibrary.ValidId("..\\x") && !PlayerLibrary.ValidId("a/b") && !PlayerLibrary.ValidId(""), "ids");
        harness.Throws("library: path for a bad id throws", () => PlayerLibrary.PathFor("C:\\lib", "../x"), "Invalid library id");
        harness.Throws("library: unknown kind is rejected",
            () => PlayerLibrary.Create("a-3", "x", "team", "", "", Sample(), Now), "Unknown library kind: team");
        harness.Throws("library: empty name is rejected",
            () => PlayerLibrary.Create("a-3", "  ", "face", "", "", Sample(), Now), "Library name must be 1-80 characters");
        harness.Throws("library: unknown format is rejected",
            () => PlayerLibrary.Parse("{\"format\":\"x\",\"values\":{\"a\":1}}", "f"), "Unsupported library format: x");
        harness.Throws("library: unknown apply scope is rejected", () => PlayerLibrary.InScope("a", "all"), "Unknown library scope: all");

        string directory = Path.Combine(Path.GetTempPath(), "nba2k27-library-test-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            PlayerLibrary.Save(directory, face);
            PlayerLibrary.Save(directory, player);
            File.WriteAllText(Path.Combine(directory, "broken.json"), "{");
            var listed = PlayerLibrary.List(directory);
            harness.Equal("library: list skips damaged files", 2, listed.Count);
            harness.Check("library: list reports kinds", listed.Any(item => item.Kind == "face") && listed.Any(item => item.Kind == "player"), "kinds");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}