using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Editing;

/// <summary>Reads a bounded, display-only summary from known journal files.</summary>
public static class JournalReader
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    private static readonly HashSet<string> Supported = new(StringComparer.Ordinal)
    {
        "nba2k27-player-edit/1", "nba2k27-player-batch-edit/1",
        "nba2k27-general-gs-edit/1", "nba2k27-native-gs-edit/1",
        "nba2k27-history-edit/1", "nba2k27-team-edit/1",
    };

    public static JsonObject Read(string directory, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName != Path.GetFileName(fileName)
            || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            || fileName.Contains(':'))
        {
            throw new ArgumentException("Journal filename is not a listed JSON file.", nameof(fileName));
        }
        string root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string path = Path.GetFullPath(Path.Combine(root, fileName));
        if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Journal file is outside its directory.", nameof(fileName));
        }
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                return Invalid("Linked journal files are not supported.");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length > MaximumBytes)
                return Invalid("Journal file exceeds the display size limit.");
            using var reader = new StreamReader(stream);
            return Parse(reader.ReadToEnd());
        }
        catch (IOException)
        {
            return Invalid("Journal file is unavailable.");
        }
        catch (UnauthorizedAccessException)
        {
            return Invalid("Journal file cannot be read.");
        }
    }

    public static JsonObject Parse(string json)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(json);
            if (node is not JsonObject document || document["format"] is not JsonValue formatNode
                || !formatNode.TryGetValue<string>(out string? format))
            {
                return Invalid("Journal JSON must contain an object format.");
            }
            if (!Supported.Contains(format))
            {
                return new JsonObject { ["state"] = "unsupported", ["format"] = format, ["error"] = "" };
            }
            var result = new JsonObject
            {
                ["state"] = "supported", ["format"] = format, ["status"] = Text(document["status"]),
                ["mode"] = Text(document["mode"]), ["time"] = Text(document["time"]),
                ["error"] = Text(document["error"]), ["changes"] = new JsonArray(),
            };
            var changes = (JsonArray)result["changes"]!;
            if (format.StartsWith("nba2k27-player-", StringComparison.Ordinal))
            {
                if (document["players"] is not JsonArray players) return Invalid("Player journal has no changes.");
                foreach (JsonNode? player in players)
                {
                    if (player is not JsonObject row || row["changes"] is not JsonArray items)
                        return Invalid("Player journal changes are invalid.");
                    foreach (JsonNode? item in items)
                    {
                        if (item is not JsonObject change || string.IsNullOrWhiteSpace(Text(change["field"]))
                            || !change.ContainsKey("before") || !change.ContainsKey("after"))
                            return Invalid("Player journal field is invalid.");
                        changes.Add(Change(Text(row["player"]), Text(change["field"]),
                            Value(change["before"]), Value(change["after"])));
                    }
                }
            }
            else if (format == "nba2k27-native-gs-edit/1")
            {
                if (document["field"] is null || !document.ContainsKey("before") || !document.ContainsKey("after"))
                    return Invalid("Native GS journal has no field values.");
                changes.Add(Change("", Text(document["field"]), Value(document["before"]), Value(document["after"])));
            }
            else
            {
                string arrayName = format is "nba2k27-history-edit/1" or "nba2k27-team-edit/1" ? "fields" : "changes";
                if (document[arrayName] is not JsonArray items) return Invalid("Journal changes are invalid.");
                foreach (JsonNode? item in items)
                {
                    if (item is not JsonObject change || !change.ContainsKey("before") || !change.ContainsKey("after"))
                        return Invalid("Journal change is invalid.");
                    string fieldName = Text(change["field"] ?? change["key"]);
                    if (string.IsNullOrWhiteSpace(fieldName)) return Invalid("Journal field is invalid.");
                    changes.Add(Change(Text(document["team"] ?? document["label"]),
                        fieldName, Value(change["before"]), Value(change["after"])));
                }
            }
            return result;
        }
        catch (JsonException)
        {
            return Invalid("Invalid JSON.");
        }
        catch (InvalidOperationException)
        {
            return Invalid("Journal values are invalid.");
        }
    }

    private static JsonObject Change(string subject, string field, string before, string after) =>
        new() { ["subject"] = subject, ["field"] = field, ["before"] = before, ["after"] = after };

    private static string Value(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out string? text)
        ? text : node?.ToJsonString() ?? "null";
    private static string Text(JsonNode? node) => node?.GetValue<string>() ?? "";
    private static JsonObject Invalid(string error) =>
        new() { ["state"] = "invalid", ["format"] = "", ["error"] = error, ["changes"] = new JsonArray() };
}
