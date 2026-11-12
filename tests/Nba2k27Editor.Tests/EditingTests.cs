using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Roster;

namespace Nba2k27Editor.Tests;

/// <summary>Offline checks for the patch overlay used by every planned write.</summary>
internal static class OverlayTests
{
    public static void Run(Harness harness)
    {
        Records(harness);
        ReadsSeePendingWrites(harness);
        NoRealWrites(harness);
        FullWidthWrite(harness);
    }

    private static void Records(Harness harness)
    {
        var memory = new SimulatedMemory(0x100);
        memory.WriteBytes(0x10, new byte[] { 0x11, 0x22, 0x33 });
        var overlay = new Overlay(memory);
        overlay.WriteBytes(0x10, new byte[] { 0x11, 0x99, 0x33 });

        var patches = overlay.Patches();
        harness.Equal("overlay: only changed bytes are reported", 1, patches.Count);
        harness.Equal("overlay: patch address", 0x11L, patches[0].Address);
        harness.Equal("overlay: patch before", (byte)0x22, patches[0].Before);
        harness.Equal("overlay: patch after", (byte)0x99, patches[0].After);
        harness.Equal("overlay: patch mask is the xor", (byte)0xBB, patches[0].Mask);

        // Writing the original value back must leave no patch at all.
        overlay.WriteBytes(0x11, new byte[] { 0x22 });
        harness.Equal("overlay: restored byte drops the patch", 0, overlay.Patches().Count);
    }

    private static void ReadsSeePendingWrites(Harness harness)
    {
        var memory = new SimulatedMemory(0x100);
        memory.WriteBytes(0x20, new byte[] { 1, 2, 3, 4 });
        var overlay = new Overlay(memory);
        overlay.WriteBytes(0x21, new byte[] { 0xFE });

        // A later read through the overlay must see the planned byte, not the stored one.
        harness.Sequence("overlay: reads see planned bytes", new byte[] { 1, 0xFE, 3, 4 },
            overlay.ReadBytes(0x20, 4));
        harness.Sequence("overlay: the store itself is untouched", new byte[] { 1, 2, 3, 4 },
            memory.ReadBytes(0x20, 4));
    }

    private static void NoRealWrites(Harness harness)
    {
        var memory = new SimulatedMemory(0x100);
        var overlay = new Overlay(memory);
        overlay.WriteUint32(0x40, 0xDEADBEEF);
        overlay.WriteBytes(0x44, new byte[] { 1, 2, 3, 4 });
        harness.Equal("overlay: no write reaches the store", 0, memory.Writes);
        harness.Equal("overlay: touched addresses counted", 8, overlay.TouchedAddresses.Count);
    }

    private static void FullWidthWrite(Harness harness)
    {
        // A 64-bit field must keep all eight bytes in the patch list, including the high half.
        var memory = new SimulatedMemory(0x100);
        var overlay = new Overlay(memory);
        long value = 0x0102030405060708L;
        overlay.WriteBytes(0x60, BitConverter.GetBytes(value));
        var patches = overlay.Patches();
        harness.Equal("overlay: 64-bit write reports eight bytes", 8, patches.Count);
        harness.Equal("overlay: highest byte included", 0x67L, patches[^1].Address);
        harness.Equal("overlay: high byte value", (byte)0x01, patches[^1].After);
        // Bytes that already matched must not be reported, so the patch list stays minimal.
        var partial = new Overlay(memory);
        partial.WriteBytes(0x60, BitConverter.GetBytes(0x21C12F52948L));
        harness.Equal("overlay: unchanged zero bytes are skipped", 6, partial.Patches().Count);
    }
}

/// <summary>Offline checks for the roster slot arithmetic shared by move and trade.</summary>
internal static class RosterSlotTests
{
    public static void Run(Harness harness)
    {
        Compaction(harness);
        Append(harness);
        RoundTrip(harness);
        Rejections(harness);
        JournalTests.RecentFormChecks(harness);
        Package(harness);
        DraftPickLayout(harness);
    }

    private static void SameSlots(Harness harness, string name, long[] expected, long[] actual)
    {
        bool same = expected.Length == actual.Length;
        for (int index = 0; same && index < expected.Length; index++)
        {
            same = expected[index] == actual[index];
        }
        harness.Check(name, same,
            $"expected [{string.Join(",", expected)}], got [{string.Join(",", actual)}]");
    }

