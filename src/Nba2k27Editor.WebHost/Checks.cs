using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.WebHost;

/// <summary>
/// Offline verification for the packaged EXE. These checks never write to the game: the bridge
/// path only connects, lists and reads, so a failure here cannot corrupt a save.
/// </summary>
internal static class Checks
{
    /// <summary>
    /// Opt-in write verification for one bool, one string and one number field: each value is
    /// written, read back, then undone and compared field-by-field with the original snapshot.
    /// Unlike the read-only checks this one does write to the running game, like the legacy
    /// --write-check, so it is never part of the default verification.
    /// </summary>
    internal static int RunTypeCheck(string? outPath)
    {
        var report = new JsonObject
        {
            ["format"] = "nba2k27-webhost-type-check/1",
            ["status"] = "failed",
            ["note"] = "真实写入并撤销；每一步都与原始快照逐字段比对。",
        };
        int exit = 1;
        var session = new PlayerSession();
        try
        {
            session.Model.RecoverBasePointers();
            IReadOnlyList<RecordListItem> items = session.Connect();
            if (items.Count == 0)
            {
                throw new InvalidOperationException("名单为空");
            }
            RecordListItem item = items[0];
            report["player"] = item.Label;
            report["index"] = item.Index;

            PlayerSnapshot original = session.ReadPlayer(item);
            var before = new Dictionary<string, object?>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, FieldValue> pair in original.Values)
            {
                before[pair.Key] = pair.Value.Raw;
            }

            var trials = new JsonArray();
            int passed = 0;
            int attempted = 0;
            var kinds = new JsonObject { ["bool"] = 0, ["text"] = 0, ["number"] = 0, ["other"] = 0 };
            var readable = new JsonObject { ["bool"] = 0, ["text"] = 0, ["number"] = 0, ["other"] = 0 };
            foreach (KeyValuePair<string, FieldValue> pair in original.Values)
            {
                string kindOf(object? raw) => raw switch
                {
                    bool => "bool",
                    string => "text",
                    double or float or int or long => "number",
                    _ => "other",
                };
                string readKind = kindOf(pair.Value.Raw);
                readable[readKind] = (readable[readKind]?.GetValue<int>() ?? 0) + 1;
                if (!pair.Value.Writable)
                {
                    continue;
                }
                string kind = kindOf(pair.Value.Raw);
                kinds[kind] = (kinds[kind]?.GetValue<int>() ?? 0) + 1;
            }
            report["writableByKind"] = kinds;
            report["readableByKind"] = readable;
            foreach (FieldSpec spec in PickTypeSamples(session, original))
            {
                attempted++;
                var trial = new JsonObject { ["key"] = spec.Key, ["label"] = spec.Label };
                try
                {
                    PlayerSnapshot snapshot = session.ReadPlayer(item);
                    FieldValue currentValue = snapshot.Values[spec.Key];
                    object? current = currentValue.Raw;
                    object? next = ProposeValue(spec, currentValue);
                    trial["before"] = currentValue.Display.ToString() ?? Convert.ToString(current,
                        System.Globalization.CultureInfo.InvariantCulture);
                    trial["after"] = Convert.ToString(next,
                        System.Globalization.CultureInfo.InvariantCulture);
                    if (next is null)
                    {
                        trial["error"] = "该类型没有可用的替代值";
                        trials.Add(trial);
                        continue;
                    }
                    var changes = new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        [spec.Key] = next,
                    };
                    PlayerTransaction? transaction = session.Apply(snapshot, changes);
                    PlayerSnapshot verify = session.ReadPlayer(item);
                    bool applied = spec.Options.Count > 1
                        ? string.Equals(
                            verify.Values[spec.Key].Display.ToString(),
                            Convert.ToString(next, System.Globalization.CultureInfo.InvariantCulture),
                            StringComparison.Ordinal)
                        : Equals(verify.Values[spec.Key].Raw, next);
                    session.UndoBatch();
                    PlayerSnapshot restored = session.ReadPlayer(item);
                    int mismatches = 0;
                    var mismatchSample = new JsonArray();
                    foreach (KeyValuePair<string, object?> pair in before)
                    {
                        if (Equals(restored.Values[pair.Key].Raw, pair.Value))
                        {
                            continue;
                        }
                        mismatches++;
                        if (mismatchSample.Count < 12)
                        {
                            mismatchSample.Add(new JsonObject
                            {
                                ["key"] = pair.Key,
                                ["was"] = Convert.ToString(pair.Value,
                                    System.Globalization.CultureInfo.InvariantCulture),
                                ["now"] = Convert.ToString(restored.Values[pair.Key].Raw,
                                    System.Globalization.CultureInfo.InvariantCulture),
                            });
                        }
                    }
                    trial["applied"] = applied;
                    trial["patches"] = transaction?.Patches.Count ?? 0;
                    trial["restoredMismatches"] = mismatches;
                    if (mismatchSample.Count > 0)
                    {
                        trial["mismatchSample"] = mismatchSample;
                    }
                    trial["ok"] = applied && mismatches == 0;
                    if (applied && mismatches == 0)
                    {
                        passed++;
                    }
                    trials.Add(trial);
                    if (mismatches > 0)
                    {
                        // Stop here: later trials would compare against a snapshot that no longer holds.
                        break;
                    }
                }
                catch (Exception exc)
                {
                    trial["error"] = exc.Message;
                    trial["ok"] = false;
                    trials.Add(trial);
                }
            }
            report["trials"] = trials;
            report["attempted"] = attempted;
            report["passed"] = passed;
            bool ok = attempted >= 2 && passed == attempted;
            report["status"] = ok ? "ok" : "failed";
            exit = ok ? 0 : 1;

