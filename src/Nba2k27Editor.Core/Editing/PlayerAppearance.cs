using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

/// <summary>The authored scan bit is evidence about a head record, not face authenticity.</summary>
public sealed record HeadScanInfo(long? Flag, long? Address, string? Error)
{
    public string Status => Flag switch { 1 => "scanned", 0 => "not_scanned", _ => "unavailable" };
}

public static class PlayerAppearance
{
    public const string HeadScanKey = "Appearance/Head/ISSCANNEDHEAD";

    public static HeadScanInfo ReadHeadScan(Schema model, RecordListItem item)
    {
        try
        {
            var entry = model.FieldByName("Players", "ISSCANNEDHEAD")
                ?? throw new InvalidOperationException("Scanned head metadata is unavailable");
            var read = model.ReadField(entry, item);
            return new HeadScanInfo(Convert.ToInt64(read.RawValue), read.Address, null);
        }
        catch (Exception exc)
        {
            return new HeadScanInfo(null, null, exc.Message);
        }
    }

    public static HeadScanInfo HeadScanFromSnapshot(PlayerSnapshot snapshot)
    {
        return snapshot.Values.TryGetValue(HeadScanKey, out var value)
            ? new HeadScanInfo(Convert.ToInt64(value.Raw), value.Address, null)
            : new HeadScanInfo(null, null,
                snapshot.Errors.GetValueOrDefault(HeadScanKey, "Scanned head field is unavailable"));
    }
}