    private static void Compaction(Harness harness)
    {
        // Slots are ordered and compact: removing a middle player closes the gap.
        var values = new long[] { 0xA1, 0xA2, 0xA3, 0xA4, 0, 0 };
        var removed = RosterMover.Removed(values, 0xA2);
        harness.Equal("roster: removal keeps the slot count", 6, removed.Length);
        SameSlots(harness, "roster: removal compacts",
            new long[] { 0xA1, 0xA3, 0xA4, 0, 0, 0 }, removed);
        harness.Equal("roster: removal shifts the tail", 0xA3L, removed[1]);
        harness.Equal("roster: removal leaves a zero at the end", 0L, removed[^1]);
    }

    private static void Append(Harness harness)
    {
        var values = new long[] { 0xA1, 0, 0xA3 };
        var added = RosterMover.Added(values, 0xB7);
        harness.Equal("roster: addition fills the first open slot", 0xB7L, added[1]);
        harness.Equal("roster: addition leaves the source array alone", 0L, values[1]);
        harness.Equal("roster: addition keeps the slot count", 3, added.Length);

        var tail = RosterMover.Added(new long[] { 0xA1, 0xA2, 0 }, 0xB7);
        harness.Equal("roster: addition appends after the last player", 0xB7L, tail[2]);
    }

    private static void RoundTrip(Harness harness)
    {
        // A move compacts the source team and takes the first open slot on the target team.
        var source = new long[] { 0xA1, 0xA2, 0xA3, 0, 0, 0 };
        var target = new long[] { 0xB1, 0, 0xB3, 0, 0, 0 };
        var sourceAfter = RosterMover.Removed(source, 0xA2);
        var targetAfter = RosterMover.Added(target, 0xA2);
        SameSlots(harness, "roster: the source team closes the gap",
            new long[] { 0xA1, 0xA3, 0, 0, 0, 0 }, sourceAfter);
        SameSlots(harness, "roster: the target team takes the first open slot",
            new long[] { 0xB1, 0xA2, 0xB3, 0, 0, 0 }, targetAfter);
        harness.Equal("roster: the source array is not mutated in place", 0xA2L, source[1]);
        harness.Equal("roster: the target array is not mutated in place", 0L, target[1]);

        // Replaying slot arithmetic alone does not restore the original order, which is exactly
        // why undo replays the recorded byte patches instead of recomputing slots.
        var naiveUndo = RosterMover.Added(sourceAfter, 0xA2);
        SameSlots(harness, "roster: slot math alone appends on undo",
            new long[] { 0xA1, 0xA3, 0xA2, 0, 0, 0 }, naiveUndo);
    }

    private static void Package(Harness harness)
    {
        // Two-for-one: A sends A2 and A3, receives B1; B sends B1, receives A2 and A3.
        var a = new long[] { 0xA1, 0xA2, 0xA3, 0xA4, 0 };
        var b = new long[] { 0xB1, 0xB2, 0, 0, 0 };
        var aAfter = RosterMover.Repacked(a, new long[] { 0xA2, 0xA3 }, new long[] { 0xB1 }, "A");
        var bAfter = RosterMover.Repacked(b, new long[] { 0xB1 }, new long[] { 0xA2, 0xA3 }, "B");
        SameSlots(harness, "roster: package closes gaps then appends incoming", new long[] { 0xA1, 0xA4, 0xB1, 0, 0 }, aAfter);
        SameSlots(harness, "roster: package keeps incoming order", new long[] { 0xB2, 0xA2, 0xA3, 0, 0 }, bAfter);
        harness.Equal("roster: package does not mutate the input", 0xA2L, a[1]);

        // A full team can still swap one for one because removal runs first.
        var full = new long[] { 0xA1, 0xA2 };
        SameSlots(harness, "roster: full team swaps one for one", new long[] { 0xA1, 0xB1 },
            RosterMover.Repacked(full, new long[] { 0xA2 }, new long[] { 0xB1 }, "A"));
        harness.Throws("roster: package rejects exceeding the roster limit",
            () => RosterMover.Repacked(full, new long[] { 0xA2 }, new long[] { 0xB1, 0xB2 }, "Atlanta Hawks"),
            "Atlanta Hawks would have more than 2 players after this trade");
        harness.Throws("roster: package rejects a player not on the team",
            () => RosterMover.Repacked(full, new long[] { 0xB9 }, Array.Empty<long>(), "A"),
            "The player is not in the expected PLAYER slot");
    }

