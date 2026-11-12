using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Core.Roster;

/// <summary>
/// One DraftData row (32 bytes): current owner team pointer, original owner team pointer,
/// drafted player pointer and packed metadata. Layout verified on 2026-11-08 against drafted
/// players: metadata bits 32..34 are the year slot of a 7-year ring, bit 35 is the round
/// (0 = first), bits 40..47 are the order within the round.
/// </summary>
public sealed record DraftPick(int Index, long Address, long Owner, long Original, long Player, ulong Meta)
{
    public const int Size = 32;
    public const int YearSlots = 7;

    public int YearSlot => (int)((Meta >> 32) & 0x7);
    public int Round => (int)((Meta >> 35) & 0x1) + 1;
    public int Order => (int)((Meta >> 40) & 0xFF) + 1;
    public bool Used => Player != 0;

    public static DraftPick Decode(int index, long address, byte[] row) => new(
        index, address,
        BitConverter.ToInt64(row, 0), BitConverter.ToInt64(row, 8),
        BitConverter.ToInt64(row, 16), BitConverter.ToUInt64(row, 24));

    /// <summary>
    /// Map year slots to calendar years from picks whose drafted player is known. Slots advance
    /// one year each around the ring; returns null when no drafted player anchors the ring.
    /// </summary>
    public static Func<int, int?> Years(IEnumerable<(int Slot, int Year)> anchors)
    {
        var bySlot = new Dictionary<int, int>();
        foreach (var (slot, year) in anchors)
        {
            bySlot[slot] = Math.Max(year, bySlot.GetValueOrDefault(slot));
        }
        if (bySlot.Count == 0)
        {
            return _ => null;
        }
        // Slots holding drafted players keep their own year; the rest are future drafts that
        // follow the most recent draft around the ring.
        var latest = bySlot.MaxBy(pair => pair.Value);
        return value => bySlot.TryGetValue(value, out int year)
            ? year
            : latest.Value + ((value - latest.Key) % YearSlots + YearSlots) % YearSlots;
    }
}

public sealed record DraftPickView(DraftPick Pick, int? Year, string? PlayerLabel)
{
    /// <summary>The low metadata word is all ones on plain picks; other values carry terms
    /// (protections or swaps) that are not decoded yet.</summary>
    public bool HasTerms => (Pick.Meta & 0xFFFFFFFF) != 0xFFFFFFFF;
}

public static class DraftPickTable
{
    /// <summary>Picks with calendar years, anchored on drafted players' YEARDRAFTEDINTERNAL.</summary>
    public static IReadOnlyList<DraftPickView> Describe(Schema model, IReadOnlyList<RecordListItem> players)
    {
        var picks = Read(model).Where(pick => pick.Owner != 0).ToList();
        var byAddress = new Dictionary<long, RecordListItem>();
        foreach (var player in players)
        {
            byAddress.TryAdd(player.Address, player);
        }
        var yearEntry = model.FieldByName("Players", "YEARDRAFTEDINTERNAL");
        var anchors = new List<(int Slot, int Year)>();
        foreach (var pick in picks.Where(pick => pick.Used))
        {
            if (yearEntry is not null && byAddress.TryGetValue(pick.Player, out var player))
            {
                long raw = Conversions.ToLong(model.ReadField(yearEntry, player).RawValue);
                if (raw is > 0 and < 255)
                {
                    anchors.Add((pick.YearSlot, 1900 + (int)raw));
                }
            }
        }
        var years = DraftPick.Years(anchors);
        return picks.Select(pick => new DraftPickView(pick, years(pick.YearSlot),
            pick.Used && byAddress.TryGetValue(pick.Player, out var who) ? who.Label : null)).ToList();
    }

    /// <summary>Read every DraftData row with a known owner team. Read-only.</summary>
    public static IReadOnlyList<DraftPick> Read(Schema model)
    {
        long roster = model.ResolveBasePointerEntry(model.BasePointerEntry("RosterData"), stopAtNull: true);
        var config = model.BasePointerEntry("DraftData");
        if (roster == 0 || config["count_offset"] is null || model.StrideValue("draftDataSize") != DraftPick.Size)
        {
            throw new RosterMoveException("The draft pick table is unavailable");
        }
        int count = BitConverter.ToInt32(model.Memory.ReadBytes(
            roster + Conversions.ToInt(FieldIo.NodeText(config["count_offset"])), 4));
        long table = BitConverter.ToInt64(model.Memory.ReadBytes(
            roster + Conversions.ToInt(FieldIo.NodeText(config["pointer_offset"])), 8));
        if (table == 0 || count < 0 || count > 4000)
        {
            throw new RosterMoveException("The draft pick table address or count is invalid");
        }
        byte[] data = model.Memory.ReadBytes(table, count * DraftPick.Size);
        var picks = new List<DraftPick>(count);
        for (int index = 0; index < count; index++)
        {
            picks.Add(DraftPick.Decode(index, table + index * (long)DraftPick.Size,
                data.AsSpan(index * DraftPick.Size, DraftPick.Size).ToArray()));
        }
        return picks;
    }
}
