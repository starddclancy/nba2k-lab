using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

public sealed record HistoryTable(string Key, long Address, int Count, int Stride);
public sealed record HistoryItem(string Key, string Kind, int Index, int? TeamIndex,
    string Category, int? Year, string Label, string Team, string Summary,
    string? RecordGroup = null, string? Metric = null, int? Rank = null, string? Notice = null);
public sealed record HistoryField(string Key, string Label, int Offset, string Type,
    int Length = 0, bool Writable = true, double Maximum = 65535, int StartBit = 0);
public sealed record HistoryBinding(long Address, byte[] Bytes);
public sealed record HistorySnapshot(
    string Token, HistoryItem Item, HistoryTable Table, long Address, byte[] Bytes,
    IReadOnlyList<HistoryField> Fields, Dictionary<string, object> Values,
    IReadOnlyList<HistoryBinding> Bindings, IReadOnlyList<string> SharedTeams,
    IReadOnlyList<RecordListItem> Teams, SessionEpoch Epoch);
public sealed record HistoryEdit(HistorySnapshot Snapshot, byte[] After, List<PatchRow> Patches,
    string JournalPath, JsonObject Journal);

/// <summary>Existing history records only; each edit retains its table, identity and selector bindings.</summary>
public sealed class HistorySession
{
    public static readonly string[] Categories =
    {
        "", "历届总冠军 / 总决赛 MVP", "总决赛 MVP（独立类型，未验证）", "得分王", "篮板王", "助攻王", "抢断王", "盖帽王", "上场时间",
        "常规赛 MVP", "最佳新秀", "最佳第六人", "最佳防守球员", "进步最快球员", "关键球员",
        "最佳阵容一阵", "最佳阵容二阵", "最佳阵容三阵", "最佳防守一阵", "最佳防守二阵",
        "最佳新秀一阵", "最佳新秀二阵", "最佳教练", "未验证类型 23", "常规赛最佳战绩",
    };

    private readonly PlayerSession _session;
    private readonly Dictionary<string, HistorySnapshot> _reads = new();
    private readonly List<HistoryEdit> _undo = new();
    public HistorySession(PlayerSession session) => _session = session;
    public int UndoDepth => _undo.Count;

    public static bool IsIndependentTeamField(FieldEntry entry, JsonObject payload) =>
        entry.Section is "Team History" or "Team Season Stats" or "Team Stats Edit" or "Team Latest Record"
        || payload.ContainsKey("selected_record_source")
        || FieldIo.NodeText(payload["stat_role"]) == "season_id_selector";

    public static HistoryTable Table(Schema model, string key, int stride)
    {
        long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
        var config = model.BasePointerEntry(key);
        if (roster == 0 || config["count_offset"] is null)
        {
            throw new InvalidOperationException("历史表尚未载入");
        }
        int count = BitConverter.ToInt32(model.Memory.ReadBytes(
            roster + Conversions.ToInt(FieldIo.NodeText(config["count_offset"])), 4));
        long address = model.ResolveBasePointerEntry(config, stopAtNull: true);
        string strideKey = key switch
        {
            "NBAHistory" => "historySize", "TeamStatsData" => "teamStatsSize",
            "TeamHistory" => "teamHistorySize", "Team" => "teamSize", "RetiredJerseyData" => "retiredJerseySize",
            "Record" => "recordSize",
            _ => throw new ArgumentException("未知历史表"),
        };
        if (address == 0 || count < 1 || count > (key == "Record" ? 1000000 : 30000)
            || model.StrideValue(strideKey) != stride)
        {
            throw new InvalidOperationException($"{key} 的地址、数量或记录大小无效");
        }
        return new HistoryTable(key, address, count, stride);
    }

    private HistoryTable TableFor(string kind) => kind switch
    {
        "league" => Table(_session.Model, "NBAHistory", 168),
        "totals" => Table(_session.Model, "Team", 5456),
        "season" => Table(_session.Model, "TeamStatsData", 64),
        "franchise" => Table(_session.Model, "TeamHistory", 364),
        "jersey" => Table(_session.Model, "RetiredJerseyData", 120),
        "records" or "teamRecords" => Table(_session.Model, "Record", 64),
        _ => throw new ArgumentException("未知历史记录类别"),
    };

