using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Editing;

public sealed record HistoryRecordSlot(int Index, string Group, string Metric, int? Rank, string? Notice = null);

/// <summary>Existing record routing from the supplied editor; unknown or conflicting rows keep their raw IDs.</summary>
public static class HistoryRecords
{
    private static readonly string[] Metrics =
    {
        "得分", "投篮命中", "三分命中", "罚球命中", "篮板", "助攻", "盖帽", "抢断", "上场时间", "失误",
        "场均得分", "投篮命中率", "三分命中率", "罚球命中率", "场均篮板", "场均助攻", "场均盖帽", "场均抢断",
        "场均上场时间", "出场数", "犯规", "40+ 得分场次", "50+ 得分场次", "60+ 得分场次", "三双场次",
    };

    private static IEnumerable<HistoryRecordSlot> Group(int start, string group, int metrics, int ranks)
    {
        for (int metric = 0; metric < metrics; metric++)
        {
            for (int rank = 0; rank < ranks; rank++)
            {
                yield return new(start + metric * ranks + rank, group, Metrics[metric], rank + 1);
            }
        }
    }

    public static IReadOnlyList<HistoryRecordSlot> League(int tableCount)
    {
        // The supplied routing reserves [0, 3100) before the first team's 510-row block.
        if (tableCount < 3100)
        {
            throw new InvalidOperationException("联盟纪录表不完整，请重新读取");
        }
        var rows = Group(0, "单场纪录（常规赛）", 10, 5)
            .Concat(Group(50, "单场纪录（季后赛）", 10, 5))
            .Concat(Group(100, "单赛季纪录", 25, 10))
            .Concat(Group(350, "生涯累计纪录", 25, 100)).ToList();
        for (int index = 2850; index < 3100; index++)
        {
            rows.Add(new(index, "分类待核对", "原始记录", null, "此记录未提供分类映射，保留原始编号。"));
        }
        return rows;
    }

    public static IReadOnlyList<HistoryRecordSlot> Team(Schema model, int tableCount, int teamIndex)
    {
        var entry = model.GroupedFields("Teams").SingleOrDefault(field =>
            field.Field["selected_record_source"] is JsonObject source
            && FieldIo.NodeText(source["role"]) == "team_record_start"
            && FieldIo.NodeText(source["target_domain"]) == "NBA Records"
            && source["versions"] is JsonArray versions
            && versions.Any(version => FieldIo.NodeText(version) == model.Offsets.VersionLabel))
            ?? throw new InvalidOperationException("缺少当前版本的球队纪录路由资料");
        var config = entry.Field["selected_record_source"]!;
        int first = config["start_index"]!.GetValue<int>();
        int count = config["row_count"]!.GetValue<int>();
        long start = checked(first + teamIndex * (long)count);
        if (teamIndex < 0 || count != 510 || start < 3100 || start + count > tableCount)
        {
            throw new InvalidOperationException("球队纪录范围超出当前纪录表，已停止读取");
        }
        var rows = Group((int)start, "单场纪录（常规赛）", 10, 5)
            .Concat(Group((int)start + 50, "单场纪录（季后赛）", 10, 5)).ToList();
        // The historical team baseline confirms both ten-row Points tables, but not the remaining metric order.
        for (int offset = 100; offset < count; offset++)
        {
            if (offset < 110 || offset >= 300 && offset < 310)
            {
                rows.Add(new((int)start + offset, offset < 110 ? "单赛季纪录" : "生涯累计纪录",
                    Metrics[0], offset % 10 + 1));
            }
            else
            {
                rows.Add(new((int)start + offset, "分类待核对", "原始记录", null,
                    "此记录的指标顺序尚无直接资料确认，保留原始编号。"));
            }
        }
        return rows;
    }
}
