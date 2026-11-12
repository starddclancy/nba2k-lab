using System.Text.Json;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;

namespace Nba2k27Editor.WebHost;

internal sealed partial class Bridge
{
    private JsonNode HistoryCommand(string command, JsonObject payload)
    {
        Session().Guard();
        var history = _history ?? throw new InvalidOperationException("请重新连接游戏");
        string key = payload["key"]?.GetValue<string>() ?? "";
        switch (command)
        {
            case "teamSeasons":
                return JsonSerializer.SerializeToNode(history.TeamSeasons(Required(payload, "teamIndex").GetValue<int>()))!;
            case "historyList":
                return JsonSerializer.SerializeToNode(history.Catalog(
                    payload["kind"]?.GetValue<string>() ?? "league", payload["teamIndex"]?.GetValue<int>()),
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })!;
            case "historyRead":
                var snapshot = history.Read(key);
                var fields = new JsonArray();
                foreach (var field in snapshot.Fields)
                {
                    var value = snapshot.Values[field.Key];
                    fields.Add(new JsonObject
                    {
                        ["key"] = field.Key, ["label"] = field.Label,
                        ["section"] = snapshot.Item.Category, ["group"] = snapshot.Item.Category,
                        ["value"] = ToJson(value), ["display"] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture),
                        ["writable"] = field.Writable,
                        ["kind"] = field.Type == "team" ? "choice" : field.Type is "string" or "wstring" ? "text" : "number",
                        ["options"] = field.Type == "team" ? new JsonArray(
                            new[] { "保留现有关联" }.Concat(snapshot.Teams.Select(HistorySession.TeamOption))
                                .Select(option => (JsonNode?)JsonValue.Create(option)).ToArray()) : new JsonArray(),
                        ["min"] = 0, ["max"] = field.Maximum,
                    });
                }
                return new JsonObject
                {
                    ["key"] = key, ["label"] = snapshot.Item.Label, ["readToken"] = snapshot.Token,
                    ["address"] = $"0x{snapshot.Address:X}", ["fields"] = fields,
                    ["sharedTeams"] = JsonSerializer.SerializeToNode(snapshot.SharedTeams),
                    ["undoDepth"] = history.UndoDepth,
                    ["undoKey"] = _historyUndoKeys.LastOrDefault(),
                };
            case "historyApply":
                var changes = Required(payload, "changes").AsObject()
                    .ToDictionary(pair => pair.Key, pair => ToClr(pair.Value));
                var applied = history.Apply(key, Required(payload, "readToken").GetValue<string>(), changes,
                    payload["acknowledgeShared"]?.GetValue<bool>() ?? false);
                if (applied is not null)
                {
                    _historyUndoKeys.Add(key);
                }
                return new JsonObject
                {
                    ["patched"] = applied is not null, ["patches"] = applied?.Patches.Count ?? 0,
                    ["journal"] = applied?.JournalPath, ["undoDepth"] = history.UndoDepth,
                    ["warning"] = applied?.Journal["warning"]?.DeepClone(),
                };
            case "historyUndo":
                var undone = history.Undo(key);
                _historyUndoKeys.RemoveAt(_historyUndoKeys.Count - 1);
                return new JsonObject { ["restored"] = undone.Patches.Count, ["journal"] = undone.JournalPath,
                    ["warning"] = undone.Journal["warning"]?.DeepClone(), ["undoDepth"] = history.UndoDepth };
            default:
                throw new InvalidOperationException("未知历史操作");
        }
    }

    private readonly List<string> _historyUndoKeys = new();
}