    private static void Rejections(Harness harness)
    {
        harness.Throws("roster: removal rejects an absent player",
            () => RosterMover.Removed(new long[] { 0xA1 }, 0xB7),
            "The player is not in the expected PLAYER slot");
        harness.Throws("roster: addition rejects a full team",
            () => RosterMover.Added(new long[] { 0xA1, 0xA2 }, 0xB7),
            "The target team has no open player slot");
        harness.Throws("roster: addition rejects a duplicate player",
            () => RosterMover.Added(new long[] { 0xA1, 0 }, 0xA1),
            "The player already occupies a slot on the target team");
    }
private static byte[] PickRow(long owner, long original, long player, ulong meta)
    {
        var row = new byte[DraftPick.Size];
        BitConverter.GetBytes(owner).CopyTo(row, 0);
        BitConverter.GetBytes(original).CopyTo(row, 8);
        BitConverter.GetBytes(player).CopyTo(row, 16);
        BitConverter.GetBytes(meta).CopyTo(row, 24);
        return row;
    }

    private static void DraftPickLayout(Harness harness)
    {
        // Metadata from the live table (2026-11-08): Kevin Durant 2007 round 1 pick 1, Carl Landry
        // 2007 round 2 pick 30, Derrick Rose 2008 round 1 pick 1, a traded future 2nd rounder.
        var durant = DraftPick.Decode(300, 0x1000, PickRow(0xA, 0xA, 0xD1, 0x05E30005FFFFFFFF));
        var landry = DraftPick.Decode(359, 0x2000, PickRow(0xB, 0xB, 0xD2, 0x05E31D0DFFFFFFFF));
        var rose = DraftPick.Decode(360, 0x3000, PickRow(0xC, 0xC, 0xD3, 0x00020006FFFFFFFF));
        var future = DraftPick.Decode(70, 0x4000, PickRow(0xE, 0xF, 0, 0x06220A01FFFFFFFF));
        harness.Equal("draft picks: round 1 decoded", 1, durant.Round);
        harness.Equal("draft picks: round 2 decoded", 2, landry.Round);
        harness.Equal("draft picks: order within round", 30, landry.Order);
        harness.Equal("draft picks: pick 1", 1, rose.Order);
        harness.Equal("draft picks: the same draft shares a year slot", durant.YearSlot, landry.YearSlot);
        harness.Check("draft picks: drafted rows are used, open rows are not", durant.Used && !future.Used, "used flags");
        harness.Equal("draft picks: owner and original pointers", 0xEL, future.Owner);
        harness.Equal("draft picks: original pointer", 0xFL, future.Original);

        var years = DraftPick.Years(new[] { (durant.YearSlot, 2007), (rose.YearSlot, 2008) });
        harness.Equal("draft picks: anchored slot keeps its year", 2007, years(durant.YearSlot) ?? 0);
        harness.Equal("draft picks: latest anchor", 2008, years(rose.YearSlot) ?? 0);
        harness.Equal("draft picks: slot after the latest draft is next year", 2009, years(0) ?? 0);
        harness.Equal("draft picks: future slot 1 is two years out", 2010, years(future.YearSlot) ?? 0);
        harness.Equal("draft picks: last future slot before the anchored drafts", 2013, years(4) ?? 0);
        harness.Check("draft picks: no anchors gives no year", DraftPick.Years(Array.Empty<(int, int)>())(3) is null, "null");
        harness.Check("draft picks: plain picks carry no terms",
            !new DraftPickView(future, 2009, null).HasTerms
            && new DraftPickView(DraftPick.Decode(79, 0, PickRow(1, 2, 0, 0x0632130106400015)), null, null).HasTerms,
            "terms flag");
    }
}

