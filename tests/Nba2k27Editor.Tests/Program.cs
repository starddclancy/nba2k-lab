using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nba2k27Editor.Tests;

namespace Nba2k27Editor.Tests;

/// <summary>
/// Offline test entry point. Runs every group against simulated memory only: no game
/// process is opened, so the suite is safe to run at any time.
/// </summary>
internal static class Program
{
    private static readonly (string Name, Action<Harness> Run)[] Groups =
    {
        ("memory+conversions", MemoryAndConversionTests.Run),
        ("field codec", FieldCodecTests.Run),
        ("overlay", OverlayTests.Run),
        ("roster slots", RosterSlotTests.Run),
    ("player library", PlayerLibraryTests.Run),
        ("prospects", ProspectTests.Run),
        ("membership and record scope", MembershipTests.Run),
        ("player summaries", PlayerSummaryTests.Run),
        ("native player calendar", PlayerAgeContextTests.Run),
        ("player read cache", PlayerReadCacheTests.Run),
        ("player statistics", PlayerStatisticsTests.Run),
        ("history records", HistoryTests.Run),
        ("season game decoding", SeasonGamesTests.Run),
        ("sparse team scanning", SparseTeamTests.Run),
        ("schedule edit transactions", ScheduleEditorTests.Run),
        ("current match reads", MatchTests.Run),
        ("current match edit planning", MatchEditTests.Run),
        ("current match fixed-width write plans", MatchWritePlanTests.Run),
        ("current match durable checkpoints", MatchCheckpointTests.Run),
        ("current match write transactions", MatchWriterTests.Run),
        ("current match pause gate", MatchPauseStateTests.Run),
        ("journal", JournalTests.Run),
        ("journal reader", JournalReaderTests.Run),
        ("general GS", GeneralGsTests.Run),
        ("legacy GS", LegacyGsTests.Run),
        ("presets", PresetTests.Run),
        ("base cache", BaseRootCacheTests.Run),
    };

    private static int Main(string[] args)
    {
        string? outputPath = null;
        for (int index = 0; index < args.Length; index++)
        {
            if (args[index] == "--out" && index + 1 < args.Length)
            {
                outputPath = args[++index];
            }
        }

        var report = new JsonObject
        {
            ["format"] = "nba2k27-editor-offline-tests/1",
            ["groups"] = new JsonArray(),
        };
        var failures = new List<string>();
        int passed = 0;

        foreach (var (name, run) in Groups)
        {
            var harness = new Harness();
            string? crash = null;
            try
            {
                run(harness);
            }
            catch (Exception exc)
            {
                crash = $"{exc.GetType().Name}: {exc.Message}";
            }

            passed += harness.Passed;
            var groupFailures = harness.Failures.Select(failure => $"{name}: {failure}").ToList();
            if (crash is not null)
            {
                groupFailures.Add($"{name}: the group threw {crash}");
            }
            failures.AddRange(groupFailures);

            ((JsonArray)report["groups"]!).Add(new JsonObject
            {
                ["name"] = name,
                ["passed"] = harness.Passed,
                ["failed"] = groupFailures.Count,
                ["crash"] = crash,
                ["failures"] = new JsonArray(groupFailures.Select(item => (JsonNode?)JsonValue.Create(item)).ToArray()),
            });

            Console.WriteLine($"{name}: {harness.Passed} passed, {groupFailures.Count} failed");
            foreach (string failure in groupFailures)
            {
                Console.WriteLine($"  FAIL {failure}");
            }
        }

        report["passed"] = passed;
        report["failed"] = failures.Count;
        report["total"] = passed + failures.Count;
        report["status"] = failures.Count == 0 ? "ok" : "failed";

        Console.WriteLine();
        Console.WriteLine($"total: {passed + failures.Count} checks, {passed} passed, {failures.Count} failed");

        if (outputPath is not null)
        {
            string json = report.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outputPath, json + "\n", new UTF8Encoding(false));
            Console.WriteLine($"report: {outputPath}");
        }

        return failures.Count == 0 ? 0 : 1;
    }
}