    /// <summary>
    /// Read-only season lines for one team, newest first: every TeamStatsData record linked from
    /// the team. The year field determines the season; slot order alone is not authoritative.
    /// </summary>
    public IReadOnlyList<IReadOnlyDictionary<string, object?>> TeamSeasons(int teamIndex)
    {
        _session.Guard();
        var table = TableFor("season");
        var team = _session.Model.ScanRecords("Teams").SingleOrDefault(t => t.Index == teamIndex)
            ?? throw new ArgumentException("请先选择球队");
        var links = _session.Memory.ReadBytes(team.Address + 4552, 52);
        var seen = new HashSet<int>();
        var result = new List<IReadOnlyDictionary<string, object?>>();
        for (int slot = 0; slot < 26; slot++)
        {
            int id = BitConverter.ToUInt16(links, slot * 2);
            if (id == 65535 || id >= table.Count || !seen.Add(id))
            {
                continue;
            }
            var row = _session.Memory.ReadBytes(table.Address + id * 64L, 64);
            var line = new Dictionary<string, object?> { ["slot"] = slot, ["id"] = id };
            foreach (var field in Fields("season", row))
            {
                line[field.Key] = field.Type == "byte" ? row[field.Offset] : BitConverter.ToUInt16(row, field.Offset);
            }
            result.Add(line);
        }
        _session.Guard();
        return result;
    }

    public IReadOnlyList<HistoryItem> Catalog(string kind, int? teamIndex = null)
    {
        _session.Guard();
        var table = TableFor(kind);
        var teams = _session.Model.ScanRecords("Teams").ToArray();
        RecordListItem? team = kind is "league" or "records" ? null
            : teams.SingleOrDefault(t => t.Index == teamIndex)
                ?? throw new ArgumentException("请先选择球队");
        var result = new List<HistoryItem>();
        if (kind is "records" or "teamRecords")
        {
            var slots = kind == "records" ? HistoryRecords.League(table.Count)
                : HistoryRecords.Team(_session.Model, table.Count, team!.Index);
            int first = slots[0].Index;
            byte[] bytes = _session.Memory.ReadBytes(table.Address + first * (long)table.Stride,
                checked(slots.Count * table.Stride));
            foreach (var slot in slots)
            {
                int offset = (slot.Index - first) * table.Stride;
                string name = $"{Text(bytes, offset + 12, 20)} {Text(bytes, offset + 32, 20)}".Trim();
                int year = (int)((BitConverter.ToUInt32(bytes, offset + 56) >> 9) & 4095);
                long pointer = BitConverter.ToInt64(bytes, offset);
                string club = teams.FirstOrDefault(t => t.Address == pointer)?.Label ?? "";
                string key = kind == "records" ? $"records:{slot.Index}" : $"teamRecords:{team!.Index}:{slot.Index}";
                result.Add(new HistoryItem(key, kind, slot.Index, team?.Index,
                    slot.Notice is null ? $"{slot.Group} · {slot.Metric}" : slot.Group,
                    year == 0 ? null : year, name.Length == 0 ? "未命名记录" : name, club,
                    BitConverter.ToSingle(bytes, offset + 52).ToString("G", CultureInfo.InvariantCulture),
                    slot.Group, slot.Metric, slot.Rank, slot.Notice));
            }
        }
        else if (kind == "totals")
        {
            result.Add(new HistoryItem($"totals:{team!.Index}", kind, team.Index, team.Index,
                "累计战绩与荣誉", null, team.Label, team.Label, "球队累计"));
        }
        else if (kind == "season")
        {
            var bytes = _session.Memory.ReadBytes(team!.Address + 4552, 52);
            var seen = new HashSet<int>();
            for (int i = 0; i < 26; i++)
            {
                int id = BitConverter.ToUInt16(bytes, i * 2);
                // Record zero is valid in the live team statistics table.
                if (id == 65535 || id >= table.Count || !seen.Add(id))
                {
                    continue;
                }
                var row = _session.Memory.ReadBytes(table.Address + id * 64L, 64);
                int? endYear = row[4] == 0 ? null : 1900 + row[4];
                string label = endYear is int year ? $"{year - 1}-{year} 赛季" : "年份未知";
                result.Add(new HistoryItem($"season:{team.Index}:{id}", kind, id, team.Index,
                    "赛季统计", endYear, label,
                    team.Label, $"{row[5]} 胜 {row[62]} 负 · {BitConverter.ToUInt16(row, 8)} 分"));
            }
        }
        else if (kind == "jersey")
        {
            foreach (var (index, row) in JerseyChain(table, team!))
            {
                int number = BitConverter.ToUInt16(row, 18);
                string jersey = Text(row, 96, 20);
                string name = $"{Text(row, 56, 20)} {Text(row, 76, 20)}".Trim();
                int year = BitConverter.ToUInt16(row, 20);
                result.Add(new HistoryItem($"jersey:{team!.Index}:{index}", kind, index, team.Index, "退役球衣", year,
                    name, team!.Label, number == 65535 ? (jersey.Length > 0 ? jersey : "荣誉") : $"#{number}"));
            }
        }
        else
        {
            uint signature = team is null ? 0 : BitConverter.ToUInt32(_session.Memory.ReadBytes(team.Address + 3864, 4));
            var bytes = _session.Memory.ReadBytes(table.Address, checked(table.Count * table.Stride));
            for (int i = 0; i < table.Count; i++)
            {
                int offset = i * table.Stride;
                if (kind == "league")
                {
                    int type = bytes[offset + 16];
                    if (type < 1 || type >= Categories.Length || type is 2 or 14 or 23)
                    {
                        continue;
                    }
                    int year = 1791 + bytes[offset + 32];
                    string first = Text(bytes, offset + 122, 20);
                    string last = Text(bytes, offset + 142, 20);
                    string name = $"{first} {last}".Trim();
                    string club = $"{Text(bytes, offset + 61, 18)} {Text(bytes, offset + 36, 25)}".Trim();
                    string summary = type is 1 or 24
                        ? $"{BitConverter.ToSingle(bytes, offset + 24):G} : {BitConverter.ToSingle(bytes, offset + 28):G}"
                        : type is >= 3 and <= 8 ? $"{BitConverter.ToSingle(bytes, offset + 24):G}" : name;
                    result.Add(new HistoryItem($"league:{i}", kind, i, null, Categories[type], year,
                        name.Length > 0 ? name : club, club, summary));
                }
                else if (signature != 0 && BitConverter.ToUInt32(bytes, offset) == signature)
                {
                    int start = BitConverter.ToUInt16(bytes, offset + 360);
                    int end = BitConverter.ToUInt16(bytes, offset + 362);
                    string name = $"{WideText(bytes, offset + 54, 25)} {WideText(bytes, offset + 4, 25)}".Trim();
                    result.Add(new HistoryItem($"franchise:{team!.Index}:{i}", kind, i, team.Index,
                        "球队沿革", start, name, team.Label, $"{start}–{end}"));
                }
            }
        }
        _session.Guard();
        if (TableFor(kind) != table)
        {
            throw new InvalidOperationException("历史表已变化，请重新读取");
        }
        return kind is "season" or "jersey" or "records" or "teamRecords" ? result
            : result.OrderByDescending(item => item.Year).ThenBy(item => item.Index).ToArray();
    }

