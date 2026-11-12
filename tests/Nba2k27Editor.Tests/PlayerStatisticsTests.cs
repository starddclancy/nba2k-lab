using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Tests;

internal static class PlayerStatisticsTests
{
    public static void Run(Harness harness)
    {
        var memory = new SimulatedMemory(0x20000) { BaseAddress = 0x100 };
        var session = new PlayerSession(memory: memory);
        var model = session.Model;
        // Recovered roots keep the fixture small while exercising the real pointer chains.
        model.RecoveredIdentity = (memory.Pid, memory.BaseAddress);
        model.RecoveredRoots["GameDataStore"] = 0x100;
        memory.Load(0x200, BitConverter.GetBytes(0x1000L));
        memory.Load(0x1000 + 168, BitConverter.GetBytes(0x2000L));
        memory.Load(0x2000 + 464, BitConverter.GetBytes(10));
        memory.Load(0x2000 + 472, BitConverter.GetBytes(0x8000L));
        var item = new RecordListItem("Players", 0, 0x4000, "Statistics Fixture");
        memory.Load(item.Address + 304, BitConverter.GetBytes((ushort)2));
        var spec = session.Specs["SeasonStats/CURRENTYEARSTATID/Scoring & Impact/POINTS"];
        long address = 0x8000 + 2 * 64 + 44;
        memory.Load(address, BitConverter.GetBytes(0xC1230000U | 936));
        var before = memory.Snapshot();
        var read = model.ReadField(spec.Entry, item);
        harness.Equal("season field resolves independent table", address, read.Address);
        harness.Equal("season total reads correct bitfield", 936L, Conversions.ToLong(read.RawValue));
        harness.Check("season field never uses player offset", read.Address != item.Address + 44);
        harness.Check("selector remains read-only", !session.Specs["Stats/Stat IDs/CURRENTYEARSTATID"].Writable);
        var unbound = model.GroupedFields("Players").Single(entry => entry.Section == "Stats"
            && entry.Group == "Scoring & Impact" && entry.NormalizedName == "POINTS");
        harness.Throws("unselected statistic cannot be read as player field", () => model.ReadField(unbound, item));

        var overlay = new Overlay(memory);
        FieldIo.WriteAuthoredValue(overlay, address, spec.Payload, session.Encode(spec, 1000));
        var rows = overlay.Patches();
        var transaction = new PlayerTransactionRecord
        {
            Item = item, Address = item.Address, Index = item.Index,
            Identity = Convert.ToHexString(session.Identity(item)),
            IdentityAfter = Convert.ToHexString(session.Identity(item)),
        };
        transaction.FieldAddresses[spec.Key] = address;
        transaction.PatchAddresses.AddRange(rows.Select(row => row.Address));
        transaction.Changes.Add(new ChangeRecord(spec.Key, 936L, 1000L, 1000L, 1000L));
        PlayerPatchGuard Guard(bool undo = false) => new(
            new[] { (item, transaction) }, () => new[] { item }, session.Identity,
            key => session.Specs[key], session.AddressOf, undo) { Memory = memory };
        var guard = Guard();
        PatchApplier.ApplyPatches(memory, rows, () => { }, guard);
        harness.Equal("season write reads back total", 1000L,
            Conversions.ToLong(model.ReadField(spec.Entry, item).RawValue));
        harness.Equal("season write preserves neighbouring award/impact bits", 0xC1230000U,
            BitConverter.ToUInt32(memory.ReadBytes(address, 4)) & ~0x3FFFU);
        var undoGuard = Guard(undo: true);
        var unresolved = PatchApplier.RestorePatches(memory, rows, playerGuard: undoGuard);
        harness.Equal("season undo has no unresolved bytes", 0, unresolved.Count);
        harness.Sequence("season undo restores whole simulated memory", before, memory.Snapshot());
        harness.Throws("season totals reject storage overflow", () => session.Encode(spec, 16384));
        harness.Throws("season totals reject fraction", () => session.Encode(spec, 2.5));
        foreach (ushort invalid in new ushort[] { 0, 65535, 10 })
        {
            memory.Load(item.Address + 304, BitConverter.GetBytes(invalid));
            harness.Throws($"season selector {invalid} rejected", () => session.AddressOf(spec, item));
        }
        memory.Load(item.Address + 304, BitConverter.GetBytes((ushort)3));
        harness.Throws("guard rejects changed season selector", () => guard.Check());
        memory.Load(item.Address + 304, BitConverter.GetBytes((ushort)2));
        memory.Load(0x2000 + 472, BitConverter.GetBytes(0x9000L));
        harness.Throws("guard rejects relocated statistics table", () => guard.Check());
        memory.Load(0x2000 + 472, BitConverter.GetBytes(0L));
        harness.Throws("null statistics table rejected", () => session.AddressOf(spec, item));
        memory.Load(0x2000 + 472, BitConverter.GetBytes(0x8000L));
        memory.Load(0x2000 + 464, BitConverter.GetBytes(2));
        harness.Throws("guard rejects reduced table count", () => guard.Check());
        memory.Load(0x2000 + 464, BitConverter.GetBytes(10));

        var values = session.Specs.Values.Where(field => field.IsSeasonBound
            && field.Key.StartsWith("SeasonStats/CURRENTYEARSTATID/")).ToDictionary(field => field.Key,
                field => new FieldValue(0L, 0L, session.AddressOf(field, item), field.Writable));
        var snapshot = new PlayerSnapshot(item, new SessionEpoch(0, 0, 0, 0, 0, 0, 0),
            session.Identity(item), values, new());
        string Key(string name) => session.Specs.Values.Single(field => field.IsSeasonBound
            && field.Key.StartsWith("SeasonStats/CURRENTYEARSTATID/") && field.Name == name).Key;
        var referenceSpec = session.Specs[Key("REFERENCECOUNT")];
        FieldIo.WriteAuthoredValue(memory, session.AddressOf(referenceSpec, item), referenceSpec.Payload, 1);
        values[Key("FIELDGOALSATTEMPTED")] = new FieldValue(100L, 100L, 0, true);
        harness.Throws("shooting consistency enforced at host", () => PlayerStatistics.ValidateChanges(session,
            snapshot, new Dictionary<string, object?> { [Key("FIELDGOALSMADE")] = 101 }));
        PlayerStatistics.ValidateChanges(session, snapshot,
            new Dictionary<string, object?> { [Key("FIELDGOALSMADE")] = 80 });
        harness.Check("valid shooting total accepted", true);
        harness.Throws("starts cannot exceed appearances", () => PlayerStatistics.ValidateChanges(session,
            snapshot, new Dictionary<string, object?> { [Key("GAMESSTARTED")] = 1 }));
        values.Remove(Key("FIELDGOALSATTEMPTED"));
        harness.Throws("partial consistency snapshot rejected", () => PlayerStatistics.ValidateChanges(session,
            snapshot, new Dictionary<string, object?> { [Key("FIELDGOALSMADE")] = 1 }));
        FieldIo.WriteAuthoredValue(memory, session.AddressOf(referenceSpec, item), referenceSpec.Payload, 2);
        harness.Throws("shared season record edits rejected", () => PlayerStatistics.ValidateChanges(session,
            snapshot, new Dictionary<string, object?> { [spec.Key] = 1000 }));
        harness.Throws("write guard also rejects new sharing before each byte",
            () => PlayerStatistics.CheckWritableRecord(session, spec, item));
        harness.Check("unverified team-award encoding remains read-only",
            !session.Specs[Key("ALLNBATEAM")].Writable);
    }
}
