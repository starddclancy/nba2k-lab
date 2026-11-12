using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

public sealed record PlayerSeason(string Selector, int Id, long Address, IReadOnlyList<string> Keys);
public sealed record PlayerStatisticsRead(
    PlayerSnapshot Snapshot, IReadOnlyList<PlayerSeason> Seasons, IReadOnlyList<string> Warnings);

/// <summary>Resolve roster season selectors against the independent, bounded statistics table.</summary>
public static class PlayerStatistics
{
    private static readonly HashSet<string> ReadOnlyNames = new(StringComparer.Ordinal)
    {
        "COMPACTYEAR", "JERSEYNUMBER", "OVERALL", "BOX+-", "POINTSOFFFREETHROW",
        "ALLNBATEAM", "ALLDEFENSETEAM", "ALLROOKIETEAM", "REFERENCECOUNT",
    };

    public static bool IsDirectStatistic(FieldSpec spec) =>
        spec.Entry.Section == "Stats" && spec.Group is "Career High Stats" or "Season High Stats"
        || spec.Entry.Section == "Vitals" && spec.Group == "Awards";

    public static void RegisterSpecs(PlayerSession session)
    {
        var selectors = session.Model.GroupedFields("Players")
            .Where(entry => FieldIo.NodeText(session.Model.FieldVersionPayload(entry.Field)["stat_role"])
                == "season_id_selector").ToArray();
        foreach (var template in session.Model.GroupedFields("Players"))
        {
            var original = session.Model.FieldVersionPayload(template.Field);
            if (FieldIo.NodeText(original["stat_role"]) != "season_id_detail"
                || template.Group == "Internal / Links" && template.NormalizedName != "REFERENCECOUNT")
            {
                continue;
            }
            foreach (var selector in selectors)
            {
                var field = (JsonObject)template.Field.DeepClone();
                var entry = template with { Field = field };
                var payload = session.Model.FieldVersionPayload(field);
                payload["selected_record_source"]!["selector_field"] = selector.NormalizedName;
                if (ReadOnlyNames.Contains(entry.NormalizedName))
                {
                    payload["readonly"] = true;
                }
                string key = $"SeasonStats/{selector.NormalizedName}/{entry.Group}/{entry.NormalizedName}";
                session.Specs[key] = new FieldSpec(key, "SeasonStats", entry.Group, entry, payload);
            }
        }
    }

    public static (long Address, int Count) Table(Schema model)
    {
        long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
        if (roster == 0)
        {
            throw new InvalidOperationException("统计表尚未载入");
        }
        var config = model.BasePointerEntry("PlayerSeasonStats");
        int offset = Conversions.ToInt(FieldIo.NodeText(config["count_offset"]));
        int count = BitConverter.ToInt32(model.Memory.ReadBytes(roster + offset, 4));
        long table = model.ResolveBasePointerEntry(config, stopAtNull: true);
        if (table == 0 || count <= 0 || count > 65535 || model.StrideValue("playerSeasonStatsSize") != 64)
        {
            throw new InvalidOperationException("统计表地址、数量或记录大小无效");
        }
        return (table, count);
    }

    public static long FieldAddress(Schema model, string domain, long player, JsonObject payload)
    {
        if (domain != "Players" || FieldIo.NodeText(payload["record_source"]) != "base_pointer_table"
            || payload["selected_record_source"] is not JsonObject selected
            || FieldIo.NodeText(selected["base_pointer"]) != "PlayerSeasonStats"
            || selected["selector_field"] is null)
        {
            throw new InvalidOperationException("请先选择该球员的赛季统计记录");
        }
        string name = FieldIo.NodeText(selected["selector_field"]);
        var selector = model.FieldByName("Players", name)
            ?? throw new InvalidOperationException("统计编号字段不存在");
        var selectorPayload = model.FieldVersionPayload(selector.Field);
        if (FieldIo.NodeText(selectorPayload["stat_role"]) != "season_id_selector")
        {
            throw new InvalidOperationException("统计编号字段无效");
        }
        int id = Conversions.ToInt(model.ReadFieldAt("Players", selector.Section, selector.Field, player).RawValue);
        var (table, count) = Table(model);
        if (id <= 0 || id == 65535 || id >= count)
        {
            throw new InvalidOperationException($"该球员没有有效的赛季统计记录（编号 {id}，表数量 {count}）");
        }
        int offset = FieldIo.FieldOffset(payload);
        if (offset < 0 || offset > 60 || FieldIo.ResolvedLengthBits(payload) > 32)
        {
            throw new InvalidOperationException("赛季统计字段越界");
        }
        return checked(table + id * 64L + offset);
    }

