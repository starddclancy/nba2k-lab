using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Cli;

internal static class StatisticsRoundTrip
{
    public static int Run(Dictionary<string, string> options)
    {
        int index = int.Parse(options["index"]);
        string path = options["out"];
        var report = new JsonObject
        {
            ["format"] = "nba2k27-statistics-write-roundtrip/1",
            ["status"] = "failed",
            ["trials"] = new JsonArray(),
        };
        var session = new PlayerSession(dataDirectory: options.GetValueOrDefault("data-dir"));
        try
        {
            session.Connect();
            var item = session.Items.Single(player => player.Index == index);
            report["pid"] = session.Memory.Pid;
            report["player"] = item.Label;
            report["index"] = index;
            var original = PlayerStatistics.Read(session, item);
            var regions = new List<(long Address, byte[] Bytes)>
            {
                (item.Address, session.Memory.ReadBytes(item.Address, 1272)),
            };
            regions.AddRange(original.Seasons.Select(season =>
                (season.Address, session.Memory.ReadBytes(season.Address, 64))));
            var keys = new[]
            {
                "SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS",
                "Stats/Career High Stats/POINTS",
                "Vitals/Awards/PLAYEROFWEEK",
                "SeasonStats/CURRENTYEARSTATID/Awards & Honors/MOSTVALUABLEPLAYER",
            };
            bool ok = true;
            foreach (string key in keys)
            {
                var trial = new JsonObject { ["field"] = key };
                ((JsonArray)report["trials"]!).Add(trial);
                int depth = session.UndoStack.Count;
                try
                {
                    var read = PlayerStatistics.Read(session, item);
                    var before = read.Snapshot.Values[key];
                    var spec = session.Specs[key];
                    object next = spec.Options.Count > 0
                        ? spec.Options.First(option => option != before.Display.ToString())
                        : Conversions.ToLong(before.Display) + 1;
                    trial["before"] = Program.ValueNode(before.Display);
                    trial["beforeRaw"] = Program.ValueNode(before.Raw);
                    trial["requested"] = Program.ValueNode(next);
                    int writesBefore = ((Win32Memory)session.Memory).Writes;
                    var transaction = session.Apply(read.Snapshot, new Dictionary<string, object?> { [key] = next })
                        ?? throw new InvalidOperationException("No changed bytes");
                    trial["patched"] = transaction.Status == "applied";
                    trial["patches"] = transaction.Patches.Count;
                    trial["writes"] = ((Win32Memory)session.Memory).Writes - writesBefore;
                    trial["journal"] = Path.GetFileName(transaction.JournalPath);
                    var after = PlayerStatistics.Read(session, item).Snapshot.Values[key];
                    trial["after"] = Program.ValueNode(after.Display);
                    trial["afterRaw"] = Program.ValueNode(after.Raw);
                    trial["readbackMatches"] = PlayerPatchGuard.SameValue(after.Raw, session.Encode(spec, next));
                    session.UndoBatch();
                    trial["undoWrites"] = ((Win32Memory)session.Memory).Writes - writesBefore
                        - trial["writes"]!.GetValue<int>();
                    var restored = PlayerStatistics.Read(session, item).Snapshot.Values[key];
                    trial["restored"] = Program.ValueNode(restored.Display);
                    trial["bytesRestored"] = regions.All(region =>
                        session.Memory.ReadBytes(region.Address, region.Bytes.Length).SequenceEqual(region.Bytes));
                    bool passed = trial["readbackMatches"]!.GetValue<bool>()
                        && trial["bytesRestored"]!.GetValue<bool>()
                        && trial["writes"]!.GetValue<int>() == trial["undoWrites"]!.GetValue<int>();
                    trial["status"] = passed ? "restored" : "needs_review";
                    ok &= passed;
                    if (!passed)
                    {
                        break;
                    }
                }
                catch (Exception exc)
                {
                    trial["error"] = exc.Message;
                    ok = false;
                    if (session.UndoStack.Count > depth)
                    {
                        try
                        {
                            session.UndoBatch();
                            trial["recoveryUndo"] = true;
                        }
                        catch (Exception undo)
                        {
                            trial["recoveryError"] = undo.Message;
                        }
                    }
                    break;
                }
            }
            report["comparedBytes"] = regions.Sum(region => region.Bytes.Length);
            report["endSnapshotZeroDiff"] = regions.All(region =>
                session.Memory.ReadBytes(region.Address, region.Bytes.Length).SequenceEqual(region.Bytes));
            report["writes"] = ((Win32Memory)session.Memory).Writes;
            report["status"] = ok && report["endSnapshotZeroDiff"]!.GetValue<bool>() ? "restored" : "needs_review";
        }
        catch (Exception exc)
        {
            report["error"] = exc.Message;
        }
        finally
        {
            session.Close();
            File.WriteAllText(path, report.ToJsonString(Program.JsonOptions));
        }
        Console.WriteLine($"{report["status"]}: {path}");
        return report["status"]?.GetValue<string>() == "restored" ? 0 : 1;
    }
}
