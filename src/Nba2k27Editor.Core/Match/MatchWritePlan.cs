using System.Buffers.Binary;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.Core.Match;

public sealed record MatchCounterPatch(int Side, int? PlayerId, int Slot, string Field,
    long Address, int Width, string BeforeHex, string AfterHex);
public sealed record MatchWritePlan(MatchEditPlan Preview, List<MatchCounterPatch> Patches);

/// <summary>Compile numeric spans only. This class has no process access or write capability.</summary>
public static class MatchWritePlanner
{
    public static MatchWritePlan Prepare(MatchSnapshot snapshot, IReadOnlyList<MatchEditRequest> requests)
    {
        var preview = MatchEditPlanner.Prepare(snapshot, requests);
        var windows = snapshot.Teams.SelectMany(team => new[] { team.StatsAddress }
            .Concat(team.Players.Select(player => player.StatsAddress))).Order().ToArray();
        for (int index = 0; index < windows.Length; index++)
        {
            long address = windows[index];
            Require(address >= 0x10000 && address % 8 == 0
                && address <= NativeGsConstants.MaxAddress - MatchReader.StatSize + 1,
                "Invalid match statistics window");
            Require(index == 0 || windows[index - 1] + MatchReader.StatSize <= address,
                "Match statistics windows overlap");
        }

        var patches = new List<MatchCounterPatch>();
        foreach (var change in preview.Changes)
        {
            // Rebounds are computed from the two physical rebound counters.
            if (change.Field == "rebounds")
            {
                continue;
            }
            var team = snapshot.Teams.Single(team => team.Side == change.Side);
            long start = change.PlayerId is int playerId
                ? team.Players.Single(player => player.Id == playerId).StatsAddress : team.StatsAddress;
            (int offset, int width) = Field(change.Slot, change.Field);
            Require(offset >= 0 && offset + width <= MatchReader.StatSize,
                "Counter lies outside the verified statistics window");
            long address = checked(start + offset);
            Require(address % width == 0, "Unaligned match counter");
            patches.Add(new(change.Side, change.PlayerId, change.Slot, change.Field, address, width,
                Encode(change.Before, width), Encode(change.After, width)));
        }
        var ordered = patches.OrderBy(patch => patch.Address).ToList();
        for (int index = 1; index < ordered.Count; index++)
        {
            Require(ordered[index - 1].Address + ordered[index - 1].Width <= ordered[index].Address,
                "Compiled match counter spans overlap");
        }
        return new(preview, ordered);
    }

    private static (int Offset, int Width) Field(int slot, string field) => field switch
    {
        "points" => (slot * 0xC8, 4),
        "twoPointMade" => (slot * 0xC8 + 4, 2),
        "threePointMade" => (slot * 0xC8 + 6, 2),
        "freeThrowMade" => (slot * 0xC8 + 0xA, 2),
        "twoPointAttempted" => (slot * 0xC8 + 0x12, 2),
        "threePointAttempted" => (slot * 0xC8 + 0x14, 2),
        "freeThrowAttempted" => (slot * 0xC8 + 0x18, 2),
        "offensiveRebounds" => (0x4B0 + slot * 4, 2),
        "defensiveRebounds" => (0x4B2 + slot * 4, 2),
        "steals" => (0x4C8 + slot * 0x24, 2),
        "blocks" => (0x4CA + slot * 0x24, 2),
        "turnovers" => (0x4CC + slot * 0x24, 2),
        "assists" => (0x4D0 + slot * 0x24, 2),
        _ => throw new InvalidOperationException("Unverified match counter field"),
    };

    private static string Encode(long value, int width)
    {
        var bytes = new byte[width];
        if (width == 4)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, checked((uint)value));
        }
        else
        {
            BinaryPrimitives.WriteUInt16LittleEndian(bytes, checked((ushort)value));
        }
        return Convert.ToHexString(bytes);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