    public static PlayerStatisticsRead Read(PlayerSession session, RecordListItem item)
    {
        session.Guard();
        var identity = session.Identity(item);
        var keys = session.Specs.Values.Where(IsDirectStatistic).Select(spec => spec.Key).ToList();
        var seasons = new List<PlayerSeason>();
        var warnings = new List<string>();
        var seen = new HashSet<int>();
        foreach (var selector in session.Specs.Values.Where(spec =>
            FieldIo.NodeText(spec.Payload["stat_role"]) == "season_id_selector"))
        {
            try
            {
                int id = Conversions.ToInt(session.Model.ReadField(selector.Entry, item).RawValue);
                if (id is 0 or 65535 || !seen.Add(id))
                {
                    continue;
                }
                var fields = session.Specs.Values.Where(spec => spec.IsSeasonBound
                    && FieldIo.NodeText(spec.Payload["selected_record_source"]?["selector_field"]) == selector.Name)
                    .ToArray();
                long address = session.AddressOf(fields[0], item) - FieldIo.FieldOffset(fields[0].Payload);
                seasons.Add(new PlayerSeason(selector.Name, id, address, fields.Select(spec => spec.Key).ToArray()));
                keys.AddRange(fields.Select(spec => spec.Key));
            }
            catch (Exception exc)
            {
                warnings.Add($"{selector.Name}: {exc.Message}");
            }
        }
        var snapshot = session.ReadPlayer(item, keys);
        // Recheck every selector after all reads so a mixed-season snapshot cannot be applied.
        if (!identity.SequenceEqual(snapshot.Identity))
        {
            throw new InvalidOperationException("读取期间球员已改变，请重新读取");
        }
        foreach (var season in seasons)
        {
            foreach (string key in season.Keys)
            {
                if (snapshot.Values.TryGetValue(key, out var value)
                    && session.AddressOf(session.Specs[key], item) != value.Address)
                {
                    throw new InvalidOperationException("读取期间赛季统计表已改变，请重新读取");
                }
            }
            var first = session.Specs[season.Keys[0]];
            if (session.AddressOf(first, item) - FieldIo.FieldOffset(first.Payload) != season.Address)
            {
                throw new InvalidOperationException("读取期间赛季统计编号已改变，请重新读取");
            }
            string reference = season.Keys.Single(key => session.Specs[key].Name == "REFERENCECOUNT");
            if (!snapshot.Values.TryGetValue(reference, out var references)
                || Conversions.ToInt(references.Raw) != 1)
            {
                warnings.Add($"统计记录 {season.Id} 的引用数不是 1，暂不开放编辑");
                foreach (string key in season.Keys)
                {
                    if (snapshot.Values.TryGetValue(key, out var value))
                    {
                        snapshot.Values[key] = value with { Writable = false };
                    }
                }
            }
        }
        return new PlayerStatisticsRead(snapshot, seasons, warnings);
    }

    public static void ValidateChanges(
        PlayerSession session, PlayerSnapshot snapshot, IReadOnlyDictionary<string, object?> changes)
    {
        foreach (string selector in changes.Keys.Where(key => session.Specs.TryGetValue(key, out var spec)
            && spec.IsSeasonBound).Select(key => FieldIo.NodeText(
                session.Specs[key].Payload["selected_record_source"]?["selector_field"])).Distinct())
        {
            var fields = session.Specs.Values.Where(spec => spec.IsSeasonBound
                && FieldIo.NodeText(spec.Payload["selected_record_source"]?["selector_field"]) == selector)
                .ToDictionary(spec => spec.Name);
            long Value(string name)
            {
                var spec = fields[name];
                if (!snapshot.Values.TryGetValue(spec.Key, out var read))
                {
                    throw new InvalidOperationException("请从数据与荣誉页完整读取该赛季后再修改");
                }
                return Conversions.ToLong(changes.TryGetValue(spec.Key, out var next)
                    ? session.Encode(spec, next) : read.Raw);
            }
            var referenceSpec = fields["REFERENCECOUNT"];
            if (Conversions.ToInt(session.Model.ReadField(referenceSpec.Entry, snapshot.Item).RawValue) != 1)
            {
                throw new InvalidOperationException("该赛季统计记录未独占，禁止修改共享记录");
            }
            foreach (var (made, attempted) in new[]
            {
                ("FIELDGOALSMADE", "FIELDGOALSATTEMPTED"), ("FREETHROWMADE", "FREETHROWATTEMPTED"),
                ("3POINTERSMADE", "3POINTERSATTEMPTED"), ("GAMESSTARTED", "GAMESPLAYED"),
                ("3POINTERSMADE", "FIELDGOALSMADE"), ("3POINTERSATTEMPTED", "FIELDGOALSATTEMPTED"),
            })
            {
                if ((changes.ContainsKey(fields[made].Key) || changes.ContainsKey(fields[attempted].Key))
                    && Value(made) > Value(attempted))
                {
                    throw new ArgumentException($"{fields[made].Label} 不能大于 {fields[attempted].Label}");
                }
            }
        }
    }

    public static void CheckWritableRecord(PlayerSession session, FieldSpec spec, RecordListItem item)
    {
        if (!spec.IsSeasonBound)
        {
            return;
        }
        string selector = FieldIo.NodeText(spec.Payload["selected_record_source"]?["selector_field"]);
        var reference = session.Specs[$"SeasonStats/{selector}/Internal / Links/REFERENCECOUNT"];
        if (Conversions.ToInt(session.Model.ReadField(reference.Entry, item).RawValue) != 1)
        {
            throw new InvalidOperationException("该赛季统计记录未独占，禁止修改共享记录");
        }
    }
}