            // Team writes go through the bridge so the same journal/undo path the UI uses is hit.
            report["team"] = CheckTeamWrite();
            if (report["team"]?["ok"]?.GetValue<bool>() != true)
            {
                report["status"] = "failed";
                exit = 1;
            }
        }
        catch (Exception exc)
        {
            report["error"] = exc.Message;
        }
        finally
        {
            session.Close();
        }
        Program.WriteReport(report, outPath);
        return exit;
    }

    /// <summary>One team field written and undone through the bridge, then read back.</summary>
    private static JsonObject CheckTeamWrite()
    {
        var report = new JsonObject();
        using var bridge = new Bridge();
        try
        {
            Reply(bridge, "connect", new JsonObject());
            JsonObject detail = Reply(bridge, "readTeam", new JsonObject { ["index"] = 0 });
            string? key = null;
            double current = 0;
            string label = string.Empty;
            if (detail["data"]?["sections"] is JsonArray sections)
            {
                foreach (JsonNode? section in sections)
                {
                    if (key is not null || section?["fields"] is not JsonArray fields)
                    {
                        continue;
                    }
                    foreach (JsonNode? node in fields)
                    {
                        if (node?["writable"]?.GetValue<bool>() != true
                            || node["kind"]?.GetValue<string>() != "number"
                            || node["value"] is not JsonValue value
                            || !value.TryGetValue(out double number))
                        {
                            continue;
                        }
                        key = node["key"]!.GetValue<string>();
                        label = node["label"]!.GetValue<string>();
                        current = number;
                        break;
                    }
                }
            }
            report["key"] = key;
            report["label"] = label;
            if (key is null)
            {
                report["ok"] = false;
                report["error"] = "没有找到可写的数值型球队字段";
                return report;
            }
            report["before"] = current;
            Reply(bridge, "applyTeam", new JsonObject
            {
                ["index"] = 0,
                ["changes"] = new JsonObject { [key] = current + 1 },
            });
            JsonObject afterApply = Reply(bridge, "readTeam", new JsonObject { ["index"] = 0 });
            report["afterApply"] = FieldValueOf(afterApply, key);
            Reply(bridge, "undoTeam", new JsonObject());
            JsonObject afterUndo = Reply(bridge, "readTeam", new JsonObject { ["index"] = 0 });
            report["afterUndo"] = FieldValueOf(afterUndo, key);
            report["ok"] = report["afterApply"]?.GetValue<double>() == current + 1
                && report["afterUndo"]?.GetValue<double>() == current;
        }
        catch (Exception exc)
        {
            report["ok"] = false;
            report["error"] = exc.Message;
        }
        return report;
    }

    private static JsonNode? FieldValueOf(JsonObject detail, string key)
    {
        if (detail["data"]?["sections"] is not JsonArray sections)
        {
            return null;
        }
        foreach (JsonNode? section in sections)
        {
            if (section?["fields"] is not JsonArray fields)
            {
                continue;
            }
            foreach (JsonNode? node in fields)
            {
                if (node?["key"]?.GetValue<string>() == key)
                {
                    return node["value"]?.DeepClone();
                }
            }
        }
        return null;
    }

    /// <summary>One writable sample per value kind the UI can edit, including combo boxes.</summary>
    private static List<FieldSpec> PickTypeSamples(PlayerSession session, PlayerSnapshot snapshot)
    {
        FieldSpec? choice = null;
        FieldSpec? number = null;
        FieldSpec? text = null;
        FieldSpec? boolean = null;
        foreach (FieldSpec spec in session.Specs.Values)
        {
            if (!snapshot.Values.TryGetValue(spec.Key, out FieldValue? value) || !value.Writable)
            {
                continue;
            }
            bool numeric = value.Raw is double or float or int or long;
            if (numeric && spec.Options.Count > 1)
            {
                choice ??= spec;
            }
            else if (numeric)
            {
                number ??= spec;
            }
            else if (value.Raw is string opening && opening.Length > 0)
            {
                text ??= spec;
            }
            else if (value.Raw is bool)
            {
                boolean ??= spec;
            }
        }
        var picked = new List<FieldSpec>();
        foreach (FieldSpec? spec in new[] { boolean, choice, number, text })
        {
            if (spec is not null)
            {
                picked.Add(spec);
            }
        }
        return picked;
    }

    private static object? ProposeValue(FieldSpec spec, FieldValue value)
    {
        // A combo is written as its option label, not an index, and the label must already exist.
        if (spec.Options.Count > 1)
        {
            string display = value.Display.ToString() ?? string.Empty;
            foreach (string option in spec.Options)
            {
                if (!string.Equals(option, display, StringComparison.Ordinal))
                {
                    return option;
                }
            }
            return null;
        }
        return value.Raw switch
        {
            bool flag => !flag,
            string text when text.Length > 0 => (text[0] == 'A' ? 'B' : 'A') + text[1..],
            double number => number + 1d,
            float number => number + 1f,
            int number => number + 1,
            long number => number + 1L,
            _ => null,
        };
    }


    private static readonly byte[] OnePixelPng =
    {
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x90, 0x77, 0x53,
        0xDE, 0x00, 0x00, 0x00, 0x0C, 0x49, 0x44, 0x41, 0x54, 0x08, 0xD7, 0x63, 0xF8, 0xCF, 0xC0, 0x00,
        0x00, 0x03, 0x01, 0x01, 0x00, 0x18, 0xDD, 0x8D, 0xB0, 0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E,
        0x44, 0xAE, 0x42, 0x60, 0x82,
    };
    internal static int Run(bool assetsCheck, bool bridgeCheck, string? outPath)
    {
        var report = new JsonObject
        {
            ["format"] = "nba2k27-webhost-check/1",
            ["version"] = typeof(Checks).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            ["writes"] = 0,
        };
        bool ok = true;
        if (assetsCheck)
        {
            string[] paths = EmbeddedAssets.Paths.ToArray();
            Array.Sort(paths, StringComparer.Ordinal);
            bool hasIndex = EmbeddedAssets.TryGet("/index.html", out _);
            bool hasScript = paths.Any(path => path.StartsWith("/assets/", StringComparison.Ordinal)
                && path.EndsWith(".js", StringComparison.Ordinal));
            bool hasStyle = paths.Any(path => path.StartsWith("/assets/", StringComparison.Ordinal)
                && path.EndsWith(".css", StringComparison.Ordinal));
            report["assets"] = new JsonObject
            {
                ["count"] = paths.Length,
                ["paths"] = new JsonArray(paths.Select(path => (JsonNode?)JsonValue.Create(path)).ToArray()),
                ["index_html"] = hasIndex,
                ["script"] = hasScript,
                ["style"] = hasStyle,
            };
            ok &= hasIndex && hasScript;
        }
        if (bridgeCheck)
        {
            using var bridge = new Bridge();
            try
            {
                JsonObject connect = Reply(bridge, "connect", new JsonObject());
                JsonObject list = Reply(bridge, "listPlayers", new JsonObject { ["query"] = string.Empty });
                var rows = list["data"] as JsonArray ?? new JsonArray();
                int firstIndex = rows.Count > 0 ? rows[0]!["index"]!.GetValue<int>() : -1;
                var detail = new JsonObject();
                if (firstIndex >= 0)
                {
                    detail = Reply(bridge, "readPlayer", new JsonObject { ["index"] = firstIndex });
                }
                int sections = (detail["data"]?["sections"] as JsonArray)?.Count ?? 0;
                int fields = 0;
                if (detail["data"]?["sections"] is JsonArray list2)
                {
                    foreach (JsonNode? section in list2)
                    {
                        fields += (section?["fields"] as JsonArray)?.Count ?? 0;
                    }
                }
                report["bridge"] = new JsonObject
                {
                    ["pid"] = connect["data"]?["pid"]?.DeepClone(),
                    ["roster"] = connect["data"]?["roster"]?.DeepClone(),
                    ["teams"] = connect["data"]?["teams"]?.DeepClone(),
                    ["draft"] = connect["data"]?["draft"]?.DeepClone(),
                    ["listed"] = rows.Count,
                    ["sample"] = rows.Count > 0 ? rows[0]!["name"]?.DeepClone() : null,
                    ["sections"] = sections,
                    ["fields"] = fields,
                    ["headScan"] = detail["data"]?["headScan"]?.DeepClone(),
                    ["unavailableCount"] = detail["data"]?["unavailableCount"]?.DeepClone(),
                    ["unavailableBySection"] = detail["data"]?["unavailableBySection"]?.DeepClone(),
                    ["errorCount"] = detail["data"]?["errorCount"]?.DeepClone(),
                    ["baseStatus"] = connect["data"]?["baseStatus"]?.DeepClone(),
                    ["rootCacheAvailable"] = !string.IsNullOrEmpty(connect["data"]?["rootCache"]?.GetValue<string>()),
                    ["cacheWarning"] = connect["data"]?["cacheWarning"]?.DeepClone(),
                    ["memoryPid"] = connect["data"]?["memoryPid"]?.DeepClone(),
                    ["memoryBase"] = connect["data"]?["memoryBase"]?.DeepClone(),
                };
                ok &= rows.Count > 0 && sections > 0;
                // Season games (read-only): schedule and box scores from the game's own game table.
                var seasonWatch = System.Diagnostics.Stopwatch.StartNew();
                JsonObject season = Reply(bridge, "seasonGames", new JsonObject());
                var seasonRows = season["data"]?["games"] as JsonArray;
                ((JsonObject)report["bridge"]!)["seasonGames"] = seasonRows is null
                    ? new JsonObject { ["error"] = season["error"]?.DeepClone() }
                    : new JsonObject
                    {
                        ["games"] = seasonRows.Count, ["played"] = seasonRows.Count(g => g?["played"]?.GetValue<bool>() == true),
                        ["table"] = season["data"]?["table"]?.DeepClone(), ["ms"] = seasonWatch.ElapsedMilliseconds,
                        ["linesWithoutPlayer"] = seasonRows.Where(g => g?["played"]?.GetValue<bool>() == true)
                            .SelectMany(g => g!["homeLines"]!.AsArray().Concat(g["awayLines"]!.AsArray()))
                            .Count(line => line![0]!.GetValue<int>() < 0),
                    };
                int headTags = rows.Count(row => row?["headScan"]?["status"] is not null);
                ((JsonObject)report["bridge"]!)["headScanRows"] = headTags;
                ok &= headTags == rows.Count && detail["data"]?["headScan"]?["status"] is not null;
                var membershipCounts = new JsonObject();
                var membershipSamples = new JsonObject();
                foreach (string category in new[] { "team", "freeAgent", "prospect", "historic", "unassigned" })
                {
                    var members = rows.Where(row => row?["category"]?.GetValue<string>() == category).ToArray();
                    membershipCounts[category] = members.Length;
                    membershipSamples[category] = new JsonArray(members.Take(5)
                        .Select(row => row?["name"]?.DeepClone()).ToArray());
                }
                ((JsonObject)report["bridge"]!)["membershipCounts"] = membershipCounts;
                ((JsonObject)report["bridge"]!)["membershipSamples"] = membershipSamples;
                ((JsonObject)report["bridge"]!)["summaryFacts"] = new JsonObject
                {
                    ["ageRows"] = rows.Count(row => row?["summary"]?["age"] is JsonValue),
                    ["overallRows"] = rows.Count(row => row?["summary"]?["overall"] is JsonValue),
                    ["sample"] = detail["data"]?["summary"]?.DeepClone(),
                    ["iverson"] = rows.FirstOrDefault(row => row?["index"]?.GetValue<int>() == 1)?["summary"]?.DeepClone(),
                };
                ok &= rows.All(row => row?["teamIndices"] is JsonArray
                    && row["category"]?.GetValue<string>() is
                        "team" or "freeAgent" or "prospect" or "historic" or "unassigned");

                // The draft class is only populated inside the draft flow; report its real state.
                JsonObject rookies = Reply(bridge, "rookies", new JsonObject());
                var rookieReport = (JsonObject)report["bridge"]!;
                rookieReport["rookies"] = (rookies["data"]?["items"] as JsonArray)?.Count ?? 0;
                rookieReport["rookieStatus"] = rookies["data"]?["status"]?.DeepClone();

                JsonObject prospects = Reply(bridge, "prospects", new JsonObject());
                var prospectRows = prospects["data"]?["items"] as JsonArray ?? new JsonArray();
                rookieReport["prospects"] = prospectRows.Count;
                rookieReport["prospectStatus"] = prospects["data"]?["status"]?.DeepClone();
                rookieReport["prospectSample"] = prospectRows.FirstOrDefault()?.DeepClone();
                var durant = prospectRows.FirstOrDefault(row => row?["name"]?.GetValue<string>() == "Kevin Durant");
                rookieReport["prospectDurant"] = durant?.DeepClone();
                bool prospectRead = true;
                if (prospectRows.Count > 0)
                {
                    int prospectIndex = prospectRows[0]!["index"]!.GetValue<int>();
                    JsonObject oneProspect = Reply(bridge, "readPlayer",
                        new JsonObject { ["index"] = prospectIndex, ["source"] = "prospects" });
                    prospectRead = oneProspect["data"]?["name"]?.GetValue<string>()
                        == prospectRows[0]!["name"]?.GetValue<string>();
                    rookieReport["prospectRead"] = prospectRead;
                }
                ok &= prospectRead;
                JsonObject snapshotChecks = PlayerReadCheck.Run(bridge, firstIndex, rows, prospectRows);
                rookieReport["snapshotChecks"] = snapshotChecks;
                ok &= snapshotChecks["ok"]!.GetValue<bool>();
                // Unknown sources must never silently fall through to a Players index.
                var rejected = new JsonObject();
                foreach (string command in new[] { "readPlayer", "readBatch", "applyPlayer", "applyBatch",
                    "exportPlayer", "importPlayer" })
                {
                    var payload = new JsonObject
                    {
                        ["index"] = firstIndex, ["source"] = "unknown-source",
                        ["indices"] = new JsonArray(JsonValue.Create(firstIndex)),
                        ["changes"] = new JsonObject { ["Vitals/Vitals/NUMBER"] = 1 },
                        ["field"] = "Attributes/Offense/3POINT",
                    };
                    var request = new JsonObject { ["id"] = command, ["command"] = command, ["payload"] = payload };
                    JsonObject response = JsonNode.Parse(bridge.Handle(request.ToJsonString(Program.JsonOptions)))!.AsObject();
                    bool didReject = response["ok"]?.GetValue<bool>() == false
                        && (response["error"]?.GetValue<string>() ?? "").Contains("未知球员来源");
                    rejected[command] = didReject;
                    ok &= didReject;
                }
                rookieReport["unknownSourceRejected"] = rejected;

                // Both GS tabs must expose the same 70 fields and never write during a read.
                var bridgeReport = (JsonObject)report["bridge"]!;
                JsonObject gs = Reply(bridge, "gsRead", new JsonObject());
                JsonObject inMatch = Reply(bridge, "inMatchRead", new JsonObject());
                int gsFields = (gs["data"]?["fields"] as JsonArray)?.Count ?? 0;
                int inMatchFields = (inMatch["data"]?["fields"] as JsonArray)?.Count ?? 0;
                int gsWrites = gs["data"]?["writeAttempts"]?.GetValue<int>() ?? -1;
                int inMatchWrites = inMatch["data"]?["writeAttempts"]?.GetValue<int>() ?? -1;
                bridgeReport["gsFields"] = gsFields;
                bridgeReport["inMatchFields"] = inMatchFields;
                bridgeReport["gsWrites"] = gsWrites;
                bridgeReport["inMatchWrites"] = inMatchWrites;
                bridgeReport["gsSample"] = (gs["data"]?["fields"] as JsonArray)?[0]?.DeepClone();
                bridgeReport["inMatchSample"] = (inMatch["data"]?["fields"] as JsonArray)?[0]?.DeepClone();
                ok &= gsFields == 70 && inMatchFields == 70 && gsWrites == 0 && inMatchWrites == 0;

                // Roster reachability is read-only here: listing teams and a placement never writes.
                JsonObject teamList = Reply(bridge, "teams", new JsonObject());
                int teamCount = (teamList["data"] as JsonArray)?.Count ?? 0;
                bridgeReport["teamCount"] = teamCount;
                bridgeReport["teamSample"] = (teamList["data"] as JsonArray)?[0]?.DeepClone();
                if (firstIndex >= 0)
                {
                    // Informational: the sampled player may be a free agent, which is not a failure.
                    try
                    {
                        JsonObject place = Reply(bridge, "placement", new JsonObject { ["index"] = firstIndex });
                        bridgeReport["placement"] = place["data"]?.DeepClone();
                    }
                    catch (Exception placementError)
                    {
                        bridgeReport["placementError"] = placementError.Message;
                    }
                }
                ok &= teamCount > 0;

                // Preset round-trip touches only the presets folder; it is removed again here.
                JsonObject saved = Reply(bridge, "presetSave", new JsonObject
                {
                    ["name"] = "__check__",
                    ["values"] = new JsonObject { ["user.inside_shot"] = 60, ["cpu.three_point"] = 45 },
                });
                JsonObject listed = Reply(bridge, "presetList", new JsonObject());
                JsonObject loaded = Reply(bridge, "presetLoad", new JsonObject { ["name"] = "__check__" });
                JsonObject removed = Reply(bridge, "presetDelete", new JsonObject { ["name"] = "__check__" });
                int loadedCount = loaded["data"]?["count"]?.GetValue<int>() ?? 0;
                bool presetDeleted = removed["data"]?["deleted"]?.GetValue<bool>() == true;
                bridgeReport["preset"] = new JsonObject
                {
                    ["savedCount"] = saved["data"]?["count"]?.DeepClone(),
                    ["files"] = (listed["data"]?["files"] as JsonArray)?.Count ?? 0,
                    ["loadedCount"] = loadedCount,
                    ["deleted"] = presetDeleted,
                };
                ok &= loadedCount == 2 && presetDeleted;

                // Player export writes only our own backup file, which this check removes again.
                if (firstIndex >= 0)
                {
                    JsonObject exported = Reply(bridge, "exportPlayer",
                        new JsonObject { ["index"] = firstIndex });
                    int exportedCount = exported["data"]?["count"]?.GetValue<int>() ?? 0;
                    string exportedPath = exported["data"]?["path"]?.GetValue<string>() ?? string.Empty;
                    JsonObject journals = Reply(bridge, "journalList", new JsonObject());
                    bridgeReport["exportedFields"] = exportedCount;
                    bridgeReport["journalCount"] = (journals["data"] as JsonArray)?.Count ?? 0;
                    ok &= exportedCount > 0;
                    // The backup is only parsed, never imported. Re-importing it used to be the
                    // "unchanged backup applies 0" check, but in a running match the game updates
                    // fatigue fields between export and import, so the import wrote stale values
                    // (2026-11-08, 36 writes on Eric Snow). A read-only check must not race the game.
                    if (exportedPath.Length > 0 && File.Exists(exportedPath))
                    {
                        try
                        {
                            var backup = JsonNode.Parse(File.ReadAllText(exportedPath)) as JsonObject;
                            int backupFields = (backup?["values"] as JsonObject)?.Count ?? 0;
                            bridgeReport["backupFields"] = backupFields;
                            ok &= backupFields == exportedCount;
                        }
                        finally
                        {
                            File.Delete(exportedPath);
                        }
                    }
                }

                bridgeReport["playerWrites"] = bridge.PlayerWriteAttempts;
                ok &= bridge.PlayerWriteAttempts == 0;

                // Avatars are local files only: an unused key reports "not found", a placed file is
                // read back as a data URL, and clearing removes it again.
                JsonObject unused = Reply(bridge, "avatarGet", new JsonObject { ["key"] = "__check__" });
                bool unusedFound = unused["data"]?["found"]?.GetValue<bool>() ?? true;
                string avatarFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "NBA2K27PlayerEditor", "avatars");
                Directory.CreateDirectory(avatarFolder);
                string avatarFile = Path.Combine(avatarFolder, "__check_avatar__.png");
                File.WriteAllBytes(avatarFile, OnePixelPng);
                JsonObject readBack = Reply(bridge, "avatarGet",
                    new JsonObject { ["key"] = "__check_avatar__" });
                bool readFound = readBack["data"]?["found"]?.GetValue<bool>() ?? false;
                bool hasDataUrl = (readBack["data"]?["dataUrl"]?.GetValue<string>() ?? string.Empty)
                    .StartsWith("data:image/png;base64,", StringComparison.Ordinal);
                JsonObject cleared = Reply(bridge, "avatarClear",
                    new JsonObject { ["key"] = "__check_avatar__" });
                bool clearedDeleted = cleared["data"]?["deleted"]?.GetValue<bool>() ?? false;
                JsonObject afterClear = Reply(bridge, "avatarGet",
                    new JsonObject { ["key"] = "__check_avatar__" });
                bool afterClearFound = afterClear["data"]?["found"]?.GetValue<bool>() ?? true;
                bridgeReport["avatar"] = new JsonObject
                {
                    ["unusedFound"] = unusedFound,
                    ["readFound"] = readFound,
                    ["hasDataUrl"] = hasDataUrl,
                    ["clearedDeleted"] = clearedDeleted,
                    ["afterClearFound"] = afterClearFound,
                };
                ok &= !unusedFound && readFound && hasDataUrl && clearedDeleted && !afterClearFound;

                // Team browsing uses the same schema reader as players and stays read-only.
                JsonObject teamRows = Reply(bridge, "listTeams", new JsonObject());
                int teamRowCount = (teamRows["data"] as JsonArray)?.Count ?? 0;
                int teamSections = 0;
                int teamFieldCount = 0;
                if (teamRowCount > 0)
                {
                    JsonObject oneTeam = Reply(bridge, "readTeam", new JsonObject { ["index"] = 0 });
                    if (oneTeam["data"]?["sections"] is JsonArray teamSectionArray)
                    {
                        teamSections = teamSectionArray.Count;
                        foreach (JsonNode? section in teamSectionArray)
                        {
                            teamFieldCount += (section?["fields"] as JsonArray)?.Count ?? 0;
                        }
                    }
                }
                bridgeReport["team"] = new JsonObject
                {
                    ["rows"] = teamRowCount,
                    ["sections"] = teamSections,
                    ["fields"] = teamFieldCount,
                };
                ok &= teamRowCount > 0 && teamSections > 0 && teamFieldCount > 0;
            }
            catch (Exception exc)
            {
                report["bridge"] = new JsonObject { ["error"] = exc.Message };
                ok = false;
            }
            finally
            {
                report["writes"] = bridge.TotalWriteAttempts;
                ok &= bridge.TotalWriteAttempts == 0;
            }
        }
        report["status"] = ok ? "ok" : "failed";
        Program.WriteReport(report, outPath);
        return ok ? 0 : 1;
    }

    /// <summary>
    /// Launches the real window, waits for the WebView2 navigation to finish and asks the page
    /// what it rendered. This is the only check that proves the frontend actually runs inside the
    /// shell rather than merely being embedded.
    /// </summary>
    internal static int RunUi(string? outPath)
    {
        var report = new JsonObject
        {
            ["format"] = "nba2k27-webhost-ui-check/1",
            ["writes"] = 0,
            ["status"] = "failed",
        };
        int exit = 1;
        EditorForm? form = null;
        void Close()
        {
            try
            {
                form?.BeginInvoke(() => form.Close());
            }
            catch (InvalidOperationException)
            {
            }
        }
        form = new EditorForm(async core =>
        {
            try
            {
                // The page mounts and then awaits the bridge, so the probe polls until the roster
                // arrives instead of sampling the empty first frame.
                JsonNode? dom = null;
                for (int attempt = 0; attempt < 40; attempt++)
                {
                    string probe = await core.ExecuteScriptAsync(
                        "({"
                        + "title: document.title,"
                        + "root: !!document.getElementById('root'),"
                        + "nodes: document.getElementById('root')?.childElementCount ?? 0,"
                        + "tables: document.querySelectorAll('table').length,"
                        + "rows: document.querySelectorAll('.roster-player-pane .ant-table-row').length,"
                        + "avatars: document.querySelectorAll('.roster-player-pane .ant-table-row .avatar').length,"
                        + "teamEntries: document.querySelectorAll('.team-navigation .ant-menu-item').length,"
                        + "freeAgents: Array.from(document.querySelectorAll('.team-navigation .ant-menu-item'))"
                        + ".some(e => e.textContent.includes('自由球员')),"
                        + "sources: document.querySelectorAll('.player-source-tabs .ant-segmented-item').length,"
                        + "selection: document.querySelectorAll('.ant-table-selection-column').length,"
                        + "header: document.querySelector('.app-header h1')?.textContent ?? '',"
                        + "tag: document.querySelector('.app-header .ant-tag')?.textContent ?? '',"
                        + "text: (document.body.innerText || '').slice(0, 160)"
                        + "})");
                    dom = JsonNode.Parse(probe);
                    int rows = dom?["rows"]?.GetValue<int>() ?? 0;
                    if (rows > 0)
                    {
                        report["settled_after_ms"] = attempt * 500;
                        break;
                    }
                    await Task.Delay(500);
                }
                report["dom"] = dom;
                report["navigated"] = core.Source;
                int finalRows = dom?["rows"]?.GetValue<int>() ?? 0;
                bool rendered = dom?["root"]?.GetValue<bool>() == true
                    && (dom?["nodes"]?.GetValue<int>() ?? 0) > 0;
                report["rendered"] = rendered;
                report["roster_rows"] = finalRows;
                report["avatars"] = dom?["avatars"]?.GetValue<int>() ?? 0;
                report["sourceTabs"] = dom?["sources"]?.GetValue<int>() ?? 0;
                report["selectionColumns"] = dom?["selection"]?.GetValue<int>() ?? 0;
                report["teamEntries"] = dom?["teamEntries"]?.GetValue<int>() ?? 0;
                report["freeAgentEntry"] = dom?["freeAgents"]?.GetValue<bool>() == true;
                bool teamLogosOk = finalRows > 0 && await TeamLogoUiCheck.Run(core, report, outPath);

                // Select a player from the current team before opening the detail pane.
                if (finalRows > 0)
                {
                    await core.ExecuteScriptAsync(
                        "document.querySelector('.roster-player-pane .ant-table-row')?.click(); true");
                    JsonNode? detail = null;
                    for (int attempt = 0; attempt < 40; attempt++)
                    {
                        await Task.Delay(500);
                        string probe = await core.ExecuteScriptAsync(
                            "({"
                            + "tabs: document.querySelectorAll('.ant-tabs-tab').length,"
                            + "tabsText: Array.from(document.querySelectorAll('.ant-tabs-tab')).map(t => t.textContent).join(' / '),"
                            + "fields: document.querySelectorAll('.detail-host .field-row, .detail-host .ant-table-row').length,"
                            + "name: document.querySelector('.field-toolbar .ant-typography')?.textContent ?? '',"
                            + "headScan: document.querySelector('.field-toolbar .head-scan-tag')?.textContent ?? '',"
                            + "teamLogoLoaded:(() => {const i=document.querySelector('.field-toolbar .team-identity img');"
                            + "return !!i?.complete && i.naturalWidth > 0;})(),"
                            + "diagnosticsHidden: !document.querySelector('.detail-host .diagnostic-data'),"
                            + "readableLabels: Array.from(document.querySelectorAll('.detail-host .field-label'))"
                            + ".every(e => /[\\u3400-\\u9fff]/.test(e.textContent)),"
                            + "unavailableText: document.querySelector('.field-toolbar')?.textContent ?? '',"
                            + "text: (document.body.innerText || '').slice(0, 200)"
                            + "})");
                        detail = JsonNode.Parse(probe);
                        if ((detail?["tabs"]?.GetValue<int>() ?? 0) >= 6)
                        {
                            break;
                        }
                    }
                    report["detail"] = detail;
                    report["detail_tabs"] = detail?["tabs"]?.GetValue<int>() ?? 0;
                    if (outPath is not null)
                    {
                        await core.ExecuteScriptAsync(
                            "Array.from(document.querySelectorAll('.detail-host .ant-tabs-tab'))"
                            + ".find(e => e.textContent.startsWith('能力值'))?.click(); true");
                        await Task.Delay(200);
                        string screenshot = Path.ChangeExtension(outPath, ".players.png");
                        using (var image = File.Create(screenshot))
                        {
                            await core.CapturePreviewAsync(
                                Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                        }
                        report["playersScreenshot"] = Path.GetFileName(screenshot);
                        int originalWidth = form!.Width;
                        int originalHeight = form.Height;
                        try
                        {
                            form.Width = 1100;
                            form.Height = 700;
                            await Task.Delay(300);
                            string compact = await core.ExecuteScriptAsync(
                                "({viewport: innerWidth, width: document.body.scrollWidth,"
                                + "toolbarWidth: document.querySelector('.field-toolbar')?.clientWidth ?? 0,"
                                + "toolbarScroll: document.querySelector('.field-toolbar')?.scrollWidth ?? 0,"
                                + "sourceLabelsFit:Array.from(document.querySelectorAll('.player-source-tabs .ant-segmented-item-label'))"
                                + ".every(e => e.scrollWidth <= e.clientWidth),"
                                + "teamLabelsFit:Array.from(document.querySelectorAll('.team-navigation .team-nav-label'))"
                                + ".every(e => e.scrollWidth <= e.clientWidth)})");
                            report["compactDetail"] = JsonNode.Parse(compact);
                            string compactScreenshot = Path.ChangeExtension(outPath, ".players-1100.png");
                            using var image = File.Create(compactScreenshot);
                            await core.CapturePreviewAsync(
                                Microsoft.Web.WebView2.Core.CoreWebView2CapturePreviewImageFormat.Png, image);
                            report["compactPlayersScreenshot"] = Path.GetFileName(compactScreenshot);
                        }
                        finally
                        {
                            form.Width = originalWidth;
                            form.Height = originalHeight;
                        }
                    }
                }
                bool detailOk = (report["detail_tabs"]?.GetValue<int>() ?? 0) >= 6;
                detailOk &= report["detail"]?["diagnosticsHidden"]?.GetValue<bool>() == true
                    && report["detail"]?["readableLabels"]?.GetValue<bool>() == true;
                if (report["compactDetail"] is JsonObject compactDetail)
                {
                    detailOk &= compactDetail["toolbarScroll"]!.GetValue<int>()
                        <= compactDetail["toolbarWidth"]!.GetValue<int>()
                        && compactDetail["sourceLabelsFit"]!.GetValue<bool>();
                    detailOk &= compactDetail["teamLabelsFit"]!.GetValue<bool>();
                }
                if (detailOk)
                {
                    detailOk &= await AppearanceUiCheck.Run(core, report, outPath);
                    detailOk &= await SummaryUiCheck.Run(core, report, outPath);
                }

                // The GS tab must render both arrays with all 70 sliders and never write on read.
                if (rendered)
                {
                    await core.ExecuteScriptAsync(
                        "Array.from(document.querySelectorAll('.team-navigation .ant-menu-item'))"
                        + ".find(e => e.textContent.includes('自由球员'))?.click(); true");
                    JsonNode? freeAgentDom = null;
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        await Task.Delay(200);
                        freeAgentDom = JsonNode.Parse(await core.ExecuteScriptAsync(
                            "({rows:document.querySelectorAll('.roster-player-pane .ant-table-row').length,"
                            + "group:document.querySelector('.roster-player-pane .toolbar')?.textContent ?? '',"
                            + "detailCleared:!document.querySelector('.detail-host .field-toolbar')})"));
                        if (freeAgentDom?["group"]?.GetValue<string>().StartsWith("自由球员") == true)
                        {
                            break;
                        }
                    }
                    report["freeAgentUi"] = freeAgentDom;
                    report["prospectUiOk"] = await ProspectUiCheck.Run(core, report, outPath);
                    await core.ExecuteScriptAsync(
                        "(() => { const items = Array.from(document.querySelectorAll('.ant-segmented-item'));"
                        + " const gs = items.find(i => (i.textContent || '').trim() === '比赛设置');"
                        + " gs?.click(); return !!gs; })()");
                    JsonNode? gsDom = null;
                    for (int attempt = 0; attempt < 24; attempt++)
                    {
                        await Task.Delay(500);
                        string probe = await core.ExecuteScriptAsync(
                            "({"
                            + "rows: document.querySelectorAll('.gs-panel .ant-table-row').length,"
                            + "ready: !document.querySelector('.gs-panel .ant-spin-spinning, .gs-panel .ant-btn-loading'),"
                            + "toolbars: document.querySelectorAll('.gs-panel .toolbar').length,"
                            + " text: (document.querySelector('.gs-panel')?.innerText || '').slice(0, 120)"
                            + "})");
                        gsDom = JsonNode.Parse(probe);
                        if ((gsDom?["rows"]?.GetValue<int>() ?? 0) >= 70
                            && gsDom?["ready"]?.GetValue<bool>() == true)
                        {
                            break;
                        }
                    }
                    report["gs"] = gsDom;
                    report["gs_rows"] = gsDom?["rows"]?.GetValue<int>() ?? 0;
                    report["gs_toolbars"] = gsDom?["toolbars"]?.GetValue<int>() ?? 0;

                    // The in-match array is a separate session and must expose the same 70 fields.
                    await core.ExecuteScriptAsync(
                        "(() => { const items = Array.from(document.querySelectorAll('.gs-panel .ant-segmented-item'));"
                        + " const m = items.find(i => (i.textContent || '').trim() === '当前比赛设置');"
                        + " m?.click(); return !!m; })()");
                    JsonNode? matchDom = null;
                    for (int attempt = 0; attempt < 24; attempt++)
                    {
                        await Task.Delay(500);
                        string probe = await core.ExecuteScriptAsync(
                            "({"
                            + "rows: document.querySelectorAll('.gs-panel .ant-table-row').length,"
                            + "ready: !document.querySelector('.gs-panel .ant-spin-spinning, .gs-panel .ant-btn-loading'),"
                            + "source: document.querySelector('.gs-panel .ant-segmented-item-selected')?.textContent.trim(),"
                            + " text: (document.querySelector('.gs-panel')?.innerText || '').slice(0, 80)"
                            + "})");
                        matchDom = JsonNode.Parse(probe);
                        if ((matchDom?["rows"]?.GetValue<int>() ?? 0) >= 70
                            && matchDom?["ready"]?.GetValue<bool>() == true)
                        {
                            break;
                        }
                    }
                    report["in_match"] = matchDom;
                    report["in_match_rows"] = matchDom?["rows"]?.GetValue<int>() ?? 0;

                    // The roster tab mounts three selectors and a team count; it writes nothing.
                    await core.ExecuteScriptAsync(
                        "(() => { const items = Array.from(document.querySelectorAll('.ant-segmented-item'));"
                        + " const m = items.find(i => (i.textContent || '').includes('调队'));"
                        + " m?.click(); return !!m; })()");
                    JsonNode? rosterDom = null;
                    for (int attempt = 0; attempt < 24; attempt++)
                    {
                        await Task.Delay(500);
                        string probe = await core.ExecuteScriptAsync(
                            "({"
                            + "panel: !!document.querySelector('.roster-panel'),"
                            + "selects: document.querySelectorAll('.roster-panel .ant-select').length,"
                            + "buttons: document.querySelectorAll('.roster-panel button').length,"
                            + " text: (document.querySelector('.roster-panel')?.innerText || '').slice(0, 80)"
                            + "})");
                        rosterDom = JsonNode.Parse(probe);
                        if ((rosterDom?["selects"]?.GetValue<int>() ?? 0) >= 3)
                        {
                            break;
                        }
                    }
                    report["roster"] = rosterDom;
                    report["roster_selects"] = rosterDom?["selects"]?.GetValue<int>() ?? 0;
                    teamLogosOk &= await TeamLogoUiCheck.RosterPage(core, report, outPath);

                    // The teams tab lists team records and shows their section tabs.
                    await core.ExecuteScriptAsync(
                        "(() => { const items = Array.from(document.querySelectorAll('.ant-segmented-item'));"
                        + " const m = items.find(i => (i.textContent || '').trim() === '球队资料');"
                        + " m?.click(); return !!m; })()");
                    JsonNode? teamDom = null;
                    for (int attempt = 0; attempt < 24; attempt++)
                    {
                        await Task.Delay(500);
                        string probe = await core.ExecuteScriptAsync(
                            "({"
                            + "rows: document.querySelectorAll('.ant-table-row').length,"
                            + "tabs: document.querySelectorAll('.ant-tabs-tab').length,"
                            + " text: (document.body.innerText || '').slice(-120)"
                            + "})");
                        teamDom = JsonNode.Parse(probe);
                        if ((teamDom?["rows"]?.GetValue<int>() ?? 0) >= 5
                            && (teamDom?["tabs"]?.GetValue<int>() ?? 0) >= 5)
                        {
                            break;
                        }
                    }
                    report["team"] = teamDom;
                    report["team_rows"] = teamDom?["rows"]?.GetValue<int>() ?? 0;
                    report["team_tabs"] = teamDom?["tabs"]?.GetValue<int>() ?? 0;
                    teamLogosOk &= await TeamLogoUiCheck.TeamPage(core, report, outPath);

                    // The journals tab lists what this editor has written.
                    await core.ExecuteScriptAsync(
                        "(() => { const items = Array.from(document.querySelectorAll('.ant-segmented-item'));"
                        + " const m = items.find(i => (i.textContent || '').trim() === '操作记录');"
                        + " m?.click(); return !!m; })()");
                    JsonNode? journalDom = null;
                    for (int attempt = 0; attempt < 20; attempt++)
                    {
                        await Task.Delay(500);
                        string probe = await core.ExecuteScriptAsync(
                            "({rows: document.querySelectorAll('.ant-table-row').length,"
                            + " text: (document.body.innerText || '').slice(-100)})");
                        journalDom = JsonNode.Parse(probe);
                        if ((journalDom?["rows"]?.GetValue<int>() ?? 0) > 0)
                        {
                            break;
                        }
                    }
                    report["journal_rows"] = journalDom?["rows"]?.GetValue<int>() ?? 0;
                    report["journal_text"] = journalDom?["text"]?.DeepClone();
                }
                bool gsOk = (report["gs_rows"]?.GetValue<int>() ?? 0) >= 70
                    && report["gs"]?["ready"]?.GetValue<bool>() == true;
                bool inMatchOk = (report["in_match_rows"]?.GetValue<int>() ?? 0) >= 70
                    && report["in_match"]?["ready"]?.GetValue<bool>() == true
                    && report["in_match"]?["source"]?.GetValue<string>() == "当前比赛设置";
                bool rosterOk = (report["roster_selects"]?.GetValue<int>() ?? 0) >= 3;
                bool avatarOk = (report["avatars"]?.GetValue<int>() ?? 0) > 0;
                bool selectionOk = (report["selectionColumns"]?.GetValue<int>() ?? 0) > 0;
                bool sourceOk = (report["sourceTabs"]?.GetValue<int>() ?? 0) == 2;
                sourceOk &= report["prospectUiOk"]?.GetValue<bool>() == true;
                bool navigationOk = (report["teamEntries"]?.GetValue<int>() ?? 0) > 3
                    && report["freeAgentEntry"]?.GetValue<bool>() == true
                    && report["freeAgentUi"]?["group"]?.GetValue<string>().StartsWith("自由球员") == true
                    && report["freeAgentUi"]?["detailCleared"]?.GetValue<bool>() == true;
                bool teamOk = (report["team_rows"]?.GetValue<int>() ?? 0) >= 5
                    && (report["team_tabs"]?.GetValue<int>() ?? 0) >= 5;
                bool journalOk = (report["journal_rows"]?.GetValue<int>() ?? 0) > 0;
                report["writes"] = form!.WriteAttempts;
                bool zeroWrites = form.WriteAttempts == 0;
                report["status"] = rendered
                    ? (finalRows > 0 && detailOk && gsOk && inMatchOk && rosterOk && avatarOk && selectionOk
                        && teamOk && journalOk && sourceOk && navigationOk && zeroWrites && teamLogosOk
                        ? "ok"
                        : "rendered_without_data")
                    : "failed";
                exit = rendered && finalRows > 0 && detailOk && gsOk && inMatchOk && rosterOk && avatarOk
                    && selectionOk && teamOk && journalOk && sourceOk && navigationOk && zeroWrites && teamLogosOk
                    ? 0
                    : 1;
            }
            catch (Exception exc)
            {
                report["error"] = exc.Message;
            }
            finally
            {
                report["writes"] = form!.WriteAttempts;
                Close();
            }
        });
        Application.Run(form);
        Program.WriteReport(report, outPath);
        return exit;
    }

    private static JsonObject Reply(Bridge bridge, string command, JsonObject payload)
    {
        var request = new JsonObject
        {
            ["id"] = "check-" + command,
            ["command"] = command,
            ["payload"] = payload,
        };
        JsonObject reply = JsonNode.Parse(bridge.Handle(request.ToJsonString(Program.JsonOptions)))!.AsObject();
        if (reply["ok"]?.GetValue<bool>() != true)
        {
            throw new InvalidOperationException(
                $"{command} 失败：{reply["error"]?.GetValue<string>() ?? "未知错误"}");
        }
        return reply;
    }
}
