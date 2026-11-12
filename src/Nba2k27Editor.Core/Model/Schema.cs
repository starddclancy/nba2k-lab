using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.Core.Model;

public sealed record RecordListItem(string Domain, int Index, long Address, string Label)
{
    public override string ToString() => $"{Domain}[{Index}] {Label} @0x{Address:X}";
}

public sealed record FieldEntry(string Domain, string Section, string Group, int Ordinal, JsonObject Field)
{
    public string NormalizedName => FieldIo.NodeText(Field["normalized_name"]);
    public string DisplayName => FieldIo.NodeText(Field["display_name"]);
}

public sealed record FieldRead(long Address, object RawValue, object DisplayValue, bool Writeable);

/// <summary>Authored layout, record scanning and base pointer resolution for one executable.</summary>
public sealed class Schema
{
    private static readonly Dictionary<string, string[]> LabelFieldNames = new(StringComparer.Ordinal)
    {
        ["Players"] = new[] { "FIRSTNAME", "LASTNAME" },
        ["Teams"] = new[] { "CITYNAME", "TEAMNAME" },
        ["Staff"] = new[] { "FIRSTNAME", "LASTNAME" },
        ["Stadiums"] = new[] { "ARENANAME", "CITYNAME" },
        ["Jerseys"] = new[] { "EDITIONNAME" },
        ["Shoes"] = new[] { "NAME" },
        ["Playbook"] = new[] { "UNLOCALIZEDNAME" },
        ["NBA History"] = new[] { "TEAMCITY", "TEAMNAME", "FIRSTNAME", "LASTNAME", "DATA" },
        ["NBA Records"] = new[] { "FIRSTNAME", "LASTNAME", "DATA" },
    };

    private static readonly Dictionary<string, int> SparseScanInvalidStreaks = new(StringComparer.Ordinal)
    {
        // Era rosters leave six unnamed slots before later active teams and historical blocks.
        ["Teams"] = 12,
        ["NBA Records"] = 12,
        ["Shoes"] = 256,
    };

    private readonly Dictionary<string, JsonObject> _layoutCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyList<FieldEntry>> _entriesCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, FieldEntry>> _lookupCache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, JsonObject>> _payloadCache = new(StringComparer.Ordinal);

    public Schema(IMemory memory, Offsets offsets, string targetExecutable)
    {
        Memory = memory;
        Offsets = offsets;
        TargetExecutable = targetExecutable;
    }

    public IMemory Memory { get; }
    public Offsets Offsets { get; }
    public string TargetExecutable { get; }
    public string LastStatus { get; set; } = "not attached";
    public Dictionary<string, long> RecoveredRoots { get; } = new(StringComparer.Ordinal);
    public (int Pid, long BaseAddress)? RecoveredIdentity { get; set; }

    /// <summary>Optional disk cache for discovered roots; see <see cref="BaseRootCache"/>.</summary>
    public BaseRootCache? RootCache { get; set; }

    public JsonObject ActiveConfig() => Offsets.ActiveConfig();

    public static string DomainBaseKey(string domain)
    {
        if (!Offsets.DomainBaseKeys.TryGetValue(domain, out var key))
        {
            throw new KeyNotFoundException($"unsupported domain: {domain}");
        }
        return key;
    }

    public string DomainStrideKey(string domain)
    {
        var baseKey = DomainBaseKey(domain);
        if (!Offsets.BasePointerSizeKeys.TryGetValue(baseKey, out var strideKey) || strideKey is null)
        {
            throw new KeyNotFoundException($"unsupported domain stride: {domain}");
        }
        return strideKey;
    }

    public int DomainStride(string domain) => StrideValue(DomainStrideKey(domain));

    public int StrideValue(string key) => Offsets.StrideValue(key);

    public JsonObject BasePointerEntry(string key)
    {
        var config = ActiveConfig();
        if (config["base_pointers"] is not JsonObject basePointers || basePointers[key] is not JsonObject entry)
        {
            throw new KeyNotFoundException($"active config is missing {key} base pointer");
        }
        if (TargetExecutable.Equals("NBA2K27.exe", StringComparison.OrdinalIgnoreCase) && RecoveredRoots.Count > 0)
        {
            if (RecoveredIdentity is not { } identity || identity.Pid != Memory.Pid || identity.BaseAddress != Memory.BaseAddress)
            {
                throw new InvalidOperationException("Attachment changed; find the 2K27 bases again");
            }
            return RecoveredEntry(key, basePointers);
        }
        return (JsonObject)entry.DeepClone();
    }

