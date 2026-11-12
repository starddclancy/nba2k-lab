using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Gs;

public sealed class PresetError : Exception
{
    public PresetError(string message) : base(message)
    {
    }
}

public sealed record GsField(string Id, string Label, string Group);

public sealed record GsChange(string Key, int? Before, int After);

/// <summary>Portable native-menu GS presets covering the 70 mapped fields and both sides.</summary>
public static class GsPresets
{
    public const string Format = "nba2k27-native-menu-preset/1";
    public const int MaxPresetBytes = 64 * 1024;

    public static readonly string[] Sides = { "user", "cpu" };

    public static readonly IReadOnlyDictionary<string, string> SideLabels = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["user"] = "用户",
        ["cpu"] = "电脑",
    };

    public static readonly IReadOnlyDictionary<string, string> Groups = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["offense"] = "进攻",
        ["defense"] = "防守",
        ["attributes"] = "属性",
        ["tendencies"] = "倾向",
        ["fouls"] = "犯规",
        ["movement"] = "移动",
    };

    /// <summary>The 70 menu records in menu order, with their Chinese labels.</summary>
    public static readonly IReadOnlyList<GsField> Catalog = new[]
    {
        new GsField("open_shot_bonus", "空位投篮加成程度", "offense"),
        new GsField("covered_shot_penalty", "干扰下投篮惩罚程度", "offense"),
        new GsField("cpu_shot_timing", "电脑投篮时机", "offense"),
        new GsField("inside_shot", "内线投篮命中率", "offense"),
        new GsField("close_shot", "近距离投篮命中率", "offense"),
        new GsField("mid_range", "中投命中率", "offense"),
        new GsField("three_point", "三分命中率", "offense"),
        new GsField("layup", "上篮命中率", "offense"),
        new GsField("free_throw_meter_size", "罚球投篮条尺寸", "offense"),
        new GsField("contact_dunk_frequency", "隔人扣篮频率", "offense"),
        new GsField("contact_dunk_success", "隔人扣篮成功率", "offense"),
        new GsField("pass_accuracy", "传球准确性", "offense"),
        new GsField("alley_oop_success", "空中接力成功率", "offense"),
        new GsField("contact_shot_success", "强行投篮成功率", "offense"),
        new GsField("ball_security", "护球能力", "offense"),
        new GsField("body_contact_impact", "身体接触影响", "offense"),
        new GsField("pass_speed", "传球速度", "offense"),
        new GsField("driving_contact_frequency", "身体对抗切入投篮频率", "defense"),
        new GsField("inside_contact_frequency", "身体对抗内线投篮频率", "defense"),
        new GsField("layup_defense_takeoff", "上篮防守强度（起跳）", "defense"),
        new GsField("layup_defense_release", "上篮防守强度（出手）", "defense"),
        new GsField("jump_shot_defense_takeoff", "防守跳投的强度（起跳）", "defense"),
        new GsField("jump_shot_defense_release", "防守跳投的强度（出手）", "defense"),
        new GsField("help_defense", "协防强度", "defense"),
        new GsField("steal_success", "抢断成功", "defense"),
        new GsField("agility", "敏捷", "attributes"),
        new GsField("vertical", "弹跳力", "attributes"),
        new GsField("strength", "力量", "attributes"),
        new GsField("stamina", "体力", "attributes"),
        new GsField("speed", "速度", "attributes"),
        new GsField("durability", "耐久", "attributes"),
        new GsField("hustle", "争抢", "attributes"),
        new GsField("ball_handling", "控球", "attributes"),
        new GsField("hands", "干扰传球", "attributes"),
        new GsField("dunking", "扣篮能力", "attributes"),
        new GsField("on_ball_defense", "持球防守", "attributes"),
        new GsField("stealing", "抢断", "attributes"),
        new GsField("blocking", "盖帽", "attributes"),
        new GsField("offensive_awareness", "进攻意识", "attributes"),
        new GsField("defensive_awareness", "防守意识", "attributes"),
        new GsField("offensive_rebounding", "进攻篮板", "attributes"),
        new GsField("defensive_rebounding", "防守篮板", "attributes"),
        new GsField("offensive_consistency", "进攻稳定性", "attributes"),
        new GsField("defensive_consistency", "防守稳定性", "attributes"),
        new GsField("fatigue_rate", "疲劳率", "attributes"),
        new GsField("inside_shot_tendency", "内线投篮倾向", "tendencies"),
        new GsField("close_shot_tendency", "近距离投篮倾向", "tendencies"),
        new GsField("mid_range_tendency", "中投倾向", "tendencies"),
        new GsField("three_point_tendency", "三分投篮倾向", "tendencies"),
        new GsField("post_shot_tendency", "背身投篮", "tendencies"),
        new GsField("drive_tendency", "篮下进攻", "tendencies"),
        new GsField("post_up_tendency", "寻找内线球员倾向", "tendencies"),
        new GsField("alley_oop_pass_tendency", "空中接力传球倾向", "tendencies"),
        new GsField("dunk_tendency", "尝试扣篮", "tendencies"),
        new GsField("putback_tendency", "尝试补篮", "tendencies"),
        new GsField("pass_interception_tendency", "切传球路线倾向", "tendencies"),
        new GsField("on_ball_steal_tendency", "持球抢断倾向", "tendencies"),
        new GsField("contest_shot_tendency", "干扰投篮", "tendencies"),
        new GsField("backdoor_cut_tendency", "底线切入倾向", "tendencies"),
        new GsField("over_the_back_foul_frequency", "背后犯规频率", "fouls"),
        new GsField("charging_foul_frequency", "进攻犯规频率", "fouls"),
        new GsField("blocking_foul_frequency", "阻挡犯规频率", "fouls"),
        new GsField("reaching_foul_frequency", "侵人犯规频率", "fouls"),
        new GsField("shooting_foul_frequency", "投篮犯规频率", "fouls"),
        new GsField("loose_ball_foul_frequency", "争球犯规频率", "fouls"),
        new GsField("illegal_screen_frequency", "非法掩护频率", "fouls"),
        new GsField("speed_max", "速度（最高）", "movement"),
        new GsField("speed_min", "速度（最低）", "movement"),
        new GsField("acceleration_max", "加速（最高）", "movement"),
        new GsField("acceleration_min", "加速（最低）", "movement"),
    };

    public static readonly IReadOnlyDictionary<string, GsField> FieldsById =
        Catalog.ToDictionary(field => field.Id, StringComparer.Ordinal);

    /// <summary>All 140 keys in menu order, user side before CPU side.</summary>
    public static readonly IReadOnlyList<string> AllKeys = Catalog
        .SelectMany(field => Sides.Select(side => $"{side}.{field.Id}"))
        .ToList();

    public static string FieldKey(string side, string fieldId)
    {
        if (Array.IndexOf(Sides, side) < 0)
        {
            throw new PresetError($"Unknown side: '{side}'");
        }
        if (!FieldsById.ContainsKey(fieldId))
        {
            throw new PresetError($"Unknown field: '{fieldId}'");
        }
        return $"{side}.{fieldId}";
    }

    public static (string Side, string FieldId) SplitKey(string key)
    {
        int dot = key.IndexOf('.');
        if (dot <= 0)
        {
            throw new PresetError($"Invalid field key: '{key}'");
        }
        string side = key[..dot];
        string fieldId = key[(dot + 1)..];
        if (FieldKey(side, fieldId) != key)
        {
            throw new PresetError($"Invalid field key: '{key}'");
        }
        return (side, fieldId);
    }

    /// <summary>Keep only known keys in menu order; values must be integers from 0 to 100.</summary>
    public static Dictionary<string, int> ValidateValues(IEnumerable<KeyValuePair<string, int>> values)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in values)
        {
            SplitKey(pair.Key);
            // A document constraint only; per-field game endpoints are unverified.
            if (pair.Value < 0 || pair.Value > 100)
            {
                throw new PresetError($"{pair.Key}: expected an integer from 0 to 100.");
            }
            result[pair.Key] = pair.Value;
        }
        var ordered = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var key in AllKeys)
        {
            if (result.TryGetValue(key, out int value))
            {
                ordered[key] = value;
            }
        }
        return ordered;
    }

    public static JsonObject MakeDocument(IEnumerable<KeyValuePair<string, int>> values) => new()
    {
        ["format"] = Format,
        ["values"] = Nodes(ValidateValues(values)),
    };

    public static IReadOnlyList<GsChange> DiffValues(
        IEnumerable<KeyValuePair<string, int>> current, IEnumerable<KeyValuePair<string, int>> proposed)
    {
        var before = ValidateValues(current);
        var after = ValidateValues(proposed);
        var changes = new List<GsChange>();
        foreach (var pair in after)
        {
            if (!before.TryGetValue(pair.Key, out int value) || value != pair.Value)
            {
                changes.Add(new GsChange(pair.Key, before.TryGetValue(pair.Key, out int old) ? old : null, pair.Value));
            }
        }
        return changes;
    }

    public static Dictionary<string, int> LoadPreset(string path, int maxBytes = MaxPresetBytes)
    {
        CheckLimit(maxBytes);
        byte[] raw;
        using (var stream = File.OpenRead(path))
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > maxBytes)
                {
                    throw new PresetError($"Preset exceeds the {maxBytes}-byte limit.");
                }
            }
            raw = buffer.ToArray();
        }
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = false });
        }
        catch (JsonException exc)
        {
            throw new PresetError($"Invalid UTF-8 JSON preset: {exc.Message}");
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new PresetError("Preset must contain exactly 'format' and 'values'.");
            }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!seen.Add(property.Name))
                {
                    throw new PresetError($"Duplicate JSON key: '{property.Name}'");
                }
            }
            if (seen.Count != 2 || !seen.Contains("format") || !seen.Contains("values"))
            {
                throw new PresetError("Preset must contain exactly 'format' and 'values'.");
            }
            var format = root.GetProperty("format");
            if (format.ValueKind != JsonValueKind.String || format.GetString() != Format)
            {
                throw new PresetError($"Unsupported preset format: '{format}'");
            }
            var values = root.GetProperty("values");
            if (values.ValueKind != JsonValueKind.Object)
            {
                throw new PresetError("Preset values must be an object.");
            }
            var parsed = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var property in values.EnumerateObject())
            {
                // Python's object_pairs_hook rejects duplicates in every object, so the
                // values object must be checked as well as the document root.
                if (parsed.ContainsKey(property.Name))
                {
                    throw new PresetError($"Duplicate JSON key: '{property.Name}'");
                }
                if (property.Value.ValueKind != JsonValueKind.Number
                    || !property.Value.TryGetInt32(out int value))
                {
                    throw new PresetError($"{property.Name}: expected an integer from 0 to 100.");
                }
                parsed[property.Name] = value;
            }
            return ValidateValues(parsed);
        }
    }

    public static string SavePreset(
        string path, IEnumerable<KeyValuePair<string, int>> values, bool overwrite = false,
        int maxBytes = MaxPresetBytes)
    {
        CheckLimit(maxBytes);
        var document = MakeDocument(values);
        // Utf8JsonWriter indents with Environment.NewLine; the Python tool writes
        // json.dumps(indent=2) with LF, so normalize to keep preset files interchangeable.
        var json = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true })
            .Replace("\r\n", "\n");
        var raw = Encoding.UTF8.GetBytes(json + "\n");
        if (raw.Length > maxBytes)
        {
            throw new PresetError($"Preset exceeds the {maxBytes}-byte limit.");
        }
        var target = new FileInfo(path);
        if (!overwrite && (File.Exists(path) || Directory.Exists(path)))
        {
            throw new IOException($"Preset already exists: {target.FullName}");
        }
        var directory = target.DirectoryName ?? ".";
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, $".{target.Name}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(raw, 0, raw.Length);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target.FullName, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
        return target.FullName;
    }

    private static void CheckLimit(int maxBytes)
    {
        if (maxBytes <= 0)
        {
            throw new PresetError("The preset size limit must be a positive integer.");
        }
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
