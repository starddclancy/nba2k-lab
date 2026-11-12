using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Roster;

/// <summary>Keep unassigned records separate from players with confirmed free-agent metadata.</summary>
public sealed record PlayerMembership(string Category, IReadOnlyList<int> TeamIndices)
{
    public static PlayerMembership Classify(IReadOnlyList<int> teamIndices, long? currentTeam,
        int? active, int? hidden, int? historic, bool? prospect, string? type)
    {
        if (teamIndices.Count > 0)
        {
            return new("team", teamIndices);
        }
        if (prospect == true)
        {
            return new("prospect", teamIndices);
        }
        if (historic == 1 || type is "HISTORIC" or "HISTORIC_DRAFT_CLASS")
        {
            return new("historic", teamIndices);
        }
        bool ordinary = type is "NORMAL" or "CREATED" or "CLONED";
        return new(currentTeam == 0 && active == 1 && hidden == 0 && historic == 0
            && prospect == false && ordinary ? "freeAgent" : "unassigned", teamIndices);
    }

    public static IReadOnlyDictionary<int, PlayerMembership> Read(Schema model,
        IReadOnlyList<RecordListItem> players, IReadOnlyList<TeamRoster> teams)
    {
        var memberships = new Dictionary<long, List<int>>();
        foreach (var team in teams)
        {
            foreach (var player in team.Players)
            {
                if (!memberships.TryGetValue(player.Address, out var indices))
                {
                    indices = new List<int>();
                    memberships[player.Address] = indices;
                }
                indices.Add(team.Index);
            }
        }
        var prospects = ProspectBrowser.Read(model, players);
        var prospectByIndex = prospects.Records.ToDictionary(row => row.Item.Index);
        var result = new Dictionary<int, PlayerMembership>();
        string[] names = { "CURRENTTEAMINTERNAL", "ISACTIVEPRIVATE", "ISHIDDEN", "ISHISTORIC", "TYPE" };
        var entries = names.ToDictionary(name => name, name => model.FieldByName("Players", name));
        foreach (var player in players)
        {
            var indices = memberships.GetValueOrDefault(player.Address) ?? new List<int>();
            if (indices.Count > 0)
            {
                result[player.Index] = new("team", indices);
                continue;
            }
            try
            {
                FieldRead Read(string name) => model.ReadField(entries[name]
                    ?? throw new InvalidOperationException("Membership metadata is unavailable"), player);
                bool? prospect = prospects.Errors.ContainsKey(player.Index) ? null
                    : prospectByIndex.GetValueOrDefault(player.Index)?.IsProspect;
                result[player.Index] = Classify(indices,
                    Conversions.ToLong(Read("CURRENTTEAMINTERNAL").RawValue),
                    Conversions.ToInt(Read("ISACTIVEPRIVATE").RawValue),
                    Conversions.ToInt(Read("ISHIDDEN").RawValue),
                    Conversions.ToInt(Read("ISHISTORIC").RawValue),
                    prospect, Read("TYPE").DisplayValue.ToString());
            }
            catch (Exception)
            {
                result[player.Index] = new("unassigned", indices);
            }
        }
        return result;
    }
}