    private JsonObject RecoveredEntry(string key, JsonObject basePointers)
    {
        var entry = (JsonObject)basePointers[key]!.DeepClone();
        if (key is "GameDataStore" or "DraftClass" or "AITuning")
        {
            entry["address"] = RecoveredRoots[key];
        }
        else
        {
            entry["address"] = RecoveredRoots["GameDataStore"];
            var chain = new JsonArray();
            if (basePointers["RosterData"]?["chain"] is JsonArray rosterChain)
            {
                foreach (var step in rosterChain)
                {
                    chain.Add(step?.DeepClone());
                }
            }
            if (key != "RosterData")
            {
                chain.Add(new JsonObject
                {
                    ["offset"] = entry["pointer_offset"]?.DeepClone(),
                    ["dereference"] = true,
                });
            }
            entry["chain"] = chain;
            entry["finalOffset"] = 0;
        }
        entry["absolute"] = false;
        entry["direct_table"] = false;
        long address = ResolveBasePointerEntry(entry, applyFinalOffsetWithoutModuleBase: true,
            followChain: true, stopAtNull: true);
        entry["address"] = address;
        entry["absolute"] = true;
        entry["direct_table"] = true;
        entry["chain"] = new JsonArray();
        entry["finalOffset"] = 0;
        return entry;
    }

    public long ResolveBasePointerEntry(
        JsonObject baseEntry,
        bool applyFinalOffsetWithoutModuleBase = true,
        bool followChain = true,
        bool stopAtNull = false)
    {
        if (baseEntry["address"] is null)
        {
            throw new KeyNotFoundException("base entry is missing address");
        }
        long authoredAddress = Conversions.ToLong(FieldIo.NodeText(baseEntry["address"]));
        long moduleBase = Memory.BaseAddress;
        long finalOffset = Conversions.ToLong(FieldIo.NodeText(baseEntry["finalOffset"]));
        if (moduleBase == 0)
        {
            return authoredAddress + (applyFinalOffsetWithoutModuleBase ? finalOffset : 0);
        }
        bool absolute = IsTrue(baseEntry["absolute"]);
        long pointerAddress = absolute ? authoredAddress : moduleBase + authoredAddress;
        if (IsTrue(baseEntry["direct_table"]))
        {
            return pointerAddress + finalOffset;
        }
        long resolved = FieldIo.ReadPointerValue(Memory, pointerAddress);
        var chainNode = baseEntry["chain"] ?? baseEntry["steps"];
        if (followChain && chainNode is JsonArray chain)
        {
            foreach (var stepNode in chain)
            {
                if (stepNode is not JsonObject step)
                {
                    throw new InvalidOperationException("base chain step must be an object");
                }
                if (stopAtNull && resolved == 0)
                {
                    return 0;
                }
                resolved += Conversions.ToLong(FieldIo.NodeText(step["offset"]));
                if (IsTrue(step["dereference"]))
                {
                    resolved = FieldIo.ReadPointerValue(Memory, resolved);
                }
            }
        }
        if (stopAtNull && resolved == 0)
        {
            return 0;
        }
        return resolved + finalOffset;
    }

    public long BaseAddressForKey(string baseKey) => ResolveBasePointerEntry(
        BasePointerEntry(baseKey), applyFinalOffsetWithoutModuleBase: false, followChain: false);

