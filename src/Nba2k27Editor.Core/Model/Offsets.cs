using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Nba2k27Editor.Core.Model;

/// <summary>Authored offset resources plus version selection, mirroring the Python offsets module.</summary>
public sealed class Offsets
{
    public static readonly IReadOnlyDictionary<string, string> HookTargetLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["nba2k22.exe"] = "NBA 2K22",
        ["nba2k23.exe"] = "NBA 2K23",
        ["nba2k24.exe"] = "NBA 2K24",
        ["nba2k25.exe"] = "NBA 2K25",
        ["nba2k26.exe"] = "NBA 2K26",
        ["nba2k27.exe"] = "NBA 2K27",
    };

    public static readonly IReadOnlyDictionary<string, string> DomainBaseKeys = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Players"] = "Player",
        ["Teams"] = "Team",
        ["Staff"] = "Staff",
        ["Stadiums"] = "Stadium",
        ["Jerseys"] = "Jersey",
        ["NBA History"] = "NBAHistory",
        ["NBA Records"] = "Record",
        ["Shoes"] = "Shoes",
        ["Playbook"] = "PlaybookData",
        ["Draft Data"] = "DraftData",
        ["Historic Draft Data"] = "HistoricDraftData",
        ["Matchup Data"] = "MatchupData",
        ["Schedule Data"] = "NBAScheduleData",
        ["AITuning"] = "AITuning",
    };

    public static readonly IReadOnlyDictionary<string, string?> BasePointerSizeKeys = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["Player"] = "playerSize",
        ["DraftClass"] = "draftClassSize",
        ["Team"] = "teamSize",
        ["Staff"] = "staffSize",
        ["Stadium"] = "stadiumSize",
        ["TeamHistory"] = "teamHistorySize",
        ["NBAHistory"] = "historySize",
        ["Record"] = "recordSize",
        ["HallOfFame"] = "hallOfFameSize",
        ["History"] = "historySize",
        ["Jersey"] = "jerseySize",
        ["Shoes"] = "shoeSize",
        ["career_stats"] = "careerStatsSize",
        ["Cursor"] = null,
        ["PlaybookData"] = "playbookSize",
        ["DraftData"] = "draftDataSize",
        ["HistoricDraftData"] = "historicDraftDataSize",
        ["MatchupData"] = "matchupDataSize",
        ["NBAScheduleData"] = "nbaScheduleSize",
        ["AITuning"] = null,
    };

    private static readonly IReadOnlyDictionary<string, string> SuperTypeFiles = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Players"] = "offsets_players.json",
        ["Teams"] = "offsets_teams.json",
        ["Staff"] = "offsets_staff.json",
        ["Stadiums"] = "offsets_stadiums.json",
        ["Jerseys"] = "offsets_jersey.json",
        ["Shoes"] = "offsets_shoes.json",
        ["Playbook"] = "offsets_playbook.json",
        ["NBA History"] = "offsets_history.json",
        ["NBA Records"] = "offsets_history.json",
        ["RosterData"] = "offsets_roster_data.json",
        ["AITuning"] = "offsets_ai_tuning.json",
        ["MyNBA Eras"] = "offsets_my_nba_eras.json",
    };

    private readonly Dictionary<string, JsonObject> _resources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, JsonObject> _layouts = new(StringComparer.Ordinal);

    public Offsets(string targetExecutable)
    {
        TargetExecutable = targetExecutable;
        VersionLabel = DeriveVersionLabel(targetExecutable);
    }

    public string TargetExecutable { get; }
    public string VersionLabel { get; }

    public static string DeriveVersionLabel(string executable)
    {
        var exe = (executable ?? string.Empty).Trim().ToLowerInvariant();
        if (HookTargetLabels.TryGetValue(exe, out var mapped))
        {
            return "2K" + mapped[^2..];
        }
        int index = exe.LastIndexOf("nba2k", StringComparison.Ordinal);
        if (index >= 0 && exe.Length >= index + 7)
        {
            return "2K" + exe.Substring(index + 5, 2);
        }
        throw new ArgumentException($"Cannot derive a version label from '{executable}'", nameof(executable));
    }

    public JsonObject Resource(string fileName)
    {
        if (_resources.TryGetValue(fileName, out var cached))
        {
            return cached;
        }
        var assembly = typeof(Offsets).Assembly;
        string name = $"Nba2k27Editor.Core.Data.{fileName}";
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Embedded offsets resource is missing: {name}");
        var node = JsonNode.Parse(stream) as JsonObject
            ?? throw new InvalidDataException($"Offsets resource {fileName} is not a JSON object");
        _resources[fileName] = node;
        return node;
    }

    public static IReadOnlyList<string> SplitVersionTokens(string? rawKey)
    {
        var text = (rawKey ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return Array.Empty<string>();
        }
        var tokens = new List<string>();
        foreach (var chunk in text.Split(','))
        {
            var token = chunk.Trim().ToUpperInvariant();
            if (token.Length > 0 && !tokens.Contains(token))
            {
                tokens.Add(token);
            }
        }
        return tokens;
    }

    public static bool VersionKeyMatches(string? rawKey, string? targetLabel)
    {
        var target = (targetLabel ?? string.Empty).Trim().ToUpperInvariant();
        if (target.Length == 0)
        {
            return false;
        }
        var tokens = SplitVersionTokens(rawKey);
        if (tokens.Count > 0)
        {
            return tokens.Contains(target);
        }
        return (rawKey ?? string.Empty).Trim().ToUpperInvariant() == target;
    }

    /// <summary>Select the version payload that belongs to the connected executable.</summary>
    public (string Label, string Key, JsonObject Payload) SelectActiveVersion(JsonObject versions)
    {
        foreach (var pair in versions)
        {
            if (VersionKeyMatches(pair.Key, VersionLabel))
            {
                if (pair.Value is not JsonObject payload)
                {
                    throw new InvalidDataException($"version payload for {pair.Key} must be an object");
                }
                return (VersionLabel, pair.Key, payload);
            }
        }
        throw new KeyNotFoundException($"no authored version matches {VersionLabel}");
    }

    public (string Label, string Key, JsonObject Payload)? TrySelectActiveVersion(JsonObject versions)
    {
        try
        {
            return SelectActiveVersion(versions);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    public JsonObject ActiveConfig()
    {
        var league = Resource("offsets_league.json");
        var versions = league["versions"] as JsonObject
            ?? throw new InvalidDataException("offsets_league.json is missing versions");
        var (_, _, versionInfo) = SelectActiveVersion(versions);
        var gameInfo = new JsonObject();
        if (versionInfo["game_info"] is JsonObject baseInfo)
        {
            foreach (var pair in baseInfo)
            {
                gameInfo[pair.Key] = pair.Value?.DeepClone();
            }
        }
        if (versionInfo["stride_constants"] is JsonObject strides)
        {
            foreach (var pair in strides)
            {
                gameInfo[pair.Key] = pair.Value?.DeepClone();
            }
        }
        var config = new JsonObject
        {
            ["game_info"] = gameInfo,
            ["base_pointers"] = versionInfo["base_pointers"]?.DeepClone() ?? new JsonObject(),
            ["super_type_map"] = league["super_type_map"]?.DeepClone() ?? new JsonObject(),
        };
        return config;
    }

    public int StrideValue(string key)
    {
        var gameInfo = ActiveConfig()["game_info"] as JsonObject
            ?? throw new InvalidDataException("active config is missing game_info");
        if (gameInfo[key] is not JsonValue value || !value.TryGetValue<double>(out var stride) || stride <= 0)
        {
            throw new KeyNotFoundException($"game_info is missing {key}");
        }
        return (int)stride;
    }

    /// <summary>Return the authored layout for a super type with dropdown options merged in.</summary>
    public JsonObject EditorLayout(string superType)
    {
        if (_layouts.TryGetValue(superType, out var cached))
        {
            return cached;
        }
        if (!SuperTypeFiles.TryGetValue(superType, out var fileName))
        {
            throw new KeyNotFoundException($"no offsets resource for {superType}");
        }
        var domain = Resource(fileName);
        JsonObject layout;
        if (domain[superType] is JsonObject own)
        {
            layout = (JsonObject)own.DeepClone();
        }
        else if (domain["MyNBA Eras"] is JsonObject eras && eras[superType] is JsonObject eraLayout)
        {
            layout = new JsonObject { [superType] = eraLayout.DeepClone() };
        }
        else
        {
            throw new InvalidDataException($"{fileName} does not contain {superType}");
        }
        MergeDropdownOptions(layout, superType);
        _layouts[superType] = layout;
        return layout;
    }

    private void MergeDropdownOptions(JsonObject layout, string superType)
    {
        var dropdowns = Resource("dropdowns.json");
        if (dropdowns[superType] is not JsonObject dropdownSections)
        {
            return;
        }
        foreach (var sectionPair in dropdownSections)
        {
            if (sectionPair.Value is not JsonObject dropdownGroups
                || layout[sectionPair.Key] is not JsonObject layoutGroups)
            {
                continue;
            }
            foreach (var groupPair in dropdownGroups)
            {
                if (groupPair.Value is not JsonArray dropdownEntries
                    || layoutGroups[groupPair.Key] is not JsonArray layoutEntries)
                {
                    continue;
                }
                var layoutByName = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
                foreach (var entry in layoutEntries)
                {
                    if (entry is JsonObject entryObject && entryObject["normalized_name"] is JsonValue nameValue)
                    {
                        layoutByName[nameValue.ToString()] = entryObject;
                    }
                }
                foreach (var dropdownEntry in dropdownEntries)
                {
                    if (dropdownEntry is not JsonObject dropdownObject
                        || dropdownObject["normalized_name"] is not JsonValue dropdownName)
                    {
                        continue;
                    }
                    if (!layoutByName.TryGetValue(dropdownName.ToString(), out var layoutEntry))
                    {
                        continue;
                    }
                    if (layoutEntry["versions"] is not JsonObject layoutVersions
                        || dropdownObject["versions"] is not JsonObject dropdownVersions)
                    {
                        continue;
                    }
                    foreach (var dropdownVersion in dropdownVersions)
                    {
                        if (dropdownVersion.Value is not JsonObject dropdownPayload)
                        {
                            continue;
                        }
                        var dropdownTokens = SplitVersionTokens(dropdownVersion.Key);
                        if (dropdownTokens.Count == 0)
                        {
                            continue;
                        }
                        foreach (var layoutVersion in layoutVersions)
                        {
                            if (layoutVersion.Value is not JsonObject layoutPayload)
                            {
                                continue;
                            }
                            var layoutTokens = SplitVersionTokens(layoutVersion.Key);
                            if (layoutTokens.Count == 0 || !dropdownTokens.Intersect(layoutTokens).Any())
                            {
                                continue;
                            }
                            foreach (var optionKey in new[] { "dropdown", "values", "id_prefixed_dropdown" })
                            {
                                if (dropdownPayload[optionKey] is { } options)
                                {
                                    layoutPayload[optionKey] = options.DeepClone();
                                }
                            }
                        }
                    }
                }
            }
        }
        if (superType == "Players")
        {
            MergeMovedDropdownOptions(layout, dropdownSections);
        }
    }

    private static void MergeMovedDropdownOptions(JsonObject layout, JsonObject dropdownSections)
    {
        foreach (var section in layout)
        {
            if (section.Value is not JsonObject layoutGroups
                || dropdownSections[section.Key] is not JsonObject dropdownGroups)
            {
                continue;
            }
            var targets = VersionedFields(layoutGroups).ToList();
            var sources = VersionedFields(dropdownGroups).ToList();
            foreach (var target in targets)
            {
                // Keep authored and same-group options. Only moved, unmapped fields need a fallback.
                if (target.Payload["values"] is not null || target.Payload["dropdown"] is not null)
                {
                    continue;
                }
                var tokens = SplitVersionTokens(target.Version);
                if (tokens.Count == 0)
                {
                    continue;
                }
                var candidates = sources.Where(source => source.Group != target.Group
                    && source.Name == target.Name
                    && (source.Payload["values"] is JsonArray || source.Payload["dropdown"] is JsonArray)
                    && SplitVersionTokens(source.Version).Intersect(tokens).Any()).ToList();
                if (candidates.Count != 1)
                {
                    continue;
                }
                // An identical name in two active layout groups is not a safe relocation.
                int targetCount = targets.Count(other => other.Name == target.Name
                    && SplitVersionTokens(other.Version).Intersect(tokens).Any());
                if (targetCount != 1)
                {
                    continue;
                }
                foreach (string key in new[] { "dropdown", "values", "id_prefixed_dropdown" })
                {
                    if (candidates[0].Payload[key] is { } options)
                    {
                        target.Payload[key] = options.DeepClone();
                    }
                }
            }
        }
    }

    private static IEnumerable<(string Group, string Name, string Version, JsonObject Payload)> VersionedFields(
        JsonObject groups)
    {
        foreach (var group in groups)
        {
            if (group.Value is not JsonArray fields)
            {
                continue;
            }
            foreach (var field in fields.OfType<JsonObject>())
            {
                if (field["normalized_name"] is not JsonValue name || field["versions"] is not JsonObject versions)
                {
                    continue;
                }
                foreach (var version in versions)
                {
                    if (version.Value is JsonObject payload)
                    {
                        yield return (group.Key, name.ToString(), version.Key, payload);
                    }
                }
            }
        }
    }
}
