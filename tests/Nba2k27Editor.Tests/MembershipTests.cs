using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.Tests;

internal static class MembershipTests
{
    public static void Run(Harness harness)
    {
        PlayerMembership Classify(long? team = 0, int? active = 1, int? hidden = 0,
            int? historic = 0, bool? prospect = false, string? type = "NORMAL") =>
            PlayerMembership.Classify(Array.Empty<int>(), team, active, hidden, historic, prospect, type);
        harness.Equal("unassigned active player with complete metadata is free agent", "freeAgent", Classify().Category);
        harness.Equal("nonzero unknown team is not free agent", "unassigned", Classify(team: 0x1234).Category);
        harness.Equal("unreadable team is not free agent", "unassigned", Classify(team: null).Category);
        harness.Equal("inactive record is not free agent", "unassigned", Classify(active: 0).Category);
        harness.Equal("hidden record is not free agent", "unassigned", Classify(hidden: 1).Category);
        harness.Equal("historic record is kept separate", "historic", Classify(historic: 1).Category);
        harness.Equal("prospects are kept separate", "prospect", Classify(prospect: true).Category);
        harness.Equal("failed prospect read is not free agent", "unassigned", Classify(prospect: null).Category);
        harness.Equal("unknown type is not free agent", "unassigned", Classify(type: null).Category);
        harness.Equal("historic draft type is kept separate", "historic", Classify(type: "HISTORIC_DRAFT_CLASS").Category);
        var multiple = PlayerMembership.Classify(new[] { 0, 71 }, null, null, null, null, null, null);
        harness.Equal("team index zero is valid", "team", multiple.Category);
        harness.Equal("same player can belong to multiple team views", 2, multiple.TeamIndices.Count);

        var memory = new SimulatedMemory(0x5000);
        var schema = new Schema(memory, new Offsets("NBA2K27.exe"), "NBA2K27.exe");
        var player = new RecordListItem("Players", 17, 0x100, "Offline Test Player");
        void Set(string name, object value)
        {
            var entry = schema.FieldByName("Players", name)!;
            var payload = schema.FieldVersionPayload(entry.Field);
            FieldIo.WriteAuthoredValue(memory, FieldIo.FieldAddress(memory, player.Address, payload,
                schema.ParentPayload("Players", payload)), payload, value);
        }
        Set("CURRENTTEAMINTERNAL", 0);
        Set("ISACTIVEPRIVATE", 1);
        Set("ISHIDDEN", 0);
        Set("ISHISTORIC", 0);
        Set("TYPE", 0);
        Set("DRAFTPROSPECTINDEX", 255);
        Set("ROUNDDRAFTED", 1);
        Set("DRAFTPICKNUMBER", 17);
        int writes = memory.Writes;
        var catalog = PlayerMembership.Read(schema, new[] { player }, Array.Empty<TeamRoster>());
        harness.Equal("authored 2K27 metadata classifies free agent", "freeAgent", catalog[17].Category);
        harness.Equal("membership reading performs no writes", writes, memory.Writes);
        var clubs = new[]
        {
            new RecordListItem("Teams", 0, 0x2000, "Current Club"),
            new RecordListItem("Teams", 92, 0x3800, "Expansion Club"),
            new RecordListItem("Teams", 1, 0x2800, "Historic Club"),
        };
        var leagueField = schema.FieldByName("Teams", "CURRENTINDEX")!;
        var leaguePayload = schema.FieldVersionPayload(leagueField.Field);
        void SetLeague(RecordListItem club, int value) => FieldIo.WriteAuthoredValue(memory,
            FieldIo.FieldAddress(memory, club.Address, leaguePayload,
                schema.ParentPayload("Teams", leaguePayload)), leaguePayload, value);
        SetLeague(clubs[0], 0);
        SetLeague(clubs[1], 29);
        SetLeague(clubs[2], 255);
        var league = LeagueRoster.Teams(schema, clubs);
        harness.Equal("current team at sparse index 92 is included", "0,92", string.Join(",", league.Select(t => t.Index)));
        SetLeague(clubs[1], 0);
        harness.Throws("duplicate league identity is rejected", () => LeagueRoster.Teams(schema, clubs));
        harness.Equal("confirmed free agent remains in current roster", true, LeagueRoster.IsCurrent(catalog[17]));
        harness.Equal("historic player outside current clubs is excluded", false, LeagueRoster.IsCurrent(Classify(historic: 1)));
        harness.Equal("historic era player actually on current club remains visible", true, LeagueRoster.IsCurrent(multiple));
        harness.Equal("past season is not current", false, LeagueRoster.IsCurrentSeason(108, 2009));
        harness.Equal("current compact season matches game year", true, LeagueRoster.IsCurrentSeason(109, 2009));
        harness.Equal("unknown game season fails closed", false, LeagueRoster.IsCurrentSeason(109, null));
        var failed = PlayerMembership.Read(schema, new[] { player with { Address = 0x5000 } }, Array.Empty<TeamRoster>());
        harness.Equal("read failure stays unassigned", "unassigned", failed[17].Category);

        var epoch = new SessionEpoch(1, 2, 3, 4, 5, 6, 7);
        var item = new RecordListItem("Teams", 7, 0x1700, "Sparse Team");
        var scope = new RecordEditScope(epoch, 0x1000, 0x100, item);
        scope.Check(epoch, 0x1000);
        harness.Equal("sparse real index is found", item, RecordEditScope.FindByIndex(new[] { item }, 7));
        harness.Throws("list position is not treated as actual index", () => RecordEditScope.FindByIndex(new[] { item }, 0));
        harness.Throws("changed table rejected", () => scope.Check(epoch, 0x2000));
        harness.Throws("mismatched record address rejected", () => (scope with { Stride = 8 }).Check(epoch, 0x1000));
    }
}
