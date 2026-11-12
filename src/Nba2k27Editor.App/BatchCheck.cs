using System.Globalization;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.App;

/// <summary>
/// Live batch verification: apply the same field change to two players in one transaction,
/// read both back, then undo once and compare both whole 1272-byte records.
/// </summary>
internal static class BatchCheck
{
    private const string FieldKey = "Attributes/Offense/3POINT";
    private const int RecordBytes = 1272;
    private const int ScanLimit = 60;

    public static JsonObject Run(IReadOnlyList<string> names)
    {
        var report = new JsonObject
        {
            ["format"] = "nba2k27-editor-batch-check/1",
            ["field"] = FieldKey,
        };
        var session = new PlayerSession();
        try
        {
            var items = session.Connect();
            report["pid"] = session.Epoch?.Pid ?? 0;
            report["roster"] = items.Count;

            IEnumerable<RecordListItem> pool;
            if (names.Count > 0)
            {
                pool = names
                    .Select(name => items.FirstOrDefault(item =>
                        item.Label.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    .Where(item => item is not null)
                    .Select(item => item!)
                    .ToList();
            }
            else
            {
                pool = items.Take(ScanLimit).ToList();
            }

            var chosen = new List<(RecordListItem Item, PlayerSnapshot Snapshot, int Before, int Target, byte[] Record)>();
            foreach (var item in pool)
            {
                if (chosen.Count >= 2)
                {
                    break;
                }
                if (chosen.Any(existing => existing.Item.Address == item.Address))
                {
                    continue;
                }
                PlayerSnapshot snapshot;
                try
                {
                    snapshot = session.ReadPlayer(item);
                }
                catch (Exception)
                {
                    continue;
                }
                if (!snapshot.Values.TryGetValue(FieldKey, out var value) || !value.Writable)
                {
                    continue;
                }
                int current = Convert.ToInt32(value.Display, CultureInfo.InvariantCulture);
                int target = current >= 99 ? current - 1 : current + 1;
                chosen.Add((item, snapshot, current, target, session.Memory.ReadBytes(item.Address, RecordBytes)));
            }

            if (chosen.Count < 2)
            {
                report["status"] = "skipped";
                report["reason"] = $"Only {chosen.Count} writable player(s) found for {FieldKey}";
                return report;
            }

            var edits = chosen
                .Select(entry => (
                    entry.Snapshot,
                    (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>
                    {
                        [FieldKey] = entry.Target.ToString(CultureInfo.InvariantCulture),
                    }))
                .ToList();
            var transaction = session.ApplyBatch(edits);
            if (transaction is null)
            {
                report["status"] = "skipped";
                report["reason"] = "No patch was needed";
                return report;
            }
            report["patches"] = transaction.Patches.Count;
            report["transaction_players"] = transaction.Players.Count;

            var rows = new JsonArray();
            bool observedOk = true;
            foreach (var entry in chosen)
            {
                int observed = Convert.ToInt32(
                    session.ReadPlayer(entry.Item).Values[FieldKey].Display, CultureInfo.InvariantCulture);
                observedOk &= observed == entry.Target;
                rows.Add(new JsonObject
                {
                    ["player"] = entry.Item.Label,
                    ["address"] = entry.Item.Address,
                    ["before"] = entry.Before,
                    ["target"] = entry.Target,
                    ["observed"] = observed,
                });
            }

            var restoredItems = session.UndoBatch();
            report["undo_players"] = restoredItems.Count;

            bool byteExact = true;
            bool restoredOk = true;
            foreach (var entry in chosen)
            {
                byte[] after = session.Memory.ReadBytes(entry.Item.Address, RecordBytes);
                byteExact &= entry.Record.AsSpan().SequenceEqual(after);
                int restoredValue = Convert.ToInt32(
                    session.ReadPlayer(entry.Item).Values[FieldKey].Display, CultureInfo.InvariantCulture);
                restoredOk &= restoredValue == entry.Before;
            }

            report["players"] = rows;
            report["record_bytes"] = RecordBytes;
            report["byte_exact"] = byteExact;
            report["observed_ok"] = observedOk;
            report["restored_ok"] = restoredOk;
            report["writes"] = session.Memory is Win32Memory memory ? memory.Writes : 0;

            bool ok = observedOk && restoredOk && byteExact;
            report["status"] = ok ? "ok" : "failed";
            if (!ok)
            {
                report["error"] = "batch write or undo verification failed";
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
