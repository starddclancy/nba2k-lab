using Nba2k27Editor.Core.Gs;
using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.Tests;

internal static class MatchWritePlanTests
{
    public static void Run(Harness harness)
    {
        var source = Sample();
        var shot = new MatchEditRequest(0, 7, 1,
            new() { ["threePointMade"] = 1, ["threePointAttempted"] = 1 });
        var plan = MatchWritePlanner.Prepare(source, new[] { shot });
        harness.Equal("shot compiles all player and team counters", 12, plan.Patches.Count);
        var score = plan.Patches.Single(patch => patch.PlayerId == 7 && patch.Slot == 1
            && patch.Field == "points");
        harness.Equal("quarter score uses basic stride", 0x500C8L, score.Address);
        harness.Equal("score remains a complete uint32", 4, score.Width);
        harness.Equal("score before bytes are explicit", "00000000", score.BeforeHex);
        harness.Equal("score uses little endian bytes", "03000000", score.AfterHex);
        var made = plan.Patches.Single(patch => patch.PlayerId == 7 && patch.Slot == 1
            && patch.Field == "threePointMade");
        harness.Equal("made shot keeps separate offset", 0x500CEL, made.Address);
        harness.Equal("made shot remains a complete uint16", 2, made.Width);
        harness.Equal("made shot bytes are explicit", "0100", made.AfterHex);
        harness.Equal("total score has independent address", 0x50000L, plan.Patches.Single(patch =>
            patch.PlayerId == 7 && patch.Slot == 0 && patch.Field == "points").Address);
        harness.Equal("team quarter score has independent address", 0x600C8L, plan.Patches.Single(patch =>
            patch.PlayerId is null && patch.Slot == 1 && patch.Field == "points").Address);

        var rebounds = MatchWritePlanner.Prepare(source, new[]
        {
            new MatchEditRequest(0, 7, 1,
                new() { ["offensiveRebounds"] = 1, ["defensiveRebounds"] = 2 }),
        });
        harness.Equal("derived rebound total is never a physical patch", 8, rebounds.Patches.Count);
        harness.Check("derived rebound total has no invented offset",
            rebounds.Patches.All(patch => patch.Field != "rebounds"));
        harness.Equal("rebound uses four byte period stride", 0x504B4L, rebounds.Patches.Single(patch =>
            patch.PlayerId == 7 && patch.Slot == 1 && patch.Field == "offensiveRebounds").Address);

        var assists = MatchWritePlanner.Prepare(source, new[]
        {
            new MatchEditRequest(0, 7, 1, new() { ["assists"] = 0x1234 }),
        });
        var assist = assists.Patches.Single(patch => patch.PlayerId == 7 && patch.Slot == 1);
        harness.Equal("assists use separate extra-counter stride", 0x504F4L, assist.Address);
        harness.Equal("multibyte counter is not byte-split", "3412", assist.AfterHex);
        harness.Equal("compiler does not change captured stats", 0L,
            source.Teams[0].Players[0].Stats.Total["points"]);

        MatchSnapshot ChangePlayer(long address)
        {
            var sample = Sample();
            sample.Teams[0].Players[0] = sample.Teams[0].Players[0] with { StatsAddress = address };
            return sample;
        }
        harness.Throws("null stats address rejected", () =>
            MatchWritePlanner.Prepare(ChangePlayer(0), new[] { shot }));
        harness.Throws("unaligned stats address rejected", () =>
            MatchWritePlanner.Prepare(ChangePlayer(0x50001), new[] { shot }));
        harness.Throws("noncanonical stats address rejected", () =>
            MatchWritePlanner.Prepare(ChangePlayer(NativeGsConstants.MaxAddress - 7), new[] { shot }));
        harness.Throws("overlapping team and player windows rejected", () =>
            MatchWritePlanner.Prepare(ChangePlayer(0x60008), new[] { shot }));
        harness.Throws("duplicate stats windows across teams rejected", () =>
            MatchWritePlanner.Prepare(ChangePlayer(0x80000), new[] { shot }));
        var large = Sample() with { Period = 7 };
        var overtime = MatchWritePlanner.Prepare(large, new[]
        {
            new MatchEditRequest(0, 7, 5, new() { ["assists"] = ushort.MaxValue }),
        });
        harness.Equal("overtime uses final supported extra-counter slot", 0x50584L,
            overtime.Patches.Single(patch => patch.PlayerId == 7 && patch.Slot == 5).Address);
        harness.Equal("ushort limit is encoded without truncation", "FFFF",
            overtime.Patches.Single(patch => patch.PlayerId == 7 && patch.Slot == 5).AfterHex);
    }

    private static MatchSnapshot Sample()
    {
        var teams = new List<MatchTeam>();
        for (int side = 0; side < 2; side++)
        {
            var player = new MatchPlayer($"Player {side}", 7, 0, true,
                0x10000 + side * 0x1000, 0x20000 + side * 0x1000,
                0x50000 + side * 0x30000, MatchReader.DecodeStats(new byte[MatchReader.StatSize]));
            teams.Add(new(side, $"Team {side}", Array.Empty<string>(), 0,
                0x30000 + side * 0x1000, 0x40000 + side * 0x1000,
                0x60000 + side * 0x30000, MatchReader.DecodeStats(new byte[MatchReader.StatSize]), new() { player }));
        }
        return new(1, 600, true, teams, new(), 0, 0, 0);
    }
}
