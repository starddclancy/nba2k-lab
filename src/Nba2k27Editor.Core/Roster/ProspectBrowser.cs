using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Roster;

public sealed record ProspectInfo(RecordListItem Item, int ProspectIndex, int DraftYear,
    int Round, int Pick, int BirthYear, int Active, string PlayerType)
{
    public bool IsProspect => ProspectIndex is >= 0 and < RosterBrowser.DraftCapacity
        && Round == 0 && Pick == 0 && Active == 1;
}

public sealed record ProspectCatalog(IReadOnlyList<ProspectInfo> Records,
    IReadOnlyDictionary<int, string> Errors);

/// <summary>Draft metadata on Players records, independent of the standalone DraftClass table.</summary>
public static class ProspectBrowser
{
    public static ProspectCatalog Read(Schema model, IReadOnlyList<RecordListItem> items)
    {
        string[] names = { "DRAFTPROSPECTINDEX", "YEARDRAFTEDINTERNAL", "ROUNDDRAFTED",
            "DRAFTPICKNUMBER", "BIRTHYEAR", "ISACTIVEPRIVATE", "TYPE" };
        var entries = names.ToDictionary(name => name, name => model.FieldByName("Players", name)
            ?? throw new InvalidOperationException($"Draft metadata is unavailable: {name}"));
        var records = new List<ProspectInfo>();
        var errors = new Dictionary<int, string>();
        foreach (var item in items)
        {
            try
            {
                var values = entries.ToDictionary(pair => pair.Key, pair => model.ReadField(pair.Value, item));
                int Raw(string name) => Conversions.ToInt(values[name].RawValue);
                records.Add(new ProspectInfo(item, Raw("DRAFTPROSPECTINDEX"),
                    Conversions.ToInt(values["YEARDRAFTEDINTERNAL"].DisplayValue),
                    Raw("ROUNDDRAFTED"), Raw("DRAFTPICKNUMBER"), Raw("BIRTHYEAR"),
                    Raw("ISACTIVEPRIVATE"), values["TYPE"].DisplayValue.ToString() ?? string.Empty));
            }
            catch (Exception exc)
            {
                errors[item.Index] = exc.Message;
            }
        }
        return new ProspectCatalog(records, errors);
    }
}