    /// <summary>
    /// Retired jerseys of one team: the list starts at team +3848, each record links to the next at
    /// +0 and back to its team at +8. Only records inside the RetiredJerseyData table are accepted.
    /// </summary>
    private IEnumerable<(int Index, byte[] Row)> JerseyChain(HistoryTable table, RecordListItem team)
    {
        long node = BitConverter.ToInt64(_session.Memory.ReadBytes(team.Address + 3848, 8));
        var seen = new HashSet<long>();
        while (node != 0 && seen.Add(node) && seen.Count <= table.Count)
        {
            long offset = node - table.Address;
            if (offset < 0 || offset % table.Stride != 0 || offset / table.Stride >= table.Count)
            {
                throw new InvalidOperationException("退役球衣链指向表外，已停止读取");
            }
            byte[] row = _session.Memory.ReadBytes(node, table.Stride);
            if (BitConverter.ToInt64(row, 8) != team.Address)
            {
                throw new InvalidOperationException("退役球衣记录不属于该球队，已停止读取");
            }
            yield return ((int)(offset / table.Stride), row);
            node = BitConverter.ToInt64(row, 0);
        }
    }

    public HistorySnapshot Read(string key)
    {
        var parts = key.Split(':');
        string kind = parts[0];
        int? teamIndex = kind is "league" or "records" ? null : parts.Length > 1 ? int.Parse(parts[1]) : null;
        var item = Catalog(kind, teamIndex).SingleOrDefault(item => item.Key == key)
            ?? throw new InvalidOperationException("记录已不属于当前历史表，请刷新");
        var table = TableFor(kind);
        long address = checked(table.Address + item.Index * (long)table.Stride);
        var teams = _session.Model.ScanRecords("Teams").ToArray();
        var bindings = new List<HistoryBinding>();
        var shared = new List<string>();
        var teamTable = TableFor("totals");
        bindings.Add(new HistoryBinding(-1, BitConverter.GetBytes(teamTable.Address)
            .Concat(BitConverter.GetBytes(teamTable.Count)).ToArray()));
        if (kind is "league" or "records" or "teamRecords")
        {
            foreach (var club in teams)
            {
                bindings.Add(new HistoryBinding(club.Address + 762, _session.Memory.ReadBytes(club.Address + 762, 86)));
                bindings.Add(new HistoryBinding(club.Address + 3864, _session.Memory.ReadBytes(club.Address + 3864, 4)));
            }
        }
        if (teamIndex is not null)
        {
            var team = teams.Single(t => t.Index == teamIndex);
            bindings.Add(new HistoryBinding(team.Address + 3864, _session.Memory.ReadBytes(team.Address + 3864, 4)));
            if (kind == "jersey")
            {
                bindings.Add(new HistoryBinding(team.Address + 3848, _session.Memory.ReadBytes(team.Address + 3848, 8)));
                long record = table.Address + item.Index * (long)table.Stride;
                bindings.Add(new HistoryBinding(record, _session.Memory.ReadBytes(record, 16)));
            }
            if (kind == "teamRecords")
            {
                bindings.Add(new HistoryBinding(team.Address + 4624, _session.Memory.ReadBytes(team.Address + 4624, 8)));
            }
            if (kind == "season")
            {
                // Include unnamed team slots when checking ownership.
                for (int i = 0; i < teamTable.Count; i++)
                {
                    long selectorAddress = teamTable.Address + i * 5456L + 4552;
                    byte[] selectors = _session.Memory.ReadBytes(selectorAddress, 52);
                    bindings.Add(new HistoryBinding(selectorAddress, selectors));
                    if (i != teamIndex && Enumerable.Range(0, 26).Any(n =>
                        BitConverter.ToUInt16(selectors, n * 2) == item.Index))
                    {
                        shared.Add($"#{i} {teams.FirstOrDefault(t => t.Index == i)?.Label ?? "未命名球队"}");
                    }
                }
                // The owners' addresses are meaningful only within this team table.
            }
        }
        byte[] bytes = _session.Memory.ReadBytes(address, table.Stride);
        var fields = Fields(kind, bytes).ToArray();
        var values = fields.ToDictionary(field => field.Key, field => ReadValue(bytes, field, teams));
        var snapshot = new HistorySnapshot(Guid.NewGuid().ToString("N"), item, table, address, bytes,
            fields, values, bindings, shared, teams, _session.Epoch!);
        Check(snapshot);
        if (!_session.Memory.ReadBytes(address, bytes.Length).SequenceEqual(bytes))
        {
            throw new InvalidOperationException("历史记录在读取期间变化，请重读");
        }
        if (_reads.Count >= 32)
        {
            _reads.Remove(_reads.Keys.First());
        }
        _reads[snapshot.Token] = snapshot;
        return snapshot;
    }

