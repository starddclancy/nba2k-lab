using System.Globalization;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.App;

/// <summary>
/// Live write verification for the shipped EXE: one field write, read back, undo in the same
/// connection, then compare the whole 1272-byte player record byte for byte.
/// </summary>
internal static class WriteCheck
{
    private const string FieldKey = "Attributes/Offense/3POINT";
    private const int RecordBytes = 1272;

    public static JsonObject Run(string? playerName)
    {
        var report = new JsonObject
        {
            ["format"] = "nba2k27-editor-write-check/1",
            ["field"] = FieldKey,
        };
        var session = new PlayerSession();
        try
        {
            var items = session.Connect();
            report["pid"] = session.Epoch?.Pid ?? 0;
            report["roster"] = items.Count;

            var item = playerName is null
                ? items.FirstOrDefault()
                : items.FirstOrDefault(candidate =>
                    candidate.Label.Equals(playerName, StringComparison.OrdinalIgnoreCase));
            if (item is null)
            {
                report["status"] = "failed";
                report["error"] = $"No player matched: {playerName}";
                return report;
            }
            report["player"] = item.Label;
            report["player_address"] = item.Address;

            // The whole record is the strongest restoration proof available in one connection.
            byte[] before = session.Memory.ReadBytes(item.Address, RecordBytes);
            var snapshot = session.ReadPlayer(item);
            if (!snapshot.Values.TryGetValue(FieldKey, out var value) || !value.Writable)
            {
                report["status"] = "skipped";
                report["reason"] = "3POINT is not writable for this player";
                return report;
            }
            int current = Convert.ToInt32(value.Display, CultureInfo.InvariantCulture);
            int target = current >= 99 ? current - 1 : current + 1;
            report["before"] = current;
            report["target"] = target;

            var transaction = session.Apply(snapshot, new Dictionary<string, object?>
            {
                [FieldKey] = target.ToString(CultureInfo.InvariantCulture),
            });
            if (transaction is null)
            {
                report["status"] = "skipped";
                report["reason"] = "No patch was needed";
                return report;
            }
            report["patches"] = transaction.Patches.Count;

            var afterWrite = session.ReadPlayer(item);
            int observed = Convert.ToInt32(afterWrite.Values[FieldKey].Display, CultureInfo.InvariantCulture);
            report["observed"] = observed;

            var restoredItems = session.UndoBatch();
            byte[] after = session.Memory.ReadBytes(item.Address, RecordBytes);
            bool byteExact = before.AsSpan().SequenceEqual(after);
            var restoredSnapshot = session.ReadPlayer(item);
            int restoredValue = Convert.ToInt32(
                restoredSnapshot.Values[FieldKey].Display, CultureInfo.InvariantCulture);

            report["restored_value"] = restoredValue;
            report["undo_players"] = restoredItems.Count;
            report["record_bytes"] = RecordBytes;
            report["byte_exact"] = byteExact;
            report["writes"] = session.Memory is Win32Memory memory ? memory.Writes : 0;

            bool ok = observed == target && restoredValue == current && byteExact;
            report["status"] = ok ? "ok" : "failed";
            if (!ok)
            {
                report["error"] = "write or undo verification failed";
            }
        }
        catch (Exception exc)
        {
            report["status"] = "failed";
            report["error"] = $"{exc.GetType().Name}: {exc.Message}";
        }
        finally
        {
            session.Close();
        }
        return report;
    }
}
