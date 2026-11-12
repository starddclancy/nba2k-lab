using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.WebHost;

/// <summary>Read-only acceptance against the two supplied Iverson lines and Rockets schedule.</summary>
internal static class SeasonGamesCheck
{
    internal static int Run(string? output)
    {
        using var bridge = new Bridge();
        var report = new JsonObject { ["format"] = "nba2k27-season-games-check/1", ["utc"] = DateTime.UtcNow };
        var checks = new JsonArray();
        bool ok = true;
        JsonObject Reply(string command, JsonObject? payload = null) =>
            JsonNode.Parse(bridge.Handle(new JsonObject
            {
                ["id"] = command, ["command"] = command, ["payload"] = payload ?? new JsonObject(),
            }.ToJsonString()))!.AsObject();
        JsonObject Call(string command, JsonObject? payload = null)
        {
            var reply = Reply(command, payload);
            if (reply["ok"]?.GetValue<bool>() != true)
            {
                throw new InvalidOperationException(reply["error"]?.GetValue<string>() ?? command);
            }
            return reply["data"]!.AsObject();
        }
        try
        {
            var connectionWatch = Stopwatch.StartNew();
            var connected = Call("connect");
            report["connectionElapsedMs"] = connectionWatch.Elapsed.TotalMilliseconds;
            report["pid"] = connected["pid"]?.DeepClone();
            using (var process = Process.GetProcessById(connected["pid"]!.GetValue<int>()))
                report["processStartedLocal"] = process.StartTime.ToString("yyyy-MM-dd HH:mm:ss");
            var watch = Stopwatch.StartNew();
            var data = Call("seasonGames", new JsonObject { ["diagnostics"] = true });
            report["elapsedMs"] = watch.Elapsed.TotalMilliseconds;
            report["data"] = data.DeepClone();
            var games = data["games"]!.AsArray();
            foreach (var (date, opponent, home) in new[]
            {
                ("2008-12-22", "Golden State Warriors", true), ("2008-12-26", "Sacramento Kings", true),
                ("2008-12-28", "Golden State Warriors", true), ("2008-12-30", "Detroit Pistons", true),
                ("2009-01-02", "Miami Heat", true), ("2009-01-03", "New York Knicks", false),
            })
            {
                var found = games.SingleOrDefault(g => g!["date"]!.GetValue<string>() == date
                    && g[home ? "homeLabel" : "awayLabel"]!.GetValue<string>() == "Houston Rockets"
                    && g[home ? "awayLabel" : "homeLabel"]!.GetValue<string>() == opponent);
                checks.Add(new JsonObject { ["date"] = date, ["opponent"] = opponent,
                    ["home"] = home, ["ok"] = found is not null, ["gameId"] = found?["id"]?.DeepClone() });
                ok &= found is not null;
            }
            foreach (var (date, expected) in new[]
            {
                ("2008-12-19", new[] { 25, -10, 14, 4, 12, 0, 2, 6, 8, 1, 4, 4, 0, 0, 2, 2 }),
                ("2008-12-17", new[] { 28, 22, 28, 11, 19, 1, 2, 5, 5, 1, 4, 3, 3, 0, 4, 1 }),
            })
            {
                var lines = games.Where(g => g!["date"]!.GetValue<string>() == date && g["played"]!.GetValue<bool>())
                    .SelectMany(g => g!["homeLines"]!.AsArray().Concat(g["awayLines"]!.AsArray()))
                    .Where(l => l![1]!.GetValue<string>() == "Allen Iverson").ToArray();
                var row = lines.SingleOrDefault();
                bool match = row is not null && expected.Select((value, index) => (value, index))
                    .All(pair => row[pair.index + 2]!.GetValue<double>() == pair.value);
                checks.Add(new JsonObject { ["date"] = date, ["player"] = "Allen Iverson",
                    ["ok"] = match, ["line"] = row?.DeepClone() });
                ok &= match;
            }
            report["played"] = games.Count(g => g!["played"]!.GetValue<bool>());
            var warm = Stopwatch.StartNew();
            var reread = Call("seasonGames");
            report["warmElapsedMs"] = warm.Elapsed.TotalMilliseconds;
            report["warmGames"] = reread["games"]!.AsArray().Count;
            report["tableCount"] = data["tableCount"]?.DeepClone();
            report["leagueTeamCount"] = data["leagueTeamCount"]?.DeepClone();
            report["otherGames"] = data["otherGames"]!.AsArray().Count;
            report["unmappedGameCount"] = data["unmappedGameCount"]?.DeepClone();
            report["tableSource"] = data["tableSource"]?.DeepClone();
            ok &= games.Count + data["otherGames"]!.AsArray().Count == data["tableCount"]!.GetValue<int>();
            var played = games.Where(g => g!["played"]!.GetValue<bool>()).ToArray();
            report["quarterScoreMismatches"] = played.Count(game =>
                new[] { "home", "away" }.Any(side =>
                    game![$"{side}Quarters"]!.AsArray().Sum(q => q!.GetValue<int>())
                    != game[$"{side}Points"]!.GetValue<int>()));
            report["unmappedPlayerLines"] = played.SelectMany(game =>
                game!["homeLines"]!.AsArray().Concat(game["awayLines"]!.AsArray()))
                .Count(line => line![0]!.GetValue<int>() < 0);
            ok &= report["quarterScoreMismatches"]!.GetValue<int>() == 0
                && report["unmappedPlayerLines"]!.GetValue<int>() == 0;
            report["teamGameCounts"] = new JsonArray(games.SelectMany(game =>
                new[] { game!["homeLabel"]!.GetValue<string>(), game["awayLabel"]!.GetValue<string>() })
                .GroupBy(label => label).OrderBy(group => group.Key).Select(group =>
                    (JsonNode?)new JsonObject { ["team"] = group.Key, ["games"] = group.Count() }).ToArray());
            DateOnly lastPlayed = played.Max(g => DateOnly.Parse(g!["date"]!.GetValue<string>()));
            // An active match may still be marked unplayed. Use a strictly later day for this read-only probe.
            var unplayed = games.Where(g => !g!["played"]!.GetValue<bool>()
                    && DateOnly.Parse(g["date"]!.GetValue<string>()) > lastPlayed)
                .OrderBy(g => g!["date"]!.GetValue<string>()).ThenBy(g => g!["id"]!.GetValue<int>()).First()!;
            report["previewCandidate"] = new JsonObject
            {
                ["gameId"] = unplayed["id"]!.DeepClone(),
                ["home"] = unplayed["homeLabel"]!.DeepClone(),
                ["away"] = unplayed["awayLabel"]!.DeepClone(),
                ["date"] = unplayed["date"]!.DeepClone(),
                ["lastPlayedDate"] = lastPlayed.ToString("yyyy-MM-dd"),
                ["strictlyAfterLastPlayedDate"] = true,
            };
            JsonObject? preview = null;
            for (int days = 1; days <= 60 && preview is null; days++)
            {
                var reply = Reply("schedulePrepare", new JsonObject
                {
                    ["gameId"] = unplayed["id"]!.DeepClone(),
                    ["date"] = lastPlayed.AddDays(days).ToString("yyyy-MM-dd"),
                });
                if (reply["ok"]?.GetValue<bool>() == true) preview = reply["data"]!.AsObject();
            }
            report["schedulePreview"] = preview?.DeepClone();
            ok &= preview is not null && preview["writesEnabled"]!.GetValue<bool>() == false;
            var gates = new JsonArray();
            foreach (string command in new[] { "scheduleApply", "scheduleUndo" })
            {
                var reply = Reply(command, new JsonObject { ["token"] = preview?["token"]?.DeepClone() });
                gates.Add(reply);
                ok &= reply["ok"]?.GetValue<bool>() == false
                    && (reply["error"]?.GetValue<string>() ?? "").Contains("赛程写入未获开启");
            }
            report["defaultWriteGates"] = gates;
            report["fixtureMatches"] = checks.All(check => check!["ok"]!.GetValue<bool>());
            report["nativeReadChecksOk"] =
                games.Count + data["otherGames"]!.AsArray().Count == data["tableCount"]!.GetValue<int>()
                && report["quarterScoreMismatches"]!.GetValue<int>() == 0
                && report["unmappedPlayerLines"]!.GetValue<int>() == 0
                && data["tableSource"]?.GetValue<string>() == "registered-native-manager"
                && preview is not null && !preview["writesEnabled"]!.GetValue<bool>()
                && gates.All(gate => gate!["ok"]!.GetValue<bool>() == false
                    && gate["error"]!.GetValue<string>().Contains("赛程写入未获开启"));
            report["limitations"] = "Read-only native records and preview/gate checks. No live schedule write, save/reload or game scheduler validation.";
        }
        catch (Exception exc)
        {
            report["error"] = exc.Message;
            ok = false;
        }
        report["checks"] = checks;
        report["writes"] = bridge.TotalWriteAttempts;
        report["status"] = ok && bridge.TotalWriteAttempts == 0 ? "ok" : "failed";
        Program.WriteReport(report, output);
        return report["status"]!.GetValue<string>() == "ok" ? 0 : 1;
    }
}
