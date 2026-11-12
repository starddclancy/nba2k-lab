using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.Tests;

/// <summary>Offline checks for the preset format: catalog shape, validation, save and load.</summary>
internal static class PresetTests
{
    public static void Run(Harness harness)
    {
        Catalog(harness);
        Keys(harness);
        Validation(harness);
        Document(harness);
        Diff(harness);
        FileRoundTrip(harness);
        Rejections(harness);
    }

    private static void Catalog(Harness harness)
    {
        harness.Equal("preset: format", "nba2k27-native-menu-preset/1", GsPresets.Format);
        harness.Equal("preset: max bytes", 64 * 1024, GsPresets.MaxPresetBytes);
        harness.Equal("preset: field count", 70, GsPresets.Catalog.Count);
        harness.Equal("preset: sides", 2, GsPresets.Sides.Length);
        harness.Equal("preset: side labels", "用户", GsPresets.SideLabels["user"]);
        harness.Equal("preset: cpu side label", "电脑", GsPresets.SideLabels["cpu"]);
        harness.Equal("preset: group count", 6, GsPresets.Groups.Count);
        harness.Equal("preset: group label", "进攻", GsPresets.Groups["offense"]);

        // Every catalog entry must resolve back to itself through the id index.
        bool indexed = GsPresets.Catalog.All(field =>
            GsPresets.FieldsById.TryGetValue(field.Id, out GsField? found) && ReferenceEquals(found, field));
        harness.Check("preset: every field is indexed by id", indexed);

        bool labelled = GsPresets.Catalog.All(field =>
            GsPresets.Groups.ContainsKey(field.Group)
            && !string.IsNullOrWhiteSpace(field.Label)
            && GsFieldIds.Ids.ContainsKey(field.Id));
        harness.Check("preset: every field has a known group, label and gs id", labelled);

        harness.Equal("preset: catalog ids are unique", 70, GsPresets.Catalog.Select(f => f.Id).Distinct().Count());
    }

    private static void Keys(Harness harness)
    {
        harness.Equal("preset: key count", 140, GsPresets.AllKeys.Count);
        harness.Equal("preset: keys are unique", 140, GsPresets.AllKeys.Distinct().Count());
        // Order is field-major then user/cpu, matching the Python catalog that produces the files.
        harness.Equal("preset: first key", "user.open_shot_bonus", GsPresets.AllKeys[0]);
        harness.Equal("preset: second key", "cpu.open_shot_bonus", GsPresets.AllKeys[1]);
        harness.Equal("preset: inside_shot pair follows three offense fields", "user.inside_shot",
            GsPresets.AllKeys[6]);
        harness.Equal("preset: inside_shot cpu partner", "cpu.inside_shot", GsPresets.AllKeys[7]);
        harness.Equal("preset: last key", "cpu.acceleration_min", GsPresets.AllKeys[^1]);

        bool split = GsPresets.AllKeys.All(key =>
        {
            var (side, fieldId) = GsPresets.SplitKey(key);
            return GsPresets.FieldKey(side, fieldId) == key;
        });
        harness.Check("preset: every key splits and rebuilds", split);

        harness.Equal("preset: field key", "user.speed", GsPresets.FieldKey("user", "speed"));
        harness.Throws("preset: unknown side rejected", () => GsPresets.FieldKey("both", "speed"));
        harness.Throws("preset: unknown field rejected", () => GsPresets.FieldKey("user", "nope"),
            "Unknown field: 'nope'");
        harness.Throws("preset: malformed key rejected", () => GsPresets.SplitKey("speed"));
    }

    private static void Validation(Harness harness)
    {
        var good = GsPresets.AllKeys.Select(key => new KeyValuePair<string, int>(key, 50)).ToList();
        var validated = GsPresets.ValidateValues(good);
        harness.Equal("preset: validate keeps all keys", 140, validated.Count);

        harness.Throws("preset: rejects 101",
            () => GsPresets.ValidateValues(new[] { Pair("user.speed", 101) }),
            "user.speed: expected an integer from 0 to 100.");
        harness.Throws("preset: rejects -1",
            () => GsPresets.ValidateValues(new[] { Pair("user.speed", -1) }),
            "user.speed: expected an integer from 0 to 100.");

        // A missing key is accepted by validation; the caller decides whether a full preset is required.
        harness.Equal("preset: partial values validate", 1,
            GsPresets.ValidateValues(new[] { Pair("user.speed", 0) }).Count);
    }

    private static KeyValuePair<string, int> Pair(string key, int value) => new(key, value);

