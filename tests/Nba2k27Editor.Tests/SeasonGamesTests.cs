using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;
using Nba2k27Editor.Core.Model;

namespace Nba2k27Editor.Tests;

internal static class SeasonGamesTests
{
    public static void Run(Harness h)
    {
        foreach (var (code, date) in new (uint, DateOnly)[]
        {
            (0x49739602, new(2008, 12, 19)), (0x497199C1, new(2008, 12, 17)),
            (0x497699C3, new(2008, 12, 22)), (0x497AA8C2, new(2008, 12, 26)),
            (0x497C99C1, new(2008, 12, 28)), (0x497E99C1, new(2008, 12, 30)),
            (0x4982A8C1, new(2009, 1, 2)), (0x49839D82, new(2009, 1, 3)),
        })
        {
            h.Equal("packed game date " + code.ToString("X"), date, SeasonGames.Date(code)!.Value);
        }
        h.Check("invalid zero date rejected", SeasonGames.Date(0) is null);
        h.Check("invalid February 31 rejected", SeasonGames.Date((589U << 21) | (31U << 16)) is null);
        // Captured player bytes, with a fixture UNIQUEID prefix; independent of the decoder.
        byte[] den = { 10, 0, 50, 246, 4, 12, 0, 2, 8, 0, 0, 4, 7, 11, 30, 0, 4, 67, 128, 0, 52, 210, 0, 0 };
        byte[] sac = { 10, 0, 56, 22, 11, 19, 2, 2, 5, 3, 0, 4, 5, 10, 22, 0, 131, 66, 0, 1, 59, 177, 0, 0 };
        foreach (var (bytes, points, minutes, pm, score) in new[]
        {
            (den, 14, 25.0, -10, 8.3), (sac, 28, 28.0, 22, 21.7),
        })
        {
            var line = SeasonGames.Line(bytes, 0)!;
            h.Equal("Iverson points", points, line.Points);
            h.Equal("Iverson minutes", minutes, line.Minutes);
            h.Equal("Iverson +/-", pm, line.PlusMinus);
            double gs = line.Points + .4 * line.FgMade - .7 * line.FgAttempted
                - .4 * (line.FtAttempted - line.FtMade) + .7 * line.OffensiveRebounds
                + .3 * line.DefensiveRebounds + line.Steals + .7 * line.Assists
                + .7 * line.Blocks - .4 * line.Fouls - line.Turnovers;
            h.Equal("Iverson Game Score", score, gs, .0001);
        }
        h.Check("empty player slot omitted", SeasonGames.Line(new byte[24], 0) is null);
        h.Throws("short line rejected", () => SeasonGames.Line(new byte[23], 0));
        byte[] invalid = (byte[])den.Clone();
        invalid[4] = 20;
        h.Throws("impossible shooting rejected", () => SeasonGames.Line(invalid, 0));
        var memory = new SimulatedMemory();
        h.Throws("zero table count rejected", () => SeasonGames.Read(memory, 0x1000, 0));
        h.Throws("unbounded table count rejected", () => SeasonGames.Read(memory, 0x1000, 30001));
        byte[] classic = new byte[SeasonGames.Stride];
        uint classicDate = (290U << 21) | (15U << 16);
        BitConverter.GetBytes(classicDate).CopyTo(classic, 0);
        BitConverter.GetBytes(12).CopyTo(classic, 8);
        h.Equal("1984 era record decoded", new DateOnly(1984, 3, 15), SeasonGames.Decode(classic, 0, 0x1000).Date);
        // The live 12/22 Rockets record changes +12 while a match is loaded; its table remains contiguous.
        var table = new byte[64 + 65 * SeasonGames.Stride];
        for (int i = 0; i < 65; i++)
        {
            int at = 64 + i * SeasonGames.Stride;
            BitConverter.GetBytes(0x497699C3U).CopyTo(table, at);
            BitConverter.GetBytes(i + 1).CopyTo(table, at + 8);
            BitConverter.GetBytes(i == 32 ? 0xBD0134BDU : 0xE6872B7DU).CopyTo(table, at + 12);
        }
        var located = SeasonGames.Locate(table, 0x1000);
        h.Equal("changing match metadata cannot truncate season table", 65, located!.Value.Count);
        h.Equal("native table retains first record", 0x1040L, located.Value.Address);
        memory.Load(0x200, BitConverter.GetBytes(0x1000L));
        memory.Load(0x1000 + 160, BitConverter.GetBytes(0x2000L));
        memory.Load(0x2000 + 40, table[64..]);
        var rooted = SeasonGames.Resolve(memory, 0x200);
        h.Equal("native manager chain resolves full table", 0x2028L, rooted.Address);
        h.Equal("native manager chain follows changed match metadata", 65, rooted.Count);
        memory.Load(0x1000 + 160, BitConverter.GetBytes(0x20000L));
        memory.Load(0x20000 + 40, table[64..]);
        h.Equal("native manager relocation does not reuse old pointer", 0x20028L, SeasonGames.Resolve(memory, 0x200).Address);
        h.Throws("missing native manager cannot fall back to unrelated memory", () => SeasonGames.Resolve(memory, 0x208));
    }
}

internal static class SparseTeamTests
{
    public static void Run(Harness h)
    {
        var memory = new SimulatedMemory(1 << 20) { BaseAddress = 0x100 };
        var model = new Schema(memory, new Offsets("NBA2K27.exe"), "NBA2K27.exe");
        model.RecoveredIdentity = (memory.Pid, memory.BaseAddress);
        model.RecoveredRoots["GameDataStore"] = 0x100;
        memory.Load(0x200, BitConverter.GetBytes(0x1000L));
        memory.Load(0x1000 + 168, BitConverter.GetBytes(0x2000L));
        memory.Load(0x2000 + 128, BitConverter.GetBytes(15));
        memory.Load(0x2000 + 136, BitConverter.GetBytes(0x8000L));
        int stride = model.DomainStride("Teams");
        void Name(int index, string name)
        {
            var field = model.FieldByName("Teams", "TEAMNAME")!;
            var item = new RecordListItem("Teams", index, 0x8000 + (long)index * stride, name);
            FieldIo.WriteAuthoredValue(memory, item.Address + 762, model.FieldVersionPayload(field.Field), name);
        }
        Name(0, "First Team");
        Name(7, "After Six Empty Slots");
        Name(9, "After Single Empty Slot");
        int writes = memory.Writes;
        var teams = model.ScanRecords("Teams", 15);
        h.Equal("sparse teams found after empty slots", 3, teams.Count);
        h.Check("real team indices preserved", teams.Select(team => team.Index).SequenceEqual(new[] { 0, 7, 9 }));
        h.Equal("sparse scan does not write", writes, memory.Writes);
    }
}
