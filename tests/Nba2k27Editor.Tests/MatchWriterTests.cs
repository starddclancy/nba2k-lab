using System.Text.Json.Nodes;
using Nba2k27Editor.Core.Match;

namespace Nba2k27Editor.Tests;

internal static class MatchWriterTests
{
    public static void Run(Harness harness)
    {
        // Apply: every span written, read back, journaled before the first write.
        var memory = new Memory();
        memory.Set(0x1000, "0C000000");
        memory.Set(0x1004, "0600");
        memory.Set(0x2000, "2000");
        var patches = new List<MatchCounterPatch>
        {
            Patch(0x1000, 4, "0C000000", "0E000000"),
            Patch(0x1004, 2, "0600", "0700"),
            Patch(0x2000, 2, "2000", "2200"),
        };
        var phases = new List<string>();
        int writesAtPrepared = -1;
        var record = MatchWriter.Apply(patches, memory.Read, memory.Write, (phase, _) =>
        {
            phases.Add(phase);
            if (phase == "prepared")
            {
                writesAtPrepared = memory.Writes;
            }
        });
        harness.Equal("apply writes every span", "0E000000", memory.Get(0x1000, 4));
        harness.Equal("apply writes two-byte span", "0700", memory.Get(0x1004, 2));
        harness.Equal("apply writes last span", "2200", memory.Get(0x2000, 2));
        harness.Equal("one write per span", 3, memory.Writes);
        harness.Equal("plan is journaled before any write", 0, writesAtPrepared);
        harness.Check("journal records prepared then applied",
            phases.SequenceEqual(new[] { "prepared", "applied" }), string.Join(",", phases));

        // Undo restores before bytes exactly.
        phases.Clear();
        MatchWriter.Undo(record, memory.Read, memory.Write, (phase, _) => phases.Add(phase));
        harness.Equal("undo restores score", "0C000000", memory.Get(0x1000, 4));
        harness.Equal("undo restores counter", "0600", memory.Get(0x1004, 2));
        harness.Equal("undo restores last span", "2000", memory.Get(0x2000, 2));
        harness.Check("undo journals prepared then undone",
            phases.SequenceEqual(new[] { "undo-prepared", "undone" }), string.Join(",", phases));

        // A span that changed since preview: nothing is written at all.
        var changed = Fresh(patches);
        changed.Set(0x1004, "0800");
        Contains(harness, "changed span rejects the whole apply", () =>
            MatchWriter.Apply(patches, changed.Read, changed.Write, (_, _) => { }), "预览后已变化");
        harness.Equal("rejected apply writes nothing", 0, changed.Writes);

        // The game updates a span mid-transaction: written spans are restored.
        var racing = Fresh(patches);
        racing.OnWrite = (address, _) =>
        {
            if (address == 0x1004)
            {
                racing.Set(0x2000, "2100");
            }
        };
        var racePhases = new List<string>();
        Contains(harness, "game update during write stops the transaction", () =>
            MatchWriter.Apply(patches, racing.Read, racing.Write, (phase, _) => racePhases.Add(phase)), "写入过程中");
        harness.Equal("rollback restores first span", "0C000000", racing.Get(0x1000, 4));
        harness.Equal("rollback restores second span", "0600", racing.Get(0x1004, 2));
        harness.Equal("game update is never overwritten", "2100", racing.Get(0x2000, 2));
        harness.Check("journal records the rollback", racePhases.Contains("rolled-back"), string.Join(",", racePhases));

        // Readback mismatch after a write triggers rollback.
        var dropping = Fresh(patches);
        dropping.DropWritesAt = 0x2000;
        Contains(harness, "lost write is detected by readback", () =>
            MatchWriter.Apply(patches, dropping.Read, dropping.Write, (_, _) => { }), "读回不一致");
        harness.Equal("readback failure restores earlier spans", "0C000000", dropping.Get(0x1000, 4));
        harness.Equal("readback failure restores second span", "0600", dropping.Get(0x1004, 2));

        // Rollback cannot restore a span the game changed again: reported, journaled, not overwritten.
        var contested = Fresh(patches);
        contested.DropWritesAt = 0x2000;
        contested.OnWrite = (address, _) =>
        {
            if (address == 0x2000)
            {
                contested.Set(0x1004, "0900");
            }
        };
        var contestedPhases = new List<string>();
        Contains(harness, "unrestorable span is reported", () =>
            MatchWriter.Apply(patches, contested.Read, contested.Write, (phase, _) => contestedPhases.Add(phase)),
            "未能恢复");
        harness.Equal("contested span keeps the game's value", "0900", contested.Get(0x1004, 2));
        harness.Check("contested rollback needs review", contestedPhases.Contains("needs-review"),
            string.Join(",", contestedPhases));

        // Undo refuses when the game changed an applied value.
        var undoMemory = Fresh(patches);
        var applied = MatchWriter.Apply(patches, undoMemory.Read, undoMemory.Write, (_, _) => { });
        undoMemory.Set(0x1000, "10000000");
        int beforeUndoWrites = undoMemory.Writes;
        Contains(harness, "undo refuses after game update", () =>
            MatchWriter.Undo(applied, undoMemory.Read, undoMemory.Write, (_, _) => { }), "撤销已停止");
        harness.Equal("refused undo writes nothing", beforeUndoWrites, undoMemory.Writes);

        // Malformed patches never reach the process.
        var guard = new Memory();
        harness.Throws("empty patch set rejected", () =>
            MatchWriter.Apply(new List<MatchCounterPatch>(), guard.Read, guard.Write, (_, _) => { }));
        harness.Throws("unaligned patch rejected", () => MatchWriter.Apply(
            new List<MatchCounterPatch> { Patch(0x1001, 2, "0000", "0100") }, guard.Read, guard.Write, (_, _) => { }));
        harness.Throws("unsupported width rejected", () => MatchWriter.Apply(
            new List<MatchCounterPatch> { Patch(0x1000, 8, "0000000000000000", "0100000000000000") },
            guard.Read, guard.Write, (_, _) => { }));
        harness.Throws("byte length mismatch rejected", () => MatchWriter.Apply(
            new List<MatchCounterPatch> { Patch(0x1000, 4, "0000", "0100") }, guard.Read, guard.Write, (_, _) => { }));
        harness.Throws("overlapping patches rejected", () => MatchWriter.Apply(
            new List<MatchCounterPatch> { Patch(0x1000, 4, "00000000", "01000000"), Patch(0x1002, 2, "0000", "0100") },
            guard.Read, guard.Write, (_, _) => { }));
        harness.Equal("malformed patches write nothing", 0, guard.Writes);

        // Journal detail carries every span with before/after for manual recovery.
        JsonObject? detail = null;
        var journaled = Fresh(patches);
        MatchWriter.Apply(patches, journaled.Read, journaled.Write, (phase, value) =>
        {
            if (phase == "prepared")
            {
                detail = value;
            }
        });
        harness.Equal("journal lists every span", 3, (detail?["patches"] as JsonArray)?.Count ?? 0);
        harness.Equal("journal keeps before bytes", "0C000000",
            detail?["patches"]?[0]?["before"]?.GetValue<string>());
    }

