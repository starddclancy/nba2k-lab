using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

/// <summary>Bind a record address to its original process, roster, table and actual index.</summary>
public sealed record RecordEditScope(SessionEpoch Epoch, long Table, int Stride, RecordListItem Item)
{
    public void Check(SessionEpoch currentEpoch, long currentTable)
    {
        if (currentEpoch != Epoch)
        {
            throw new InvalidOperationException("Game or roster changed; reconnect before editing this record");
        }
        if (Table <= 0 || currentTable != Table || Stride <= 0 || Item.Index < 0
            || Item.Address != checked(Table + (long)Item.Index * Stride))
        {
            throw new InvalidOperationException("Record table or address changed; reconnect before editing");
        }
    }

    public static RecordListItem FindByIndex(IReadOnlyList<RecordListItem> items, int index) =>
        items.FirstOrDefault(item => item.Index == index)
        ?? throw new InvalidOperationException($"Record index {index} is not loaded; reconnect before editing");
}