/// <summary>Offline checks for the transaction journal document.</summary>
internal static class JournalTests
{
    public static void Run(Harness harness)
    {
        var epoch = new SessionEpoch(34112, 638000000000000000L, 140701805117440L, 161775288L, 161900000L, 161901000L, 4056);
        var transaction = new PlayerTransaction
        {
            Epoch = epoch,
            Source = "roster",
            SourceSignature = (140701805117440L, 4056),
            Status = "applied",
        };
        var record = new PlayerTransactionRecord
        {
            Player = "Brian Skinner",
            Index = 7,
            Address = 0x21C12F52948L,
            Identity = "0102",
            IdentityAfter = "0102",
        };
        record.Changes.Add(new ChangeRecord("Attributes/Offense/3POINT", 63, 66, 47L, 66L));
        record.FieldAddresses["Attributes/Offense/3POINT"] = 0x21C12F52D8BL;
        record.PatchAddresses.Add(0x21C12F52D8BL);
        transaction.Patches.Add(new PatchRow(0x21C12F52D8BL, 63, 66, 125));
        transaction.Players.Add(record);

        var document = (JsonObject)JsonNode.Parse(JournalJson.Serialize(transaction))!;
        harness.Equal("journal: format", "nba2k27-player-edit/1", document["format"]!.GetValue<string>());
        harness.Equal("journal: status", "applied", document["status"]!.GetValue<string>());
        harness.Equal("journal: epoch keeps all seven fields", 7, document["epoch"]!.AsArray().Count);
        harness.Equal("journal: epoch base", 140701805117440L, document["epoch"]![2]!.GetValue<long>());
        harness.Equal("journal: signature body", 140701805117440L,
            document["source_signature"]![0]!.GetValue<long>());
        harness.Equal("journal: signature count", 4056, document["source_signature"]![1]!.GetValue<int>());
        harness.Equal("journal: patch address is exact", 0x21C12F52D8BL,
            document["patches"]![0]!["address"]!.GetValue<long>());
        harness.Equal("journal: patch mask", 125, document["patches"]![0]!["mask"]!.GetValue<int>());

        var player = document["players"]![0]!;
        harness.Equal("journal: player label", "Brian Skinner", player["player"]!.GetValue<string>());
        harness.Equal("journal: player address", 0x21C12F52948L, player["address"]!.GetValue<long>());
        harness.Equal("journal: field address", 0x21C12F52D8BL,
            player["field_addresses"]!["Attributes/Offense/3POINT"]!.GetValue<long>());
        var change = player["changes"]![0]!;
        harness.Equal("journal: change field", "Attributes/Offense/3POINT", change["field"]!.GetValue<string>());
        harness.Equal("journal: change before", 63L, change["before"]!.GetValue<long>());
        harness.Equal("journal: change after", 66L, change["after"]!.GetValue<long>());
        harness.Equal("journal: change display", 47L, change["display"]!.GetValue<long>());

        // A byte payload must serialize as lowercase hex so the journal stays readable.
        record.Changes.Add(new ChangeRecord("Vitals/ID/FIRSTNAME", "AA", "BB", "BB", new byte[] { 0xAA, 0xBB }));
        record.Changes.Add(new ChangeRecord("Vitals/ID/NUMBER", 5, 6, 6L, null));
        var withBytes = (JsonObject)JsonNode.Parse(JournalJson.Serialize(transaction))!;
        harness.Equal("journal: byte payload is hex", "aabb",
            withBytes["players"]![0]!["changes"]![1]!["expected_raw"]!.GetValue<string>());

        // A missing value must serialize as JSON null rather than a placeholder string.
        harness.Check("journal: null expected_raw stays null",
            withBytes["players"]![0]!["changes"]![2]!["expected_raw"] is null);
    }

    // Values read from the live roster on 2026-11-08 (Brian Skinner; Cleveland, Philadelphia).
    internal static void RecentFormChecks(Harness harness)
    {
        harness.Equal("form: game score float", 3.1, Nba2k27Editor.Core.Editing.RecentForm.Float(1078355556));
        harness.Equal("form: game score float 2", 6.9, Nba2k27Editor.Core.Editing.RecentForm.Float(1088212173));
        var games = Nba2k27Editor.Core.Editing.RecentForm.Games(new long[] { 1213437443, 1213042310, 0, 0, 0 }, new long[] { 30, 28, 0, 0, 0 },
            new long[] { 1078355556, 1063675492, 0, 0, 0 });
        harness.Equal("form: empty slots skipped", 2, games.Count);
        harness.Equal("form: date is unix seconds", "2008-06-14", games[0].Date.ToString("yyyy-MM-dd"));
        harness.Equal("form: minutes", 28, games[1].Minutes);
        harness.Equal("form: last10 newest first", "WWWLLLLWLW", Nba2k27Editor.Core.Editing.RecentForm.Results(647));
        harness.Equal("form: signed streak", -1, Nba2k27Editor.Core.Editing.RecentForm.SignedByte(255));
        var values = new Dictionary<string, long> { ["LAST10"] = 934, ["WINLOSSSTREAK"] = 255, ["LAST10HOME"] = 890, ["LAST10HOMECOUNT"] = 10,
            ["LAST10AWAY"] = 1, ["LAST10AWAYCOUNT"] = 10, ["SEASONHOMEWINS"] = 16, ["SEASONHOMELOSSES"] = 25, ["LONGESTWINSTREAK"] = 3 };
        var team = Nba2k27Editor.Core.Editing.RecentForm.Team(name => values.GetValueOrDefault(name));
        harness.Equal("form: team last10", "LWWLLWLWWW", team.Last10);
        harness.Equal("form: team wins", 6, team.Wins);
        harness.Equal("form: team streak", -1, team.Streak);
        harness.Equal("form: team away mask", "WLLLLLLLLL", team.Last10Away);
    }
}