    public void Check(HistorySnapshot snapshot)
    {
        _session.Guard(snapshot.Epoch);
        if (TableFor(snapshot.Item.Kind) != snapshot.Table)
        {
            throw new InvalidOperationException("历史表已移动或数量变化，请重新读取");
        }
        ValidateBindings(_session.Model, snapshot.Bindings);
    }

    public static void ValidateBindings(Schema model, IReadOnlyList<HistoryBinding> bindings)
    {
        foreach (var binding in bindings)
        {
            if (binding.Address == -1)
            {
                var teams = Table(model, "Team", 5456);
                if (teams.Address != BitConverter.ToInt64(binding.Bytes)
                    || teams.Count != BitConverter.ToInt32(binding.Bytes, 8))
                {
                    throw new InvalidOperationException("球队表已变化");
                }
            }
            else if (!model.Memory.ReadBytes(binding.Address, binding.Bytes.Length).SequenceEqual(binding.Bytes))
            {
                throw new InvalidOperationException("球队签名或赛季关联已变化，请重新读取");
            }
        }
    }

    public HistoryEdit? Apply(string key, string token, IReadOnlyDictionary<string, object?> changes,
        bool acknowledgeShared = false)
    {
        if (!_reads.TryGetValue(token, out var snapshot) || snapshot.Item.Key != key)
        {
            throw new InvalidOperationException("读取凭证已失效，请重新读取该历史记录");
        }
        Check(snapshot);
        if (snapshot.SharedTeams.Count > 0 && !acknowledgeShared)
        {
            throw new InvalidOperationException("此统计被其他球队记录共用，请先确认共享影响");
        }
        if (!_session.Memory.ReadBytes(snapshot.Address, snapshot.Bytes.Length).SequenceEqual(snapshot.Bytes))
        {
            throw new InvalidOperationException("历史记录已在游戏中变化，请重新读取后再应用");
        }
        var overlay = new Overlay(_session.Memory);
        Plan(overlay, snapshot, changes);
        var rows = overlay.Patches().ToList();
        if (rows.Count == 0)
        {
            return null;
        }
        byte[] after = overlay.ReadBytes(snapshot.Address, snapshot.Bytes.Length);
        var directory = Path.Combine(_session.DataDirectory, "history-edits");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
        var journal = new JsonObject
        {
            ["format"] = "nba2k27-history-edit/1", ["status"] = "prepared",
            ["time"] = DateTimeOffset.Now.ToString("O"), ["record"] = key,
            ["label"] = snapshot.Item.Label, ["table"] = snapshot.Table.Key,
            ["address"] = $"0x{snapshot.Address:X}", ["tableAddress"] = $"0x{snapshot.Table.Address:X}",
            ["count"] = snapshot.Table.Count, ["stride"] = snapshot.Table.Stride,
            ["epoch"] = JsonSerializer.SerializeToNode(snapshot.Epoch),
            ["beforeBytes"] = Convert.ToHexString(snapshot.Bytes), ["afterBytes"] = Convert.ToHexString(after),
            ["sharedTeams"] = JsonSerializer.SerializeToNode(snapshot.SharedTeams),
            ["fields"] = new JsonArray(changes.Keys.Select(name => (JsonNode?)new JsonObject
            {
                ["key"] = name, ["before"] = JsonSerializer.SerializeToNode(snapshot.Values[name]),
                ["after"] = JsonSerializer.SerializeToNode(ReadValue(after,
                    snapshot.Fields.Single(f => f.Key == name), snapshot.Teams)),
            }).ToArray()),
            ["bytes"] = JsonSerializer.SerializeToNode(rows),
        };
        var edit = new HistoryEdit(snapshot, after, rows, path, journal);
        Save(edit);
        try
        {
            ApplyRecord(_session.Memory, snapshot.Address, after, rows, () => CheckEdit(edit));
        }
        catch (Exception exc)
        {
            journal["status"] = exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal)
                ? "needs_review" : "failed";
            journal["error"] = exc.Message;
            Save(edit);
            throw;
        }
        _undo.Add(edit);
        _reads.Clear();
        journal["status"] = "applied";
        try { Save(edit); }
        catch (IOException exc) { journal["warning"] = "写入已生效，日志状态更新失败：" + exc.Message; }
        return edit;
    }

    private void CheckEdit(HistoryEdit edit)
    {
        Check(edit.Snapshot);
        CheckUnchangedBytes(_session.Memory, edit.Snapshot.Address, edit.Snapshot.Bytes, edit.Patches);
    }

    public static void CheckUnchangedBytes(IMemory memory, long address, byte[] before, IReadOnlyList<PatchRow> rows)
    {
        var current = memory.ReadBytes(address, before.Length);
        var changed = rows.Select(row => row.Address - address).ToHashSet();
        for (int i = 0; i < before.Length; i++)
        {
            if (!changed.Contains(i) && current[i] != before[i])
            {
                throw new InvalidOperationException("历史记录的未编辑字节已变化，操作已停止");
            }
        }
    }

    public static void ApplyRecord(IMemory memory, long address, byte[] expected,
        IReadOnlyList<PatchRow> rows, Action guard)
    {
        PatchApplier.ApplyPatches(memory, rows, guard);
        try
        {
            guard();
            if (!memory.ReadBytes(address, expected.Length).SequenceEqual(expected))
            {
                throw new InvalidOperationException("历史整条记录读回与计划不一致");
            }
        }
        catch (Exception exc)
        {
            var unresolved = PatchApplier.RestorePatches(memory, rows, guard);
            string suffix = unresolved.Count == 0 ? "; completed writes were rolled back"
                : "; rollback incomplete: " + string.Join("; ", unresolved.Select(row => $"0x{row.Address:X}: {row.Error}"));
            throw new InvalidOperationException(exc.Message + suffix, exc);
        }
    }

    public HistoryEdit Undo(string key)
    {
        if (_undo.Count == 0 || _undo[^1].Snapshot.Item.Key != key)
        {
            throw new InvalidOperationException("请回到最近修改的历史记录再撤销");
        }
        var edit = _undo[^1];
        Check(edit.Snapshot);
        if (!_session.Memory.ReadBytes(edit.Snapshot.Address, edit.After.Length).SequenceEqual(edit.After))
        {
            throw new InvalidOperationException("历史记录已被游戏更改，不能覆盖撤销，请检查日志");
        }
        var reverse = edit.Patches.Select(row => new PatchRow(row.Address, row.After, row.Before, row.Mask)).ToArray();
        try
        {
            ApplyRecord(_session.Memory, edit.Snapshot.Address, edit.Snapshot.Bytes, reverse, () => CheckEdit(edit));
        }
        catch (Exception exc)
        {
            edit.Journal["error"] = exc.Message;
            if (exc.Message.Contains("rollback incomplete:", StringComparison.Ordinal))
            {
                edit.Journal["status"] = "needs_review";
            }
            Save(edit);
            throw;
        }
        edit.Journal["status"] = "undone";
        _undo.RemoveAt(_undo.Count - 1);
        _reads.Clear();
        try { Save(edit); }
        catch (IOException exc) { edit.Journal["warning"] = "撤销已完成，日志状态更新失败：" + exc.Message; }
        return edit;
    }

    private static void Save(HistoryEdit edit)
    {
        string temp = edit.JournalPath + ".tmp";
        File.WriteAllText(temp, edit.Journal.ToJsonString(new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
        }));
        File.Move(temp, edit.JournalPath, true);
    }

    public static void Plan(Overlay overlay, HistorySnapshot snapshot, IReadOnlyDictionary<string, object?> changes)
    {
        foreach (var (key, value) in changes)
        {
            if (value is null)
            {
                throw new ArgumentException($"历史字段缺少值：{key}");
            }
            var field = snapshot.Fields.SingleOrDefault(f => f.Key == key)
                ?? throw new ArgumentException($"未知历史字段：{key}");
            if (!field.Writable)
            {
                throw new ArgumentException($"{field.Label} 暂不开放编辑");
            }
            if (field.Type == "team")
            {
                var team = snapshot.Teams.SingleOrDefault(t => TeamOption(t) == Convert.ToString(value, CultureInfo.InvariantCulture))
                    ?? throw new ArgumentException("请选择实际存在的球队");
                long pointer = snapshot.Address + field.Offset;
                overlay.WriteBytes(pointer, BitConverter.GetBytes(team.Address));
                if (snapshot.Item.Kind != "league")
                {
                    // Team records hold only the pointer; the name cache below is a league-record layout.
                    continue;
                }
                // Update the display cache in the same transaction as the team pointer.
                string city = overlay.ReadWString(team.Address + 812, 18);
                string name = overlay.ReadWString(team.Address + 762, 25);
                WriteText(overlay, snapshot.Address + (field.Offset == 0 ? 36 : 79), name, 25, false);
                WriteText(overlay, snapshot.Address + (field.Offset == 0 ? 61 : 104), city, 18, false);
                continue;
            }
            long address = snapshot.Address + field.Offset;
            if (field.Type is "string" or "wstring")
            {
                WriteText(overlay, address, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
                    field.Length, field.Type == "wstring");
            }
            else
            {
                if (value is bool)
                {
                    throw new ArgumentException("统计数值不能是布尔值");
                }
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                ValidateNumber(field, number);
                byte[] bytes = field.Type switch
                {
                    "byte" => new[] { (byte)number },
                    "ushort" => BitConverter.GetBytes((ushort)number),
                    "uint" => BitConverter.GetBytes((uint)number),
                    "float" => BitConverter.GetBytes((float)number),
                    _ => throw new ArgumentException("未支持的历史字段类型"),
                };
                overlay.WriteBytes(address, bytes);
            }
        }
        if (overlay.TouchedAddresses.Any(address => address < snapshot.Address
            || address >= snapshot.Address + snapshot.Bytes.Length))
        {
            throw new InvalidOperationException("历史写入超出记录边界");
        }
        var after = overlay.ReadBytes(snapshot.Address, snapshot.Bytes.Length);
        if (snapshot.Item.Kind == "franchise"
            && BitConverter.ToUInt16(after, 360) > BitConverter.ToUInt16(after, 362))
        {
            throw new ArgumentException("开始年份不能晚于结束年份");
        }
        if (snapshot.Item.Kind == "jersey")
        {
            for (int slot = 0; slot < 8; slot++)
            {
                int start = BitConverter.ToUInt16(after, 24 + slot * 4);
                int end = BitConverter.ToUInt16(after, 26 + slot * 4);
                if (start > end)
                {
                    throw new ArgumentException($"第 {slot + 1} 段效力的开始年份不能晚于结束年份");
                }
            }
        }
        if (snapshot.Item.Kind == "season")
        {
            foreach (var (made, attempted) in new[] { (12, 14), (16, 18), (20, 22), (46, 48), (50, 52), (16, 12), (18, 14) })
            {
                if (changes.Keys.Any(key => snapshot.Fields.Any(f => f.Key == key && (f.Offset == made || f.Offset == attempted)))
                    && BitConverter.ToUInt16(after, made) > BitConverter.ToUInt16(after, attempted))
                {
                    throw new ArgumentException("命中数不能大于出手数，三分不能大于投篮总计");
                }
            }
        }
    }

    public static void ValidateNumber(HistoryField field, double value)
    {
        if (!double.IsFinite(value) || value < 0 || value > field.Maximum
            || field.Type != "float" && value != Math.Truncate(value)
            || field.Key is "SCORE1" or "SCORE2" or "POSITION" or "WINS" or "LOSSES" && value != Math.Truncate(value))
        {
            throw new ArgumentException($"{field.Label} 数值无效，范围为 0–{field.Maximum}");
        }
    }

    private static void WriteText(Overlay overlay, long address, string text, int length, bool wide)
    {
        if (text.Length >= length || text.Any(char.IsControl) || !wide && text.Any(c => c > 255))
        {
            throw new ArgumentException($"文本最多 {length - 1} 个字符，窄字符字段不支持中文");
        }
        if (wide) { overlay.WriteWStringFixed(address, text, length); }
        else { overlay.WriteAsciiFixed(address, text, length); }
    }

    public static string TeamOption(RecordListItem team) => $"{team.Index} · {team.Label}";
    public static object ReadValue(byte[] bytes, HistoryField field, IReadOnlyList<RecordListItem> teams) =>
        field.Type switch
        {
            "string" => Text(bytes, field.Offset, field.Length),
            "wstring" => WideText(bytes, field.Offset, field.Length),
            "byte" => (int)bytes[field.Offset],
            "ushort" => (int)BitConverter.ToUInt16(bytes, field.Offset),
            "uint" => (long)BitConverter.ToUInt32(bytes, field.Offset),
            "float" => (double)BitConverter.ToSingle(bytes, field.Offset),
            "bits" => (int)((BitConverter.ToUInt32(bytes, field.Offset) >> field.StartBit)
                & ((1U << field.Length) - 1)),
            "team" => teams.FirstOrDefault(t => t.Address == BitConverter.ToInt64(bytes, field.Offset)) is { } team
                ? TeamOption(team) : "保留现有关联",
            _ => throw new ArgumentException("未知历史字段类型"),
        };

    private static string Text(byte[] bytes, int offset, int length) =>
        System.Text.Encoding.Latin1.GetString(bytes, offset, length).Split('\0')[0];
    private static string WideText(byte[] bytes, int offset, int length) =>
        System.Text.Encoding.Unicode.GetString(bytes, offset, length * 2).Split('\0')[0];

    public static IEnumerable<HistoryField> Fields(string kind, byte[] bytes)
    {
        if (kind is "records" or "teamRecords")
        {
            yield return new("TEAM", "所属球队", 0, "team");
            yield return new("SIGNATUREID", "球员签名 ID", 8, "uint", Writable: false);
            yield return new("FIRSTNAME", "纪录保持者名", 12, "string", 20);
            yield return new("LASTNAME", "纪录保持者姓", 32, "string", 20);
            yield return new("DATA", "纪录数值", 52, "float", Maximum: float.MaxValue);
            yield return new("DAY", "日编码", 56, "bits", 5, false, 31);
            yield return new("MONTH", "月编码", 56, "bits", 4, false, 15, 5);
            yield return new("YEAR", "年份", 56, "bits", 12, false, 4095, 9);
        }
        else if (kind == "league")
        {
            int type = bytes[16];
            yield return new("TYPE", "分类码", 16, "byte", Writable: false);
            yield return new("SEASON", "赛季", 32, "byte", Writable: false);
            yield return new("TEAM", type == 1 ? "冠军球队" : "所属球队", 0, "team");
            yield return new("TEAMCITY", "球队城市", 61, "string", 18, false);
            yield return new("TEAMNAME", "球队名称", 36, "string", 25, false);
            if (type != 24)
            {
                yield return new("FIRSTNAME", type == 1 ? "总决赛 MVP 名" : "获奖者名", 122, "string", 20);
                yield return new("LASTNAME", type == 1 ? "总决赛 MVP 姓" : "获奖者姓", 142, "string", 20);
            }
            if (type == 1)
            {
                yield return new("OPPONENT", "亚军球队", 8, "team");
                yield return new("LOSERTEAMCITY", "亚军城市", 104, "string", 18, false);
                yield return new("LOSERTEAMNAME", "亚军名称", 79, "string", 25, false);
                yield return new("SCORE1", "冠军系列赛胜场", 24, "float", Maximum: 7);
                yield return new("SCORE2", "亚军系列赛胜场", 28, "float", Maximum: 7);
            }
            else if (type == 24)
            {
                yield return new("WINS", "胜场", 24, "float", Maximum: 255);
                yield return new("LOSSES", "负场", 28, "float", Maximum: 255);
            }
            else if (type is >= 3 and <= 8)
            {
                yield return new("DATA", "场均数值", 24, "float", Maximum: 1000);
            }
            else if (type is >= 15 and <= 21)
            {
                // POSITION uses the same float slot as DATA / RESULT.
                yield return new("POSITION", "位置编码", 24, "float", Writable: false, Maximum: 5);
            }
        }
        else if (kind == "totals")
        {
            yield return new("CHAMPIONSHIPS", "总冠军次数", 3887, "byte", Maximum: 255);
            yield return new("CONFERENCECHAMPIONSHIPS", "分区冠军次数", 3883, "byte", Maximum: 255);
            yield return new("DIVISIONTITLES", "赛区冠军次数", 3886, "byte", Maximum: 255);
            yield return new("PLAYOFFAPPEARANCES", "季后赛次数", 3882, "byte", Maximum: 255);
            yield return new("HISTORICWIN", "历史胜场", 4632, "ushort");
            yield return new("HISTORICLOSSES", "历史负场", 4634, "ushort");
            yield return new("FIRSTPICKSCOUNT", "状元签次数", 4902, "ushort");
            yield return new("LOTTERYPICKSCOUNT", "乐透签次数", 4904, "ushort");
            // The live game stores these as full calendar years (Celtics LASTPLAYOFFYEAR = 2008).
            yield return new("LASTPLAYOFFYEAR", "最近一次进季后赛年份", 3880, "ushort", Maximum: 4095);
            yield return new("LASTLOTTERYYEAR", "最近一次乐透年份", 4898, "ushort", Maximum: 4095);
            yield return new("LASTNONLOTTERYYEAR", "最近一次未进乐透年份", 4900, "ushort", Maximum: 4095);
            yield return new("LASTYEARPLAYOFFSOPPONENT", "上季季后赛最后对手", 4640, "team");
        }
        else if (kind == "franchise")
        {
            yield return new("SIGNATURE", "球队关联签名", 0, "uint", Writable: false);
            yield return new("CITYNAME", "历史城市", 54, "wstring", 25);
            yield return new("TEAMNAME", "历史队名", 4, "wstring", 25);
            yield return new("STARTYEAR", "开始年份", 360, "ushort", Maximum: 4095);
            yield return new("ENDYEAR", "结束年份", 362, "ushort", Maximum: 4095);
            yield return new("FILENAME", "历史图标资源", 104, "string", 256, false);
        }
        else if (kind == "jersey")
        {
            yield return new("JERSEYNUMBER", "球衣号码（65535 = 荣誉/非号码）", 18, "ushort");
            yield return new("JERSEYSTRING", "号码文字（非号码荣誉）", 96, "string", 20);
            yield return new("UNLOCALIZEDFIRSTNAME", "名", 56, "string", 20);
            yield return new("UNLOCALIZEDLASTNAME", "姓", 76, "string", 20);
            yield return new("RETIREDYEAR", "退役年份", 20, "ushort", Maximum: 4095);
            // NUMTENURES packs into bits 16-19 of +20; only the tenures in use are offered.
            int tenures = bytes[22] & 15;
            for (int slot = 0; slot < Math.Max(1, Math.Min(8, tenures)); slot++)
            {
                yield return new($"START{slot + 1}", $"第 {slot + 1} 段开始年份", 24 + slot * 4, "ushort", Maximum: 4095);
                yield return new($"END{slot + 1}", $"第 {slot + 1} 段结束年份", 26 + slot * 4, "ushort", Maximum: 4095);
            }
        }
        else if (kind == "season")
        {
            yield return new("COMPACTYEAR", "赛季", 4, "byte", Writable: false);
            yield return new("WINS", "胜场", 5, "byte", Maximum: 255);
            yield return new("LOSSES", "负场", 62, "byte", Maximum: 255);
            var names = new[]
            {
                ("MINUTESPLAYED", "上场分钟"), ("TOTALPOINTS", "总得分"), ("TOTALPOINTSALLOWED", "总失分"),
                ("FIELDGOALSMADE", "投篮命中"), ("FIELDGOALSATTEMPTED", "投篮出手"),
                ("THREEPOINTSHOTSMADE", "三分命中"), ("THREEPOINTSHOTSATTEMPTED", "三分出手"),
                ("FREETHROWMADE", "罚球命中"), ("FREETHROWATTEMPTED", "罚球出手"),
                ("OFFENSIVEREBOUNDS", "进攻篮板"), ("DEFENSIVEREBOUNDS", "防守篮板"), ("STEALS", "抢断"),
                ("TURNOVERS", "失误"), ("BLOCKS", "盖帽"), ("ASSISTS", "助攻"), ("FOULS", "犯规"),
                ("POINTSOFFTURNOVERS", "利用失误得分"), ("SECONDCHANCEPOINTS", "二次进攻得分"),
                ("POINTSINPAINT", "内线得分"), ("OPPONENTFIELDGOALSMADE", "对手投篮命中"),
                ("OPPONENTFIELDGOALSATTEMPTED", "对手投篮出手"), ("OPPONENTTHREEPOINTSHOTSMADE", "对手三分命中"),
                ("OPPONENTTHREEPOINTSHOTSATTEMPTED", "对手三分出手"), ("TURNOVERSFORCED", "制造失误"),
                ("BENCHPOINTS", "替补得分"), ("FASTBREAKPOINTS", "快攻得分"), ("POSSESSIONS", "回合数"),
            };
            for (int i = 0; i < names.Length; i++)
            {
                int offset = 6 + i * 2 + (i >= 17 ? 2 : 0);
                yield return new(names[i].Item1, names[i].Item2, offset, "ushort");
            }
        }
    }
}
