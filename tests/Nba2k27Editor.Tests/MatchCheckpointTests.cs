using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.Tests;

internal static class MatchCheckpointTests
{
    public static void Run(Harness harness)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nba2k27-match-checkpoint-" + Guid.NewGuid().ToString("N"));
        try
        {
            var checkpoint = new MatchCheckpoint(directory);
            checkpoint.Record("started", "matchRead", new JsonObject { ["writes"] = 0 });
            checkpoint.Save("snapshot.json", new JsonObject { ["pid"] = 22508, ["points"] = 9 });
            harness.Equal("checkpoint persists valid snapshot", 9,
                JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "snapshot.json")))!["points"]!.GetValue<int>());
            File.WriteAllText(Path.Combine(directory, "snapshot.json.tmp"), "{interrupted");
            var reopened = new MatchCheckpoint(directory);
            harness.Equal("interrupted temporary write preserves previous snapshot", 22508,
                JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "snapshot.json")))!["pid"]!.GetValue<int>());
            reopened.Record("failed", "matchRead", new JsonObject { ["error"] = "process exited" });
            string[] lines = File.ReadAllLines(Path.Combine(directory, "events.jsonl"));
            harness.Equal("checkpoint reopen preserves prior events", 2, lines.Length);
            harness.Equal("last persisted event identifies failure", "failed",
                JsonNode.Parse(lines[1])!["phase"]!.GetValue<string>());
            harness.Throws("checkpoint refuses parent path", () =>
                checkpoint.Save("../outside.json", new JsonObject()));
            harness.Throws("checkpoint refuses rooted path", () =>
                checkpoint.Save(Path.Combine(directory, "rooted.json"), new JsonObject()));
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }
}