    /// <summary>The action must throw, and its message must contain <paramref name="fragment"/>.</summary>
    private static void Contains(Harness harness, string name, Action action, string fragment)
    {
        string message = harness.Throws(name + " (throws)", action);
        harness.Check(name, message.Contains(fragment, StringComparison.Ordinal), $"message: {message}");
    }

    private static Memory Fresh(List<MatchCounterPatch> patches)
    {
        var memory = new Memory();
        foreach (var patch in patches)
        {
            memory.Set(patch.Address, patch.BeforeHex);
        }
        return memory;
    }

    private static MatchCounterPatch Patch(long address, int width, string before, string after) =>
        new(0, 7, 1, "points", address, width, before, after);

    private sealed class Memory
    {
        private readonly Dictionary<long, byte> _bytes = new();
        public int Writes;
        public long? DropWritesAt;
        public Action<long, byte[]>? OnWrite;

        public void Set(long address, string hex)
        {
            byte[] data = Convert.FromHexString(hex);
            for (int index = 0; index < data.Length; index++)
            {
                _bytes[address + index] = data[index];
            }
        }

        public string Get(long address, int size) => Convert.ToHexString(Read(address, size));

        public byte[] Read(long address, int size) =>
            Enumerable.Range(0, size).Select(index => _bytes.GetValueOrDefault(address + index)).ToArray();

        public void Write(long address, byte[] data)
        {
            Writes++;
            if (DropWritesAt != address)
            {
                for (int index = 0; index < data.Length; index++)
                {
                    _bytes[address + index] = data[index];
                }
            }
            OnWrite?.Invoke(address, data);
        }
    }
}
