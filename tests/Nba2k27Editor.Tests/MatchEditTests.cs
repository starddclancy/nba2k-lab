using System.Text.Json;
using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.Tests;

internal static class MatchEditTests
{
    public static void Run(Harness harness)
    {
        var source = Sample();
        string before = JsonSerializer.Serialize(source);
        var shot = new MatchEditRequest(0, 0, 1, new() { ["twoPointMade"] = 2, ["twoPointAttempted"] = 2 });
        var plan = MatchEditPlanner.Prepare(source, new[] { shot });
        harness.Equal("shot changes player quarter score", 4L, Value(plan, 0, 0, 1, "points"));
        harness.Equal("shot changes player total score", 7L, Value(plan, 0, 0, 0, "points"));
        harness.Equal("shot changes team quarter score", 6L, Value(plan, 0, null, 1, "points"));
        harness.Equal("shot changes team total score", 12L, Value(plan, 0, null, 0, "points"));
        harness.Equal("shot has one affected player", 1, plan.Players);
        harness.Equal("shot has one affected period", 1, plan.Periods);
        harness.Check("shot marks all scores as derived", plan.Changes
            .Where(change => change.Field == "points").All(change => change.Derived));
        harness.Check("shot input differs from linked totals", !plan.Changes.Single(change =>
            change.PlayerId == 0 && change.Slot == 1 && change.Field == "twoPointMade").Derived);
        harness.Check("shot does not modify other side or period", plan.Changes
            .All(change => change.Side == 0 && change.Slot is 0 or 1));
        harness.Equal("planning does not mutate source", before, JsonSerializer.Serialize(source));

        var free = new MatchEditRequest(0, 1, 2, new() { ["freeThrowMade"] = 2, ["freeThrowAttempted"] = 2 });
        var batch = MatchEditPlanner.Prepare(source, new[] { shot, free });
        harness.Equal("two players update team score once", 14L, Value(batch, 0, null, 0, "points"));
        harness.Equal("batch counts both players", 2, batch.Players);
        harness.Equal("batch counts both periods", 2, batch.Periods);
        harness.Equal("batch deterministic across request order", JsonSerializer.Serialize(batch),
            JsonSerializer.Serialize(MatchEditPlanner.Prepare(source, new[] { free, shot })));
        harness.Equal("batch has unique affected counters", batch.Changes.Count, batch.Changes
            .Select(change => (change.Side, change.PlayerId, change.Slot, change.Field)).Distinct().Count());

        var secondPeriod = new MatchEditRequest(0, 0, 2, new() { ["threePointMade"] = 0 });
        var decrease = MatchEditPlanner.Prepare(source, new[] { secondPeriod });
        harness.Equal("decreasing made shot reduces total", 7L, Value(decrease, 0, null, 0, "points"));
        var multiplePeriods = MatchEditPlanner.Prepare(source, new[] { shot, secondPeriod });
        harness.Equal("same player multiple periods sum correctly", 4L, Value(multiplePeriods, 0, 0, 0, "points"));
        harness.Equal("same player counted once", 1, multiplePeriods.Players);
        harness.Equal("same player retains both periods", 2, multiplePeriods.Periods);

        var rebounds = MatchEditPlanner.Prepare(source, new[]
        {
            new MatchEditRequest(1, 0, 2, new() { ["offensiveRebounds"] = 1, ["defensiveRebounds"] = 2 }),
        });
        harness.Equal("rebounds derive player total", 3L, Value(rebounds, 1, 0, 0, "rebounds"));
        harness.Equal("rebounds derive team period", 3L, Value(rebounds, 1, null, 2, "rebounds"));
        harness.Check("rebounds leave score unchanged", rebounds.Changes.All(change => change.Field != "points"));
        var overtime = MatchEditPlanner.Prepare(Sample() with { Period = 7 }, new[]
        {
            new MatchEditRequest(0, 0, 5, new() { ["freeThrowMade"] = 1, ["freeThrowAttempted"] = 1 }),
        });
        harness.Equal("overtime accumulated slot updates score", 11L, Value(overtime, 0, null, 0, "points"));

        void Reject(string name, MatchEditRequest request, MatchSnapshot? sample = null) =>
            harness.Throws(name, () => MatchEditPlanner.Prepare(sample ?? Sample(), new[] { request }));
        Reject("negative counters rejected", shot with { Changes = new() { ["assists"] = -1 } });
        Reject("oversized counters rejected", shot with { Changes = new() { ["assists"] = 65536 } });
        Reject("made above attempted rejected", shot with { Changes = new() { ["threePointMade"] = 1 } });
        Reject("fractional percentages cannot be fields", shot with { Changes = new() { ["percentage"] = 50 } });
        Reject("pointer field rejected", shot with { Changes = new() { ["entity"] = 1 } });
        Reject("direct score without shot allocation rejected", shot with { Changes = new() { ["points"] = 20 } });
        Reject("direct rebound total rejected", shot with { Changes = new() { ["rebounds"] = 20 } });
        Reject("full match total is not an editable period", shot with { Slot = 0 });
        Reject("future quarter rejected", shot with { Slot = 3 });
        Reject("overtime before it starts rejected", shot with { Slot = 5 });
        Reject("invalid side rejected", shot with { Side = 2 });
        Reject("unknown player rejected", shot with { PlayerId = 100 });
        Reject("empty changes rejected", shot with { Changes = new() });
        Reject("unchanged values rejected", shot with { Changes = new() { ["twoPointMade"] = 1 } });
        harness.Throws("empty edit list rejected", () => MatchEditPlanner.Prepare(Sample(), Array.Empty<MatchEditRequest>()));
        harness.Throws("duplicate player period rejected", () => MatchEditPlanner.Prepare(Sample(), new[] { shot, shot }));
        harness.Throws("oversized edit list rejected", () =>
            MatchEditPlanner.Prepare(Sample(), Enumerable.Repeat(shot, 181).ToArray()));
        Reject("duplicate side identity rejected", shot, Sample() with
        {
            Teams = new() { source.Teams[0], source.Teams[0] },
        });
        var ambiguous = Sample();
        ambiguous.Teams[0].Players.Add(ambiguous.Teams[0].Players[0]);
        Reject("ambiguous player ID rejected", shot, ambiguous);
        Reject("known snapshot discrepancy rejected", shot, Sample() with
        {
            Issues = new() { new("team", "points", "playerSum", 1, 2) },
        });
        var wrongTotal = Sample();
        wrongTotal.Teams[0].Players[0].Stats.Total["points"]++;
        Reject("inconsistent original totals rejected", shot, wrongTotal);
        var wrongTeam = Sample();
        wrongTeam.Teams[0].Stats.Periods[0]["assists"] = 1;
        wrongTeam.Teams[0].Stats.Total["assists"] = 1;
        Reject("hidden team player discrepancy rejected", shot, wrongTeam);
        var missing = Sample();
        missing.Teams[0].Players[0].Stats.Periods[0].Remove("assists");
        Reject("incomplete original stats rejected", shot, missing);
        var wrongShot = Sample();
        wrongShot.Teams[0].Players[0].Stats.Periods[0]["twoPointAttempted"] = 0;
        Reject("inconsistent original shooting rejected", shot, wrongShot);

        var overflow = Sample();
        overflow.Teams[0].Players[0].Stats.Periods[0]["assists"] = 65535;
        overflow.Teams[0].Players[0].Stats.Total["assists"] = 65535;
        overflow.Teams[0].Stats.Periods[0]["assists"] = 65535;
        overflow.Teams[0].Stats.Total["assists"] = 65535;
        string overflowBefore = JsonSerializer.Serialize(overflow);
        Reject("player aggregate overflow rejected", shot with
        {
            Slot = 2, Changes = new() { ["assists"] = 1 },
        }, overflow);
        Reject("team aggregate overflow rejected", shot with
        {
            PlayerId = 1, Changes = new() { ["assists"] = 1 },
        }, overflow);
        harness.Equal("failed plans do not mutate source", overflowBefore, JsonSerializer.Serialize(overflow));
    }

