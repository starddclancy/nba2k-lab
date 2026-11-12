using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.WebHost;

internal static class LeagueScopeCheck
{
    internal static int Run(string? output)
    {
        using var bridge = new Bridge();
        var report = new JsonObject { ["format"] = "nba2k27-league-scope-check/1", ["utc"] = DateTime.UtcNow };
        int exit = 1;
        JsonNode Call(string command, JsonObject? payload = null)
        {
            var reply = JsonNode.Parse(bridge.Handle(new JsonObject
            {
                ["id"] = command, ["command"] = command, ["payload"] = payload ?? new JsonObject(),
            }.ToJsonString()))!;
            if (reply["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException(reply["error"]?.GetValue<string>());
            return reply["data"]!;
        }
        try
        {
            var connected = Call("connect");
            report["connection"] = connected.DeepClone();
            using (var process = Process.GetProcessById(connected["pid"]!.GetValue<int>()))
                report["processStarted"] = process.StartTime.ToUniversalTime().ToString("O");
            var teams = Call("listTeams").AsArray();
            var players = Call("listPlayers").AsArray();
            var allTeams = Call("listTeams", new JsonObject { ["scope"] = "all" }).AsArray();
            report["teams"] = teams.DeepClone();
            report["resourceTeamCount"] = allTeams.Count;
            report["playerCount"] = players.Count;
            report["playerCategories"] = new JsonObject(players.GroupBy(row => row!["category"]!.GetValue<string>())
                .Select(group => KeyValuePair.Create<string, JsonNode?>(group.Key, JsonValue.Create(group.Count()))));
            var teamIndices = teams.Select(team => team!["index"]!.GetValue<int>()).ToHashSet();
            var invalidPlayers = players.Where(player => player!["category"]!.GetValue<string>() is not ("team" or "freeAgent")
                || player["teamIndices"]!.AsArray().Any(index => !teamIndices.Contains(index!.GetValue<int>()))).ToArray();
            report["invalidPlayerMembershipCount"] = invalidPlayers.Length;
            var season = Call("leagueSeason");
            report["playerSeason"] = season.DeepClone();
            var teamSeason = Call("leagueTeamSeason").AsArray();
            report["teamSeason"] = teamSeason.DeepClone();
            int year = connected["seasonEndYear"]!.GetValue<int>();
            int yearColumn = season["fields"]!.AsArray().Select((name, index) => (name, index))
                .Single(pair => pair.name!.GetValue<string>() == "COMPACTYEAR").index;
            int invalidSeasonRows = season["rows"]!.AsArray().Count(row =>
                1900 + row!["values"]![yearColumn]!.GetValue<double>() != year)
                + teamSeason.Count(row => 1900 + row!["COMPACTYEAR"]!.GetValue<double>() != year);
            report["wrongSeasonCount"] = invalidSeasonRows;
            report["seasonEndYear"] = year;
            report["currentTeamCount"] = teams.Count;
            var picks = Call("draftPicks").AsArray();
            report["draftPicks"] = picks.DeepClone();
            report["draftPickOrders"] = new JsonArray(picks.GroupBy(pick => (
                    Year: pick!["year"]?.GetValue<int>(), Round: pick!["round"]!.GetValue<int>(),
                    Used: pick!["used"]!.GetValue<bool>()))
                .OrderBy(group => group.Key.Year).ThenBy(group => group.Key.Round).ThenBy(group => group.Key.Used)
                .Select(group => (JsonNode?)new JsonObject
                {
                    ["year"] = group.Key.Year,
                    ["round"] = group.Key.Round,
                    ["used"] = group.Key.Used,
                    ["count"] = group.Count(),
                    ["orders"] = new JsonArray(group.Select(pick => pick!["order"]!.GetValue<int>())
                        .Distinct().Order().Select(order => (JsonNode?)JsonValue.Create(order)).ToArray()),
                }).ToArray());
            report["status"] = teams.Count == 30 && invalidPlayers.Length == 0 && invalidSeasonRows == 0 ? "ok" : "failed";
            exit = report["status"]!.GetValue<string>() == "ok" ? 0 : 1;
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
        }
        report["writes"] = bridge.TotalWriteAttempts;
        Program.WriteReport(report, output);
        return exit;
    }
}