    private static void Document(Harness harness)
    {
        var values = GsPresets.AllKeys.Select(key => Pair(key, 42)).ToList();
        var document = GsPresets.MakeDocument(values);
        harness.Equal("preset: document format", GsPresets.Format, document["format"]!.GetValue<string>());
        var node = document["values"]!.AsObject();
        harness.Equal("preset: document value count", 140, node.Count);
        harness.Equal("preset: document order follows catalog", "user.open_shot_bonus", node.First().Key);
        harness.Equal("preset: document value", 42, node["user.inside_shot"]!.GetValue<int>());
    }

    private static void Diff(Harness harness)
    {
        var before = GsPresets.AllKeys.ToDictionary(key => key, _ => 50);
        var after = new Dictionary<string, int>(before) { ["user.speed"] = 60 };
        var changes = GsPresets.DiffValues(before, after);
        harness.Equal("preset: diff finds one change", 1, changes.Count);
        harness.Equal("preset: diff key", "user.speed", changes[0].Key);
        harness.Equal("preset: diff before", 50, changes[0].Before);
        harness.Equal("preset: diff after", 60, changes[0].After);
        harness.Equal("preset: identical values diff empty", 0,
            GsPresets.DiffValues(before, before).Count);
    }

    private static void FileRoundTrip(Harness harness)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nba2k27-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string path = Path.Combine(directory, "preset.json");
            var values = GsPresets.AllKeys.Select((key, index) => Pair(key, index % 101)).ToList();
            string saved = GsPresets.SavePreset(path, values, overwrite: true);
            harness.Equal("preset: save returns path", path, saved);
            harness.Check("preset: file exists", File.Exists(path));

            byte[] raw = File.ReadAllBytes(path);
            harness.Check("preset: file has no CRLF", !raw.Contains((byte)'\r'));
            harness.Check("preset: file ends with newline", raw[^1] == (byte)'\n');
            harness.Equal("preset: utf8 without BOM", (byte)'{', raw[0]);

            var loaded = GsPresets.LoadPreset(path);
            harness.Equal("preset: load round trip count", 140, loaded.Count);
            harness.Check("preset: load round trip values",
                values.All(pair => loaded[pair.Key] == pair.Value));

            // Overwrite must be refused unless requested.
            harness.Throws("preset: save refuses to overwrite", () => GsPresets.SavePreset(path, values));
            harness.Equal("preset: overwrite succeeds", path, GsPresets.SavePreset(path, values, overwrite: true));

            // No temporary file may survive a successful save.
            harness.Check("preset: no temp file left",
                !Directory.EnumerateFiles(directory, "*.tmp").Any());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Rejections(Harness harness)
    {
        string directory = Path.Combine(Path.GetTempPath(), "nba2k27-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string Probe(string name, string content)
            {
                string path = Path.Combine(directory, name + ".json");
                File.WriteAllText(path, content);
                return path;
            }

            harness.Throws("preset: rejects duplicate key", () => GsPresets.LoadPreset(Probe("dup",
                    """{"format":"nba2k27-native-menu-preset/1","values":{"user.speed":50,"user.speed":51}}""")),
                "Duplicate JSON key: 'user.speed'");

            harness.Throws("preset: rejects wrong format", () => GsPresets.LoadPreset(Probe("fmt",
                    """{"format":"nba2k27-other/1","values":{}}""")),
                "Unsupported preset format: 'nba2k27-other/1'");

            harness.Throws("preset: rejects extra key", () => GsPresets.LoadPreset(Probe("extra",
                    """{"format":"nba2k27-native-menu-preset/1","values":{},"extra":1}""")),
                "Preset must contain exactly 'format' and 'values'.");

            harness.Throws("preset: rejects out of range", () => GsPresets.LoadPreset(Probe("range",
                    """{"format":"nba2k27-native-menu-preset/1","values":{"user.speed":101}}""")),
                "user.speed: expected an integer from 0 to 100.");

            harness.Throws("preset: rejects unknown field", () => GsPresets.LoadPreset(Probe("field",
                    """{"format":"nba2k27-native-menu-preset/1","values":{"user.nope":50}}""")),
                "Unknown field: 'nope'");

            harness.Throws("preset: rejects non-integer value", () => GsPresets.LoadPreset(Probe("float",
                    """{"format":"nba2k27-native-menu-preset/1","values":{"user.speed":50.5}}""")));

            harness.Throws("preset: rejects malformed json", () => GsPresets.LoadPreset(Probe("bad", "{oops")));

            string missing = Path.Combine(directory, "missing.json");
            harness.Throws("preset: rejects missing file", () => GsPresets.LoadPreset(missing));

            // Oversized input must be refused before parsing.
            string huge = Probe("huge", new string(' ', GsPresets.MaxPresetBytes + 1));
            harness.Throws("preset: rejects oversized file", () => GsPresets.LoadPreset(huge));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