    private static long Value(MatchEditPlan plan, int side, int? playerId, int slot, string field) =>
        plan.Changes.Single(change => change.Side == side && change.PlayerId == playerId
            && change.Slot == slot && change.Field == field).After;

    private static MatchSnapshot Sample()
    {
        var teams = new List<MatchTeam>();
        for (int side = 0; side < 2; side++)
        {
            var players = new List<MatchPlayer>();
            for (int id = 0; id < 2; id++)
            {
                var stats = MatchReader.DecodeStats(new byte[MatchReader.StatSize]);
                stats.Periods[0]["twoPointMade"] = 1;
                stats.Periods[0]["twoPointAttempted"] = 1;
                stats.Periods[0]["points"] = 2;
                stats.Periods[1]["threePointMade"] = 1;
                stats.Periods[1]["threePointAttempted"] = 1;
                stats.Periods[1]["points"] = 3;
                foreach (string field in stats.Total.Keys.ToArray())
                {
                    stats.Total[field] = stats.Periods.Sum(row => row[field]);
                }
                players.Add(new($"Player {side}-{id}", id, 0, true, 0, 0, 0, stats));
            }
            var team = MatchReader.DecodeStats(new byte[MatchReader.StatSize]);
            foreach (string field in team.Total.Keys.ToArray())
            {
                team.Total[field] = players.Sum(player => player.Stats.Total[field]);
                for (int period = 0; period < 5; period++)
                {
                    team.Periods[period][field] = players.Sum(player => player.Stats.Periods[period][field]);
                }
            }
            teams.Add(new(side, $"Team {side}", Array.Empty<string>(), 0, 0, 0, 0, team, players));
        }
        return new(2, 600, true, teams, new(), 0, 0, 0);
    }
}
