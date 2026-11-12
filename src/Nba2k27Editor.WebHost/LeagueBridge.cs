using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Model;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.WebHost;

internal sealed partial class Bridge
{
    private IReadOnlyList<RecordListItem> LeagueTeams() => LeagueRoster.Teams(Session().Model, TeamItems());

    private IReadOnlyDictionary<int, PlayerMembership> LeagueMembership()
    {
        var session = Session();
        session.RefreshRosterIndex();
        var indices = LeagueTeams().Select(team => team.Index).ToHashSet();
        return PlayerMembership.Read(session.Model, session.Items,
            session.RosterIndex.Teams.Where(team => indices.Contains(team.Index)).ToArray());
    }

    private IReadOnlyList<RecordListItem> LeaguePlayers()
    {
        var memberships = LeagueMembership();
        return Session().Items.Where(item => LeagueRoster.IsCurrent(memberships[item.Index])).ToArray();
    }

    private int SeasonEndYear()
    {
        var context = PlayerAgeContext.Read(Session().Memory);
        return context.SeasonEndYear
            ?? throw new InvalidOperationException($"当前游戏赛季无法确认：{context.Error ?? "日历尚未载入"}");
    }
}
