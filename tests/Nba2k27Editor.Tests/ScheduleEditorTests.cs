using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.Tests;

internal static class ScheduleEditorTests
{
    public static void Run(Harness h)
    {
        var memory = new SimulatedMemory();
        var table = new ScheduleTable(0x10000, 3, "fixture-epoch");
        void Load(int slot, uint code, int id, int home, int away, bool played = false)
        {
            byte[] row = new byte[SeasonGames.Stride];
            BitConverter.GetBytes(code).CopyTo(row, 0);
            BitConverter.GetBytes(played ? code : 0).CopyTo(row, 4);
            BitConverter.GetBytes(id).CopyTo(row, 8);
            BitConverter.GetBytes(1234).CopyTo(row, 12);
            BitConverter.GetBytes((ushort)home).CopyTo(row, 16);
            BitConverter.GetBytes((ushort)away).CopyTo(row, 18);
            memory.Load(table.Address + slot * SeasonGames.Stride, row);
        }
        Load(0, 0x49739602, 1, 1, 2, true);
        Load(1, 0x4982A8C1, 2, 1, 3);
        Load(2, 0x49839D82, 3, 1, 4);
        byte[] baseline = memory.Snapshot();
        var phases = new List<string>();
        var editor = new ScheduleEditor(memory, () => table, (phase, _) =>
        {
            if (phase == "prepared")
            {
                h.Equal("journal is durable before first write", 0, memory.Writes);
            }
            phases.Add(phase);
        });
        h.Throws("played game cannot be rescheduled", () => editor.Prepare(1, new(2009, 1, 5)));
        h.Throws("same day conflict refused", () => editor.Prepare(2, new(2009, 1, 3)));
        h.Throws("past date refused", () => editor.Prepare(2, new(2008, 12, 18)));
        h.Throws("unchanged date refused", () => editor.Prepare(2, new(2009, 1, 2)));
        h.Throws("outside season refused", () => editor.Prepare(2, new(2015, 1, 1)));
        h.Throws("missing game refused", () => editor.Prepare(99, new(2009, 1, 5)));
        var plan = editor.Prepare(2, new(2009, 1, 5));
        h.Equal("preview is read only", 0, memory.Writes);
        h.Equal("preview before", new DateOnly(2009, 1, 2), plan.BeforeDate);
        h.Equal("preview after", new DateOnly(2009, 1, 5), plan.AfterDate);
        editor.Apply(plan.Token);
        uint packed = BitConverter.ToUInt32(memory.ReadBytes(table.Address + SeasonGames.Stride, 4));
        h.Equal("write preserves original low 16 bits", 0xA8C1U, packed & 0xFFFF);
        h.Equal("write changes only date", new DateOnly(2009, 1, 5), SeasonGames.Date(packed)!.Value);
        h.Equal("one complete date span written", 1, memory.Writes);
        h.Throws("preview cannot be replayed", () => editor.Apply(plan.Token));
        h.Equal("undo available", 1, editor.UndoDepth);
        editor.Undo();
        h.Sequence("undo restores entire memory", baseline, memory.Snapshot());
        h.Equal("apply undo writes conserved", 2, memory.Writes);
        h.Equal("undo stack cleared", 0, editor.UndoDepth);
        h.Check("journal records apply and undo", phases.Contains("applied") && phases.Contains("undone"));

        var stale = editor.Prepare(2, new(2009, 1, 5));
        table = table with { Epoch = "other-roster" };
        h.Throws("new roster invalidates preview", () => editor.Apply(stale.Token));
        h.Equal("identity rejection writes nothing", 2, memory.Writes);
        table = table with { Epoch = "fixture-epoch" };
        var changed = editor.Prepare(2, new(2009, 1, 5));
        memory.Load(table.Address + SeasonGames.Stride + 8, BitConverter.GetBytes(99));
        h.Throws("reused record invalidates preview", () => editor.Apply(changed.Token));
        h.Equal("record rejection writes nothing", 2, memory.Writes);
        memory.Load(0, baseline);
        var journalFail = new ScheduleEditor(memory, () => table, (_, _) => throw new IOException("disk unavailable"));
        var blocked = journalFail.Prepare(2, new(2009, 1, 5));
        h.Throws("journal failure prevents write", () => journalFail.Apply(blocked.Token));
        h.Equal("failed journal writes nothing", 2, memory.Writes);

        memory.Load(0, baseline);
        var guarded = new ScheduleEditor(memory, () => table, (phase, _) => phases.Add(phase));
        var conflict = guarded.Prepare(2, new(2009, 1, 5));
        Load(2, 0x49859D82, 3, 1, 4);
        h.Throws("new conflict after preview prevents write", () => guarded.Apply(conflict.Token));
        h.Equal("late conflict writes nothing", 2, memory.Writes);
        memory.Load(0, baseline);
        var moved = guarded.Prepare(2, new(2009, 1, 5));
        table = table with { Address = 0x20000 };
        h.Throws("moved table invalidates preview", () => guarded.Apply(moved.Token));
        h.Equal("moved table writes nothing", 2, memory.Writes);
        table = table with { Address = 0x10000 };
        var interrupted = guarded.Prepare(2, new(2009, 1, 5));
        memory.OnWrite = (_, _) =>
        {
            memory.OnWrite = null;
            throw new IOException("write interrupted after transfer");
        };
        h.Throws("write failure propagates", () => guarded.Apply(interrupted.Token));
        h.Sequence("write failure restores full memory", baseline, memory.Snapshot());
        h.Check("write failure journaled", phases.Contains("rolled-back"));
        h.Equal("failed transaction has no undo", 0, guarded.UndoDepth);

        var edited = guarded.Prepare(2, new(2009, 1, 5));
        guarded.Apply(edited.Token);
        memory.Load(table.Address + SeasonGames.Stride, BitConverter.GetBytes(0x4986A8C1U));
        byte[] external = memory.Snapshot();
        int writes = memory.Writes;
        h.Throws("external date update stops undo", guarded.Undo);
        h.Sequence("external update preserved", external, memory.Snapshot());
        h.Equal("rejected undo writes nothing", writes, memory.Writes);
        h.Equal("rejected undo retained", 1, guarded.UndoDepth);

        memory.Load(0, baseline);
        var finalJournal = new ScheduleEditor(memory, () => table, (phase, _) =>
        {
            if (phase == "applied") throw new IOException("post-write journal unavailable");
        });
        var committed = finalJournal.Prepare(2, new(2009, 1, 5));
        h.Throws("post-write journal error reported", () => finalJournal.Apply(committed.Token));
        h.Equal("committed transaction retains undo on journal failure", 1, finalJournal.UndoDepth);
        finalJournal.Undo();
        h.Sequence("journal failure can still undo", baseline, memory.Snapshot());
    }
}
