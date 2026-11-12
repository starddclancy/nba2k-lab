using System.Text.Json.Nodes;

namespace Nba2k27Editor.WebHost;

/// <summary>Exercise snapshot routing and no-op submission without changing game memory.</summary>
internal static class PlayerReadCheck
{
    internal static JsonObject Run(Bridge bridge, int firstIndex, JsonArray roster, JsonArray prospects)
    {
        const string key = "Attributes/Offense/3POINT";
        var checks = new JsonObject();
        JsonNode Call(string command, JsonObject payload)
        {
            var response = Send(command, payload);
            if (response["ok"]?.GetValue<bool>() != true)
            {
                throw new InvalidOperationException(response["error"]?.GetValue<string>() ?? command);
            }
            return response["data"]!;
        }
        JsonObject Send(string command, JsonObject payload) => JsonNode.Parse(bridge.Handle(new JsonObject
        {
            ["id"] = "reads-" + command, ["command"] = command, ["payload"] = payload,
        }.ToJsonString()))!.AsObject();
        void Reject(string name, string command, JsonObject payload, string error)
        {
            JsonObject response = Send(command, payload);
            checks[name] = response["ok"]?.GetValue<bool>() == false
                && (response["error"]?.GetValue<string>() ?? "").Contains(error, StringComparison.Ordinal);
        }

        string source = prospects.Count > 0 ? "prospects" : "roster";
        JsonArray available = prospects.Count > 0 ? prospects : roster;
        int index = available[0]!["index"]!.GetValue<int>();
        int other = available[1]!["index"]!.GetValue<int>();
        JsonNode detail = Call("readPlayer", new() { ["index"] = index, ["source"] = source });
        checks["rosterSummaries"] = roster.All(row => row?["summary"] is JsonObject summary
            && new[] { "position", "secondaryPosition", "birthYear", "heightCm", "weightKg", "threePoint",
                "age", "overall" }
                .All(summary.ContainsKey));
        checks["listDetailSummary"] = detail["summary"] is JsonObject
            && JsonNode.DeepEquals(available[0]?["summary"], detail["summary"]);
        var summaryKeys = new Dictionary<string, string>
        {
            ["position"] = "Vitals/Vitals/POSITION",
            ["secondaryPosition"] = "Vitals/Vitals/SECONDARYPOSITION",
            ["birthYear"] = "Vitals/Vitals/BIRTHYEAR",
            ["heightCm"] = "Vitals/Body/HEIGHT",
            ["weightKg"] = "Vitals/Body/WEIGHT",
            ["threePoint"] = key,
        };
        var editorFields = detail["sections"]!.AsArray()
            .SelectMany(section => section!["fields"]!.AsArray())
            .ToDictionary(field => field!["key"]!.GetValue<string>(), field => field!["value"]);
        checks["summaryEditorValues"] = summaryKeys.All(pair => detail["summary"]?[pair.Key] is not { } fact
            || editorFields.TryGetValue(pair.Value, out var value) && JsonNode.DeepEquals(fact, value));
        checks["summaryAgeValuesValid"] = roster.All(row => row?["summary"]?["age"] is not JsonValue age
            || age.GetValue<int>() is >= 0 and <= 120);
        checks["summaryOverallValuesValid"] = roster.All(row => row?["summary"]?["overall"] is not JsonValue rating
            || rating.GetValue<int>() is >= 25 and <= 99);
        string token = detail["readToken"]!.GetValue<string>();
        JsonNode current = detail["sections"]!.AsArray().SelectMany(section => section!["fields"]!.AsArray())
            .Single(field => field!["key"]!.GetValue<string>() == key)!["value"]!;
        JsonObject Edit(string readToken, int target, string browseSource) => new()
        {
            ["index"] = target, ["source"] = browseSource, ["readToken"] = readToken,
            ["changes"] = new JsonObject { [key] = current.DeepClone() },
        };
        Reject("invalidToken", "applyPlayer", Edit("missing", index, source), "快照已失效");
        Reject("emptyToken", "applyPlayer", Edit("", index, source), "快照已失效");
        var nullToken = Edit(token, index, source);
        nullToken["readToken"] = null;
        Reject("nullToken", "applyPlayer", nullToken, "读取快照不能为空");
        Reject("wrongIndex", "applyPlayer", Edit(token, other, source), "球员选择不一致");
        if (source == "prospects")
        {
            Reject("wrongSource", "applyPlayer", Edit(token, index, "roster"), "球员来源不一致");
        }
        JsonNode same = Call("applyPlayer", Edit(token, index, source));
        checks["singleNoopPatches"] = same["patches"]?.GetValue<int>() == 0;
        Reject("consumedToken", "applyPlayer", Edit(token, index, source), "快照已失效");

        JsonArray Selection() => new(JsonValue.Create(index), JsonValue.Create(other));
        JsonNode preview = Call("readBatch", new()
        {
            ["indices"] = Selection(), ["source"] = source, ["field"] = key,
        });
        string batchToken = preview["readToken"]!.GetValue<string>();
        checks["batchReadBoth"] = preview["players"]!.AsArray().Count == 2
            && preview["field"]!["key"]!.GetValue<string>() == key;
        Reject("batchFieldMismatch", "applyBatch", new()
        {
            ["indices"] = Selection(), ["source"] = source, ["readToken"] = batchToken,
            ["changes"] = new JsonObject { ["Vitals/Vitals/NUMBER"] = 1 },
        }, "字段不在读取快照");
        Reject("batchSelectionMismatch", "applyBatch", new()
        {
            ["indices"] = new JsonArray(JsonValue.Create(index)), ["source"] = source,
            ["readToken"] = batchToken, ["changes"] = new JsonObject { [key] = current.DeepClone() },
        }, "球员选择不一致");
        Reject("duplicateBatch", "readBatch", new()
        {
            ["indices"] = new JsonArray(JsonValue.Create(index), JsonValue.Create(index)),
            ["source"] = source, ["field"] = key,
        }, "请选择不同的球员");
        Reject("unknownBatchField", "readBatch", new()
        {
            ["indices"] = Selection(), ["source"] = source, ["field"] = "unknown",
        }, "字段不存在或不可写");
        JsonNode singlePreview = Call("readBatch", new()
        {
            ["indices"] = new JsonArray(JsonValue.Create(index)), ["source"] = source, ["field"] = key,
        });
        Reject("partialImport", "importPlayer", new()
        {
            ["index"] = index, ["source"] = source, ["readToken"] = singlePreview["readToken"]!.DeepClone(),
        }, "仅包含批量字段");
        JsonNode noOpBatch = Call("applyBatch", new()
        {
            ["indices"] = new JsonArray(JsonValue.Create(index)), ["source"] = source,
            ["readToken"] = singlePreview["readToken"]!.DeepClone(),
            ["changes"] = new JsonObject { [key] = singlePreview["players"]![0]!["value"]!.DeepClone() },
        });
        checks["batchNoopPatches"] = noOpBatch["patches"]?.GetValue<int>() == 0;
        // Reconnecting also invalidates handles when the same game process remains running.
        JsonNode beforeReconnect = Call("readPlayer", new() { ["index"] = firstIndex });
        int writesBeforeReconnect = bridge.PlayerWriteAttempts;
        Call("connect", new());
        Reject("reconnectInvalidates", "applyPlayer",
            Edit(beforeReconnect["readToken"]!.GetValue<string>(), firstIndex, "roster"), "快照已失效");
        checks["zeroWrites"] = writesBeforeReconnect == 0 && bridge.PlayerWriteAttempts == 0;
        checks["ok"] = checks.All(pair => pair.Value?.GetValue<bool>() == true);
        return checks;
    }
}