    public int? RecordCountLimit(string baseKey)
    {
        try
        {
            int count = Conversions.ToInt(FieldIo.NodeText(BasePointerEntry(baseKey)["record_count"]));
            return count > 0 ? count : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public IReadOnlyList<FieldEntry> LayoutEntries(string domain)
    {
        if (_entriesCache.TryGetValue(domain, out var cached))
        {
            return cached;
        }
        var layout = Offsets.EditorLayout(domain);
        var entries = new List<FieldEntry>();
        int ordinal = 0;
        foreach (var sectionPair in layout)
        {
            if (sectionPair.Value is not JsonObject groups)
            {
                continue;
            }
            foreach (var groupPair in groups)
            {
                if (groupPair.Value is not JsonArray fields)
                {
                    continue;
                }
                foreach (var fieldNode in fields)
                {
                    if (fieldNode is not JsonObject field)
                    {
                        continue;
                    }
                    entries.Add(new FieldEntry(domain, sectionPair.Key, groupPair.Key, ordinal++, field));
                }
            }
        }
        _entriesCache[domain] = entries;
        return entries;
    }

    public JsonObject? FieldVersionPayloadOrNull(JsonObject field)
    {
        if (field["versions"] is not JsonObject versions)
        {
            return null;
        }
        return Offsets.TrySelectActiveVersion(versions)?.Payload;
    }

    public JsonObject FieldVersionPayload(JsonObject field)
    {
        return FieldVersionPayloadOrNull(field)
            ?? throw new KeyNotFoundException($"field has no active version for {TargetExecutable}");
    }

    public JsonObject? ParentPayload(string domain, JsonObject payload)
    {
        var parentName = FieldIo.NodeText(payload["parent"]);
        if (parentName.Length == 0)
        {
            return null;
        }
        var parentEntry = FieldByName(domain, parentName)
            ?? throw new KeyNotFoundException($"missing parent field: {parentName}");
        return FieldVersionPayload(parentEntry.Field);
    }

    public Dictionary<string, FieldEntry> FieldLookup(string domain)
    {
        if (_lookupCache.TryGetValue(domain, out var cached))
        {
            return cached;
        }
        var lookup = new Dictionary<string, FieldEntry>(StringComparer.Ordinal);
        foreach (var entry in LayoutEntries(domain))
        {
            if (FieldVersionPayloadOrNull(entry.Field) is null)
            {
                continue;
            }
            foreach (var candidate in new[] { entry.Field["normalized_name"], entry.Field["display_name"] })
            {
                var key = FieldIo.FieldIdentity(candidate is null ? string.Empty : FieldIo.NodeText(candidate));
                if (key.Length > 0 && !lookup.ContainsKey(key))
                {
                    lookup[key] = entry;
                }
            }
        }
        _lookupCache[domain] = lookup;
        return lookup;
    }

    public FieldEntry? FieldByName(string domain, string name)
    {
        var key = FieldIo.FieldIdentity(name);
        return FieldLookup(domain).TryGetValue(key, out var entry) ? entry : null;
    }

    public IReadOnlyList<FieldEntry> GroupedFields(string domain)
    {
        var result = new List<FieldEntry>();
        foreach (var entry in LayoutEntries(domain))
        {
            var payload = FieldVersionPayloadOrNull(entry.Field);
            if (payload is null || IsTrue(payload["hidden"]))
            {
                continue;
            }
            result.Add(entry);
        }
        return result;
    }

    public FieldRead ReadField(FieldEntry entry, RecordListItem item)
    {
        var payload = FieldVersionPayload(entry.Field);
        long address = FieldAddress(entry.Domain, item.Address, payload);
        var raw = FieldIo.ReadAuthoredValue(Memory, address, payload);
        var display = FieldIo.RawToDisplayValue(entry.Section, entry.Field, payload, raw);
        bool writeable = !IsTrue(payload["readonly"]) && FieldIo.ImplementedPayload(payload);
        return new FieldRead(address, raw, display, writeable);
    }

    public FieldRead ReadFieldAt(string domain, string section, JsonObject field, long recordAddress)
    {
        var payload = FieldVersionPayload(field);
        long address = FieldAddress(domain, recordAddress, payload);
        var raw = FieldIo.ReadAuthoredValue(Memory, address, payload);
        var display = FieldIo.RawToDisplayValue(section, field, payload, raw);
        bool writeable = !IsTrue(payload["readonly"]) && FieldIo.ImplementedPayload(payload);
        return new FieldRead(address, raw, display, writeable);
    }

    public long FieldAddress(string domain, long recordAddress, JsonObject payload)
    {
        if (payload.ContainsKey("record_source"))
        {
            return Editing.PlayerStatistics.FieldAddress(this, domain, recordAddress, payload);
        }
        return FieldIo.FieldAddress(Memory, recordAddress, payload, ParentPayload(domain, payload));
    }

    public IReadOnlyList<FieldEntry> LabelEntries(string domain)
    {
        if (!LabelFieldNames.TryGetValue(domain, out var names))
        {
            return Array.Empty<FieldEntry>();
        }
        var entries = new List<FieldEntry>();
        foreach (var name in names)
        {
            var entry = FieldByName(domain, name);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }
        return entries;
    }

    public string LabelForRecordAddress(string domain, long recordAddress, IReadOnlyList<FieldEntry> labelEntries)
    {
        var labels = new List<string>();
        var values = new List<object>();
        foreach (var entry in labelEntries)
        {
            var payload = FieldVersionPayload(entry.Field);
            long address = FieldIo.FieldAddress(Memory, recordAddress, payload, ParentPayload(domain, payload));
            var raw = FieldIo.ReadAuthoredValue(Memory, address, payload);
            var display = FieldIo.RawToDisplayValue(entry.Section, entry.Field, payload, raw);
            values.Add(display);
            var text = DisplayText(display).Trim();
            if (text.Length > 0)
            {
                labels.Add(text);
            }
        }
        if (!ValidLabelValues(domain, recordAddress, values))
        {
            return string.Empty;
        }
        return string.Join(" ", labels);
    }

    public List<RecordListItem> ScanRecords(string domain, int? limit = null)
    {
        if (!Memory.Attached || Memory.BaseAddress == 0)
        {
            throw new InvalidOperationException($"not attached to {TargetExecutable}");
        }
        var baseKey = DomainBaseKey(domain);
        if (!Offsets.BasePointerSizeKeys.TryGetValue(baseKey, out var strideKey) || strideKey is null)
        {
            throw new KeyNotFoundException($"unsupported base stride: {baseKey}");
        }
        int? explicitLimit = limit ?? RecordCountLimit(baseKey) ?? (domain == "Teams" ? 512 : null);
        long baseAddress = BaseAddressForKey(baseKey);
        int stride = StrideValue(strideKey);
        var labelEntries = LabelEntries(domain);
        int invalidStreakStop = SparseScanInvalidStreaks.TryGetValue(domain, out int stop) ? stop : 1;
        int invalidStreak = 0;
        var items = new List<RecordListItem>();
        int index = 0;
        while (explicitLimit is null || index < explicitLimit)
        {
            long address = baseAddress + (long)index * stride;
            string label;
            try
            {
                label = LabelForRecordAddress(domain, address, labelEntries);
            }
            catch (Exception)
            {
                if (items.Count == 0 && index == 0)
                {
                    throw;
                }
                label = string.Empty;
            }
            if (label.Length == 0)
            {
                if (items.Count == 0)
                {
                    break;
                }
                invalidStreak++;
                if (invalidStreak >= invalidStreakStop)
                {
                    break;
                }
                index++;
                continue;
            }
            invalidStreak = 0;
            items.Add(new RecordListItem(domain, index, address, label));
            index++;
        }
        return items;
    }

    /// <summary>Validate the current routes, then scan for them when the authored addresses are stale.</summary>
    public bool RecoverBasePointers()
    {
        if (!TargetExecutable.Equals("NBA2K27.exe", StringComparison.OrdinalIgnoreCase))
        {
            LastStatus = "Base discovery is for NBA2K27. Select that target first.";
            return false;
        }
        if (!Memory.Attached)
        {
            LastStatus = $"not attached to {TargetExecutable}";
            return false;
        }
        var identity = (Memory.Pid, Memory.BaseAddress);
        if (RecoveredIdentity is { } previous
            && (previous.Pid != identity.Pid || previous.BaseAddress != identity.BaseAddress))
        {
            RecoveredRoots.Clear();
            RecoveredIdentity = null;
        }
        // A previous scan of this same process is reusable; validation below still decides
        // whether the cached addresses are actually correct right now.
        if (RecoveredRoots.Count == 0 && RootCache is not null
            && RootCache.TryLoad(identity.Pid, identity.BaseAddress, out Dictionary<string, long> cached))
        {
            foreach (KeyValuePair<string, long> pair in cached)
            {
                RecoveredRoots[pair.Key] = pair.Value;
            }
            RecoveredIdentity = identity;
        }
        if (BasePointersValid())
        {
            LastStatus = "NBA2K27 base pointers are valid; discovery skipped.";
            return true;
        }
        RecoveredRoots.Clear();
        RecoveredIdentity = null;
        var (roots, failures) = BaseScanner.FindBases(Memory, TargetExecutable);
        if (failures.Count > 0)
        {
            LastStatus = "Base discovery failed: " + string.Join("; ", failures.Values);
            return false;
        }
        RecoveredRoots.Clear();
        foreach (var pair in roots)
        {
            RecoveredRoots[pair.Key] = pair.Value;
        }
        RecoveredIdentity = identity;
        RootCache?.Save(identity.Pid, identity.BaseAddress, RecoveredRoots);
        LastStatus = "NBA2K27 base pointers recovered for this attachment.";
        return true;
    }

    public bool BasePointersValid()
    {
        try
        {
            var config = ActiveConfig();
            if (config["base_pointers"] is not JsonObject entries)
            {
                return false;
            }
            var roots = new Dictionary<string, long>(StringComparer.Ordinal);
            if (RecoveredRoots.Count > 0)
            {
                foreach (var pair in RecoveredRoots)
                {
                    roots[pair.Key] = pair.Value;
                }
            }
            else
            {
                foreach (var key in new[] { "GameDataStore", "DraftClass", "AITuning" })
                {
                    roots[key] = Conversions.ToLong(FieldIo.NodeText(entries[key]?["address"]));
                }
            }
            foreach (var pair in entries)
            {
                var entry = (JsonObject)pair.Value!.DeepClone();
                long expected = ExpectedAddress(pair.Key, entry, entries, roots);
                long actual = ResolveBasePointerEntry(BasePointerEntry(pair.Key), stopAtNull: true);
                if (actual != expected || (pair.Key is "GameDataStore" or "RosterData") && actual == 0)
                {
                    return false;
                }
                if (actual != 0 && Memory.ReadBytes(actual, 1).Length != 1)
                {
                    return false;
                }
            }
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private long ExpectedAddress(string key, JsonObject entry, JsonObject basePointers, Dictionary<string, long> roots)
    {
        var saved = new Dictionary<string, long>(RecoveredRoots, StringComparer.Ordinal);
        try
        {
            RecoveredRoots.Clear();
            foreach (var pair in roots)
            {
                RecoveredRoots[pair.Key] = pair.Value;
            }
            return ResolveBasePointerEntry(RecoveredEntry(key, basePointers), stopAtNull: true);
        }
        finally
        {
            RecoveredRoots.Clear();
            foreach (var pair in saved)
            {
                RecoveredRoots[pair.Key] = pair.Value;
            }
        }
    }

    public bool ValidLabelValues(string domain, long recordAddress, IReadOnlyList<object> values)
    {
        if (domain == "NBA History")
        {
            var typeEntry = FieldByName(domain, "TYPE");
            if (typeEntry is null)
            {
                return ValidRecordListLabelValues(values);
            }
            try
            {
                var payload = FieldVersionPayload(typeEntry.Field);
                long address = FieldIo.FieldAddress(
                    Memory, recordAddress, payload, ParentPayload(domain, payload));
                long rawType = Conversions.ToInt(FieldIo.ReadAuthoredValue(Memory, address, payload));
                if (rawType <= 0)
                {
                    return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
            return values.Any(value => DisplayText(value).Any(char.IsLetter));
        }
        return ValidRecordListLabelValues(values);
    }

    private static bool ValidRecordListLabelValues(IReadOnlyList<object> values)
    {
        var parts = values.Where(value => DisplayText(value).Trim().Length > 0).ToList();
        return parts.Count > 0 && parts.All(ValidRecordListLabelPart);
    }

    private static bool ValidRecordListLabelPart(object value)
    {
        var text = DisplayText(value).Trim();
        if (text.Length == 0)
        {
            return false;
        }
        if (!text.Any(character => char.IsLetter(character) || character == '#'))
        {
            return false;
        }
        return text.All(character => !char.IsLetter(character) || IsLatinLetter(character));
    }

    private static bool IsLatinLetter(char character)
    {
        return character is >= '\u0041' and <= '\u005A'
            or >= '\u0061' and <= '\u007A'
            or '\u00AA' or '\u00B5' or '\u00BA'
            or >= '\u00C0' and <= '\u00D6'
            or >= '\u00D8' and <= '\u00F6'
            or >= '\u00F8' and <= '\u02AF'
            or >= '\u1D00' and <= '\u1DBF'
            or >= '\u1E00' and <= '\u1EFF'
            or >= '\u2C60' and <= '\u2C7F'
            or >= '\uA720' and <= '\uA7FF'
            or >= '\uFB00' and <= '\uFB06'
            or >= '\uFF21' and <= '\uFF3A'
            or >= '\uFF41' and <= '\uFF5A';
    }

    public static string DisplayText(object? value)
    {
        return value switch
        {
            null => string.Empty,
            string text => text,
            byte[] bytes => string.Join(" ", bytes.Select(one => one.ToString("x2"))),
            ValueTuple<double, double> pair => $"{pair.Item1}-{pair.Item2}",
            double number => number.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            float single => single.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static bool IsTrue(JsonNode? node)
    {
        return node is JsonValue value && value.GetValueKind() == System.Text.Json.JsonValueKind.True;
    }
}
