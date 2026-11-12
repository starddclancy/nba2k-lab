using System.Globalization;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.Cli;

/// <summary>Offline preset save/load/diff plus a live capture that never writes to the game.</summary>
public static class GsPresetCommand
{
    public static int Run(Dictionary<string, string> options)
    {
        string mode = options.GetValueOrDefault("mode", "load");
        string? path = options.GetValueOrDefault("path");
        bool fromLive = options.ContainsKey("from-live");
        bool overwrite = options.ContainsKey("overwrite");
        string? outPath = options.GetValueOrDefault("out");
        string? valuesText = options.GetValueOrDefault("values");

        if (mode == "save")
        {
            Dictionary<string, int> values;
            if (fromLive)
            {
                using var session = new NativeGeneralGsSession(options.GetValueOrDefault("data-dir"));
                var snapshot = session.Connect();
                values = new Dictionary<string, int>(snapshot.Values, StringComparer.Ordinal);
                Console.WriteLine($"captured {values.Count} live values, writes {session.WriteAttempts}");
            }
            else if (valuesText is not null)
            {
                values = ParseValues(valuesText);
            }
            else
            {
                throw new ArgumentException("save mode needs --from-live or --values");
            }
            var saved = GsPresets.SavePreset(path ?? throw new ArgumentException("save mode needs --path"),
                values, overwrite);
            Console.WriteLine($"saved {values.Count} values to {saved}");
            return 0;
        }

        if (path is null)
        {
            throw new ArgumentException("load/diff mode needs --path");
        }
        var preset = GsPresets.LoadPreset(path);
        Console.WriteLine($"loaded {preset.Count} values from {Path.GetFullPath(path)}");
        var report = new JsonObject
        {
            ["format"] = GsPresets.Format,
            ["fields"] = GsPresets.Catalog.Count,
            ["groups"] = GsPresets.Groups.Count,
            ["value_count"] = preset.Count,
            ["keys"] = new JsonArray(preset.Keys.Select(key => (JsonNode?)JsonValue.Create(key)).ToArray()),
            ["values"] = Nodes(preset),
        };
        if (mode == "diff" && valuesText is not null)
        {
            var changes = GsPresets.DiffValues(preset, ParseValues(valuesText));
            report["changes"] = new JsonArray(changes
                .Select(change => (JsonNode?)new JsonObject
                {
                    ["key"] = change.Key,
                    ["before"] = change.Before,
                    ["after"] = change.After,
                })
                .ToArray());
            Console.WriteLine($"diff {changes.Count} changes");
        }
        if (outPath is not null)
        {
            File.WriteAllText(outPath, report.ToJsonString(JournalJson.Options));
            Console.WriteLine($"report: {Path.GetFullPath(outPath)}");
        }
        return 0;
    }

    private static Dictionary<string, int> ParseValues(string text)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            int equals = part.IndexOf('=');
            if (equals <= 0)
            {
                throw new ArgumentException($"expected key=value, got {part}");
            }
            values[part[..equals].Trim()] = int.Parse(part[(equals + 1)..].Trim(), CultureInfo.InvariantCulture);
        }
        return values;
    }

    private static JsonObject Nodes(IReadOnlyDictionary<string, int> values)
    {
        var node = new JsonObject();
        foreach (var pair in values)
        {
            node[pair.Key] = pair.Value;
        }
        return node;
    }
}
