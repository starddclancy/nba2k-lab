using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Roster;

public sealed record TeamRoster(int Index, string Label, IReadOnlyList<RecordListItem> Players, IReadOnlySet<long> TwoWayAddresses)
{
    public static TeamRoster Empty(int index, string label) =>
        new(index, label, Array.Empty<RecordListItem>(), new HashSet<long>());
}

public sealed record DraftTableDescriptor(long Root, long Container, long Body, int Count, int Capacity, int Stride)
{
    public (long Body, int Count) Signature => (Body, Count);
}

public sealed class RosterIndex
{
    public IReadOnlyList<TeamRoster> Teams { get; set; } = Array.Empty<TeamRoster>();
    public IReadOnlyList<RecordListItem> DraftItems { get; set; } = Array.Empty<RecordListItem>();
    public (long Body, int Count)? DraftSignature { get; set; }
    public string DraftStatus { get; set; } = "未读取新秀名单";
    public List<string> Warnings { get; } = new();
    public DraftTableDescriptor? DraftDescriptor { get; set; }
}

/// <summary>Team membership and draft class reading, ported from roster_browser.py.</summary>
public static class RosterBrowser
{
    public const int DraftCapacity = 150;
    public const int DraftStride = 2360;
    public const int DraftBodyOffset = 8;
    public const long MaxUserAddress = 0x7FFFFFFFFFFF;

    public sealed record SlotEntry(bool IsTwoWay, int Slot, FieldEntry Entry);

