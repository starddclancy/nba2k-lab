using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.WebHost;

/// <summary>Opt-in live write checks. Every trial is undone before the next one starts.</summary>
internal static class ProspectWriteCheck
{
    internal static int Run(string? outPath)
    {
        const string key = "Attributes/Offense/3POINT";
        var report = new JsonObject
        {
            ["format"] = "nba2k27-prospect-write-check/1",
            ["version"] = AppVersion.Value,
            ["status"] = "failed",
            ["note"] = "Live single/batch/import writes, followed by undo and record/field comparison.",
        };
        using var bridge = new Bridge();
        bool pendingUndo = false;
        string? importPath = null;
        try
        {
            var connected = Call(bridge, "connect", new());
            report["pid"] = connected["pid"]?.DeepClone();
            var prospects = Call(bridge, "prospects", new())["items"]!.AsArray();
            if (prospects.Count < 2)
            {
                throw new InvalidOperationException("At least two loaded draft prospects are required");
            }
            var target = prospects.FirstOrDefault(row => row!["name"]!.GetValue<string>() == "Kevin Durant")
                ?? prospects[0]!;
            var other = prospects.First(row => row!["index"]!.GetValue<int>() != target["index"]!.GetValue<int>())!;
            int index = target["index"]!.GetValue<int>();
            int otherIndex = other["index"]!.GetValue<int>();
            report["target"] = target.DeepClone();
            report["other"] = other.DeepClone();
            var originals = new Dictionary<int, JsonObject>();
            var all = Call(bridge, "listPlayers", new()).AsArray();
            foreach (var row in all.Where(row => row!["name"]!.GetValue<string>() == target["name"]!.GetValue<string>()
                || row["index"]!.GetValue<int>() == otherIndex))
            {
                int recordIndex = row!["index"]!.GetValue<int>();
                originals[recordIndex] = Read(recordIndex);
            }
            var observer = new PlayerSession();
            try
            {
                observer.Connect();
                var records = originals.Keys.ToDictionary(recordIndex => recordIndex,
                    recordIndex => observer.Items.Single(item => item.Index == recordIndex));
                var bytes = records.ToDictionary(pair => pair.Key,
                    pair => observer.Memory.ReadBytes(pair.Value.Address, observer.Model.DomainStride("Players")));
                JsonObject original = originals[index];
                var field = Cells(original).Single(cell => cell["key"]!.GetValue<string>() == key);
                double before = field["value"]!.GetValue<double>();
                double next = before < 99 ? before + 1 : before - 1;
                report["before"] = before;
                report["requested"] = next;
                var trials = new JsonArray();
                report["trials"] = trials;
                foreach (string operation in new[] { "single", "batch", "import" })
                {
                    var trial = new JsonObject { ["operation"] = operation };
                    trials.Add(trial);
                    JsonNode read = Call(bridge, "readPlayer", new()
                    {
                        ["index"] = index, ["source"] = "prospects",
                    });
                    string staleToken = read["readToken"]!.GetValue<string>();
                    JsonNode prepared = operation == "batch"
                        ? Call(bridge, "readBatch", new()
                        {
                            ["indices"] = new JsonArray(JsonValue.Create(index), JsonValue.Create(otherIndex)),
                            ["source"] = "prospects", ["field"] = key,
                        })
                        : Call(bridge, "readPlayer", new() { ["index"] = index, ["source"] = "prospects" });
                    string readToken = prepared["readToken"]!.GetValue<string>();
                    trial["snapshotTokenUsed"] = true;
                    JsonNode applied;
                    if (operation == "import")
                    {
                        string directory = Path.Combine(Path.GetTempPath(), "Nba2k27EditorChecks");
                        Directory.CreateDirectory(directory);
                        importPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json");
                        var spec = observer.Specs[key];
                        File.WriteAllText(importPath, new JsonObject
                        {
                            ["format"] = "nba2k27-player-export/1",
                            ["values"] = new JsonObject { [key] = Convert.ToInt64(observer.Encode(spec, next)) },
                        }.ToJsonString());
                        bridge.PickFile = _ => importPath;
                        applied = Call(bridge, "importPlayer", new()
                        {
                            ["index"] = index, ["source"] = "prospects", ["readToken"] = readToken,
                        });
                    }
                    else
                    {
                        applied = Call(bridge, operation == "batch" ? "applyBatch" : "applyPlayer", new()
                        {
                            ["index"] = index,
                            ["indices"] = new JsonArray(JsonValue.Create(index), JsonValue.Create(otherIndex)),
                            ["source"] = "prospects",
                            ["readToken"] = readToken,
                            ["changes"] = new JsonObject { [key] = next },
                        });
                    }
                    pendingUndo = (applied["patches"]?.GetValue<int>() ?? 0) > 0;
                    trial["result"] = applied.DeepClone();
                    if (!pendingUndo)
                    {
                        throw new InvalidOperationException("The trial did not produce a real patch");
                    }
                    try
                    {
                        var snapshot = observer.ReadPlayer(records[index]);
                        trial["observed"] = Convert.ToDouble(snapshot.Values[key].Display);
                        bool match = Convert.ToDouble(snapshot.Values[key].Display) == next;
                        if (operation == "single")
                        {
                            int writesBeforeConflict = bridge.PlayerWriteAttempts;
                            bool rejected = false;
                            try
                            {
                                Call(bridge, "applyPlayer", new()
                                {
                                    ["index"] = index, ["source"] = "prospects", ["readToken"] = staleToken,
                                    ["changes"] = new JsonObject { [key] = next },
                                });
                            }
                            catch (InvalidOperationException exc)
                            {
                                rejected = exc.Message.Contains("changed in the game", StringComparison.Ordinal);
                                trial["conflictError"] = exc.Message;
                            }
                            trial["conflictRejected"] = rejected;
                            trial["conflictWrites"] = bridge.PlayerWriteAttempts - writesBeforeConflict;
                            match &= rejected && bridge.PlayerWriteAttempts == writesBeforeConflict;
                        }
                        if (operation == "batch")
                        {
                            var second = observer.ReadPlayer(records[otherIndex]);
                            trial["observedOther"] = Convert.ToDouble(second.Values[key].Display);
                            match &= Convert.ToDouble(second.Values[key].Display) == next;
                        }
                        trial["applied"] = match;
                    }
                    finally
                    {
                        Call(bridge, "undoPlayer", new());
                        pendingUndo = false;
                    }
                    int mismatches = 0;
                    int byteMismatches = 0;
                    foreach (var pair in originals)
                    {
                        var current = Cells(Read(pair.Key)).ToDictionary(cell => cell["key"]!.GetValue<string>());
                        mismatches += Cells(pair.Value).Count(cell =>
                            !current.TryGetValue(cell["key"]!.GetValue<string>(), out var now)
                            || !JsonNode.DeepEquals(cell["raw"], now["raw"]));
                        byte[] actual = observer.Memory.ReadBytes(records[pair.Key].Address, bytes[pair.Key].Length);
                        byteMismatches += actual.Zip(bytes[pair.Key]).Count(pair => pair.First != pair.Second);
                    }
                    trial["restoredFieldMismatches"] = mismatches;
                    trial["restoredByteMismatches"] = byteMismatches;
                    trial["comparedRecords"] = originals.Count;
                    if (trial["applied"]!.GetValue<bool>() != true || mismatches != 0 || byteMismatches != 0)
                    {
                        throw new InvalidOperationException("Apply or restoration comparison failed; later trials were stopped");
                    }
                }
                report["observerWrites"] = observer.Memory is Nba2k27Editor.Core.Memory.Win32Memory memory
                    ? memory.Writes : 0;
            }
            finally
            {
                observer.Close();
            }
            report["status"] = "ok";
        }
        catch (Exception exc)
        {
            report["error"] = exc.Message;
        }
        finally
        {
            if (pendingUndo)
            {
                try
                {
                    Call(bridge, "undoPlayer", new());
                    report["recoveryUndo"] = true;
                }
                catch (Exception exc)
                {
                    report["recoveryUndoError"] = exc.Message;
                }
            }
            if (importPath is not null && File.Exists(importPath))
            {
                File.Delete(importPath);
            }
            report["playerWrites"] = bridge.PlayerWriteAttempts;
            Program.WriteReport(report, outPath);
        }
        return report["status"]!.GetValue<string>() == "ok" ? 0 : 1;

        JsonObject Read(int recordIndex) => Call(bridge, "readPlayer",
            new() { ["index"] = recordIndex, ["source"] = "roster" }).AsObject();
    }

    private static IEnumerable<JsonObject> Cells(JsonObject detail) =>
        detail["sections"]!.AsArray().SelectMany(section =>
            section!["fields"]!.AsArray().Select(cell => cell!.AsObject()));

    private static JsonNode Call(Bridge bridge, string command, JsonObject payload)
    {
        var response = JsonNode.Parse(bridge.Handle(new JsonObject
        {
            ["id"] = command, ["command"] = command, ["payload"] = payload,
        }.ToJsonString()))!;
        if (response["ok"]?.GetValue<bool>() != true)
        {
            throw new InvalidOperationException(response["error"]?.GetValue<string>() ?? command);
        }
        return response["data"]!;
    }
}
