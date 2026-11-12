using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Roster;

/// <summary>Current save membership; resource order and historical birth years are not league identities.</summary>
public static class LeagueRoster
{
    public static IReadOnlyList<RecordListItem> Teams(Schema model, IEnumerable<RecordListItem> resources)
    {
        var field = model.FieldByName("Teams", "CURRENTINDEX")
            ?? throw new InvalidOperationException("球队当前联盟索引字段不可用");
        var indices = new HashSet<int>();
        var teams = new List<RecordListItem>();
        foreach (var team in resources)
        {
            int index = Conversions.ToInt(model.ReadField(field, team).RawValue);
            if (index is < 0 or >= 255) continue;
            if (!indices.Add(index))
            {
                throw new InvalidOperationException("当前联盟球队索引重复，请重新连接");
            }
            teams.Add(team);
        }
        if (teams.Count == 0) throw new InvalidOperationException("当前联盟名单尚未载入");
        return teams;
    }

    public static bool IsCurrent(PlayerMembership membership) =>
        membership.Category is "team" or "freeAgent";

    public static bool IsCurrentSeason(int compactYear, int? seasonEndYear) =>
        seasonEndYear is >= 1900 and <= 2300 && compactYear > 0 && 1900 + compactYear == seasonEndYear;
}