    public static IReadOnlyList<SlotEntry> TeamSlotEntries(Schema model)
    {
        var entries = new List<SlotEntry>();
        foreach (var entry in model.GroupedFields("Teams"))
        {
            if (entry.Section != "Team Players" || entry.Group != "Team Players")
            {
                continue;
            }
            var name = entry.NormalizedName;
            bool twoWay = name.StartsWith("2WAYPLAYER", StringComparison.Ordinal);
            var prefix = twoWay ? "2WAYPLAYER" : "PLAYER";
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }
            var suffix = name[prefix.Length..];
            if (suffix.Length == 0 || !suffix.All(char.IsDigit))
            {
                continue;
            }
            entries.Add(new SlotEntry(twoWay, int.Parse(suffix), entry));
        }
        return entries.OrderBy(row => row.IsTwoWay).ThenBy(row => row.Slot).ToList();
    }

    public static (long MemoryId, int Pid, long Base, long Root) DraftSource(Schema model)
    {
        var memory = model.Memory;
        if (!model.TargetExecutable.Equals("NBA2K27.exe", StringComparison.OrdinalIgnoreCase) || !memory.Attached)
        {
            throw new InvalidOperationException("An attached NBA2K27 reader is required for the draft class");
        }
        var config = model.ActiveConfig();
        if (config["base_pointers"] is not System.Text.Json.Nodes.JsonObject basePointers
            || basePointers["DraftClass"] is not System.Text.Json.Nodes.JsonObject entry
            || config["game_info"] is not System.Text.Json.Nodes.JsonObject info)
        {
            throw new InvalidOperationException("Draft class has an unsupported table layout");
        }
        int? recordCount = AsInt(entry["record_count"]);
        int? classCount = AsInt(info["draftClassCount"]);
        int? classSize = AsInt(info["draftClassSize"]);
        int? bodyOffset = AsInt(info["draftClassBodyOffset"]);
        int? finalOffset = AsInt(entry["finalOffset"]);
        bool layoutOk = recordCount == DraftCapacity && classCount == DraftCapacity
            && classSize == DraftStride && bodyOffset == DraftBodyOffset && finalOffset == DraftBodyOffset;
        bool aliasOk = FieldIo.NodeText(entry["schema_alias"]) == "Players";
        bool flagsClear = !IsTrue(entry["absolute"]) && !IsTrue(entry["direct_table"])
            && entry["chain"] is not System.Text.Json.Nodes.JsonArray { Count: > 0 };
        if (!layoutOk || !aliasOk || !flagsClear)
        {
            throw new InvalidOperationException("Draft class has an unsupported table layout");
        }
        if (model.RecoveredRoots.Count > 0 && model.RecoveredIdentity is { } identity
            && (identity.Pid != memory.Pid || identity.BaseAddress != memory.BaseAddress))
        {
            throw new InvalidOperationException("Draft class reader attachment changed; reconnect");
        }
        long rva = model.RecoveredRoots.Count > 0
            ? model.RecoveredRoots["DraftClass"]
            : Conversions.ToLong(FieldIo.NodeText(entry["address"]));
        if (rva <= 0)
        {
            throw new InvalidOperationException("Draft class has an invalid root");
        }
        long root = memory.BaseAddress + rva;
        if (root % 8 != 0)
        {
            throw new InvalidOperationException("Draft class root is not an aligned pointer slot");
        }
        return (memory.GetHashCode(), memory.Pid, memory.BaseAddress, root);
    }

    public static DraftTableDescriptor ReadDraftDescriptor(Schema model)
    {
        var source = DraftSource(model);
        var memory = model.Memory;
        long root = source.Root;

        DraftTableDescriptor ReadTable()
        {
            long container = (long)DraftRead(memory, root, 8);
            if (container == 0)
            {
                throw new InvalidOperationException("Draft class is not loaded");
            }
            long body = container + DraftBodyOffset;
            if (container % 8 != 0 || container > MaxUserAddress - DraftBodyOffset
                || body > MaxUserAddress - (long)DraftCapacity * DraftStride + 1)
            {
                throw new InvalidOperationException("Draft class table is outside its supported bounds");
            }
            int count = (int)DraftRead(memory, container, 4);
            if (count < 1 || count > DraftCapacity)
            {
                throw new InvalidOperationException(
                    $"Draft class runtime count {count} is outside the supported 1..{DraftCapacity} range");
            }
            return new DraftTableDescriptor(root, container, body, count, DraftCapacity, DraftStride);
        }

        var before = ReadTable();
        long resolvedBody = model.BaseAddressForKey("DraftClass");
        var after = ReadTable();
        if (DraftSource(model) != source || before != after)
        {
            throw new InvalidOperationException("Draft class changed while reading its table descriptor");
        }
        if (resolvedBody != before.Body)
        {
            throw new InvalidOperationException("Draft class body does not match its resolved source");
        }
        return before;
    }

    public static (long Body, int Count) DraftSignature(Schema model) => ReadDraftDescriptor(model).Signature;

    public static RosterIndex BuildRosterIndex(Schema model, IReadOnlyList<RecordListItem> players)
    {
        var result = new RosterIndex();
        var byAddress = new Dictionary<long, RecordListItem>();
        foreach (var player in players)
        {
            byAddress[player.Address] = player;
        }
        try
        {
            var teams = model.ScanRecords("Teams", 512);
            var slots = TeamSlotEntries(model);
            if (slots.Count == 0)
            {
                throw new InvalidOperationException("No team roster slot definitions");
            }
            var buckets = new List<TeamRoster>();
            int failures = 0;
            int unresolved = 0;
            foreach (var team in teams)
            {
                var members = new List<RecordListItem>();
                var seen = new HashSet<long>();
                var twoWay = new HashSet<long>();
                foreach (var slot in slots)
                {
                    long pointer;
                    try
                    {
                        var read = model.ReadField(slot.Entry, team);
                        pointer = Conversions.ToLong(read.RawValue);
                    }
                    catch (Exception)
                    {
                        failures++;
                        continue;
                    }
                    if (pointer == 0)
                    {
                        continue;
                    }
                    if (!byAddress.TryGetValue(pointer, out var player))
                    {
                        unresolved++;
                        continue;
                    }
                    if (slot.IsTwoWay)
                    {
                        twoWay.Add(pointer);
                    }
                    if (seen.Add(pointer))
                    {
                        members.Add(player);
                    }
                }
                buckets.Add(new TeamRoster(team.Index, team.Label, members, twoWay));
            }
            result.Teams = buckets;
            if (teams.Count == 0)
            {
                result.Warnings.Add("未读取到球队，仍可浏览全部球员");
            }
            if (failures > 0)
            {
                result.Warnings.Add($"球队位置读取失败 {failures} 项");
            }
            if (unresolved > 0)
            {
                result.Warnings.Add($"球队引用未匹配到当前球员表 {unresolved} 项");
            }
        }
        catch (Exception exc)
        {
            result.Warnings.Add($"球队浏览不可用：{exc.Message}");
        }
        try
        {
            var source = DraftSource(model);
            var descriptor = ReadDraftDescriptor(model);
            int playerStride = model.DomainStride("Players");
            if (playerStride != 1272)
            {
                throw new InvalidOperationException("The active player table has an unsupported record size");
            }
            long draftEnd = descriptor.Body + (long)descriptor.Count * descriptor.Stride;
            if (byAddress.Keys.Any(address => address < draftEnd && address + playerStride > descriptor.Container))
            {
                throw new InvalidOperationException("Draft class overlaps the active player table");
            }
            var labelEntries = model.LabelEntries("Players");
            if (labelEntries.Count == 0)
            {
                throw new InvalidOperationException("No draft player label definitions");
            }
            var items = new List<RecordListItem>();
            int empty = 0;
            var failures = new List<int>();
            for (int index = 0; index < descriptor.Count; index++)
            {
                long address = descriptor.Body + (long)index * descriptor.Stride;
                string label;
                try
                {
                    label = model.LabelForRecordAddress("Players", address, labelEntries);
                }
                catch (Exception)
                {
                    failures.Add(index);
                    continue;
                }
                if (label.Length == 0)
                {
                    empty++;
                    continue;
                }
                items.Add(new RecordListItem("Players", index, address, label));
            }
            if (ReadDraftDescriptor(model) != descriptor || DraftSource(model) != source)
            {
                throw new InvalidOperationException("Draft class changed while reading");
            }
            if (items.Any(item => byAddress.ContainsKey(item.Address)))
            {
                throw new InvalidOperationException("Draft class overlaps the active player table");
            }
            result.DraftItems = items;
            result.DraftSignature = descriptor.Signature;
            result.DraftDescriptor = descriptor;
            result.DraftStatus = $"新秀名单 · {items.Count} 人 / {descriptor.Count} 槽";
            if (empty > 0)
            {
                result.Warnings.Add($"新秀有效范围内空白或姓名无效 {empty} 槽");
            }
            if (failures.Count > 0)
            {
                result.Warnings.Add($"新秀槽位读取失败 {failures.Count} 项，首个索引 {failures[0]}");
            }
        }
        catch (Exception exc)
        {
            result.DraftStatus = $"新秀名单不可用：{exc.Message}";
        }
        return result;
    }

    private static ulong DraftRead(IMemory memory, long address, int size)
    {
        if (size <= 0 || address <= 0 || address > MaxUserAddress - size + 1)
        {
            throw new InvalidOperationException("Draft class address is outside the supported user address space");
        }
        var raw = memory.ReadBytes(address, size);
        if (raw.Length != size)
        {
            throw new InvalidOperationException("Draft class read was partial");
        }
        ulong value = 0;
        for (int index = raw.Length - 1; index >= 0; index--)
        {
            value = (value << 8) | raw[index];
        }
        return value;
    }

    private static int? AsInt(System.Text.Json.Nodes.JsonNode? node)
    {
        if (node is System.Text.Json.Nodes.JsonValue value)
        {
            if (value.TryGetValue<int>(out var integer))
            {
                return integer;
            }
            if (value.TryGetValue<double>(out var number))
            {
                return (int)number;
            }
        }
        return null;
    }

    private static bool IsTrue(System.Text.Json.Nodes.JsonNode? node)
    {
        return node is System.Text.Json.Nodes.JsonValue value
            && value.GetValueKind() == System.Text.Json.JsonValueKind.True;
    }
}
