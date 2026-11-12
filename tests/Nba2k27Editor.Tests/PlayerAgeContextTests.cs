using Nba2k27Editor.Core.Editing;
using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.Tests;

internal static class PlayerAgeContextTests
{
    public static void Run(Harness h)
    {
        foreach (int mode in new[] { 13, 25 })
        {
            var memory = Calendar(mode);
            PlayerAgeContext context = PlayerAgeContext.Read(memory);
            h.Equal($"mode {mode} uses the verified native calendar route", new DateOnly(2009, 10, 16), context.Date);
            h.Equal($"mode {mode} carries the native season year", 2009, context.SeasonEndYear);
            h.Equal($"mode {mode} retains the roster year rule", true, context.UsesRosterYear);
            h.Equal($"mode {mode} calendar reading never writes", 0, memory.Writes);
        }
        var unsupported = Calendar(5);
        h.Check("unverified mode stays unavailable", PlayerAgeContext.Read(unsupported).Error is not null);
        var wrongRoute = Calendar(13);
        wrongRoute.Load(0x3000 + 0x468, BitConverter.GetBytes(0x123456L));
        h.Check("supported mode cannot bypass the native date route", PlayerAgeContext.Read(wrongRoute).Date is null);
        var alternate = Calendar(13);
        alternate.Load(0x4000 + 0x37, new byte[] { 1 });
        h.Check("supported mode cannot bypass alternate calendar validation", PlayerAgeContext.Read(alternate).Date is null);
        var changedCode = Calendar(13);
        changedCode.Load(0x21e9c40, new byte[11]);
        h.Check("changed native code stays unavailable", PlayerAgeContext.Read(changedCode).Date is null);
        var fallback = Calendar(13);
        fallback.Load(0x5000 + 0x7c4c94, BitConverter.GetBytes((ushort)0));
        byte[] rows = new byte[33 * 32];
        BitConverter.GetBytes(25).CopyTo(rows, 0);
        BitConverter.GetBytes(0x848U).CopyTo(rows, 4);
        fallback.Load(0x5000 + 0x79af70, rows);
        h.Equal("verified calendar fallback uses the native season and row date",
            new DateOnly(2009, 10, 16), PlayerAgeContext.Read(fallback).Date);
    }

    private static CalendarMemory Calendar(int mode)
    {
        var memory = new CalendarMemory();
        memory.Load(0x21e9c40, Convert.FromHexString("488B05398286080FB74018C3"));
        memory.Load(0x2ca3fc0, Convert.FromHexString("4883EC28B8FF070000238180030000"));
        memory.Load(0xaa51e80, BitConverter.GetBytes(0x1000L));
        byte[] season = new byte[0x41];
        BitConverter.GetBytes(mode).CopyTo(season, 8);
        BitConverter.GetBytes((ushort)2002).CopyTo(season, 0x18);
        season[0x1c] = 6;
        memory.Load(0x1000, season);
        memory.Load(0xaa84c58 + 0x20, new byte[1]);
        memory.Load(0xaa84c58 + 0x10, BitConverter.GetBytes(0x2000L));
        memory.Load(0x2000, BitConverter.GetBytes(0x3000L));
        memory.Load(0x3000 + 0x468, BitConverter.GetBytes(0x1cbc310L));
        memory.Load(0xaa51ee0, BitConverter.GetBytes(0x4000L));
        memory.Load(0x4000 + 0x37, new byte[1]);
        memory.Load(0xaa51e88, BitConverter.GetBytes(0x5000L));
        memory.Load(0x5000 + 0x7c4c94, BitConverter.GetBytes((ushort)1));
        memory.Load(0x7bfcce0, BitConverter.GetBytes(0U));
        byte[] events = new byte[12];
        BitConverter.GetBytes(1253048321U).CopyTo(events, 0);
        BitConverter.GetBytes(121).CopyTo(events, 8);
        memory.Load(0x5000 + 0x7c4c98, events);
        memory.Load(0x2000 + 0xd0, BitConverter.GetBytes(0x6000L));
        memory.Load(0x6000 + 0xb0, BitConverter.GetBytes(0x7000L));
        memory.Load(0x7000, Convert.FromHexString("B001C3"));
        return memory;
    }

    private sealed class CalendarMemory : IMemory
    {
        private readonly Dictionary<long, byte> _bytes = new();
        public bool Simulated => true;
        public int PointerSize => 8;
        public long BaseAddress => 0;
        public int Pid => 0;
        public bool Attached => true;
        public int Writes { get; private set; }

        public void Load(long address, byte[] data)
        {
            for (int i = 0; i < data.Length; i++) _bytes[address + i] = data[i];
        }

        public byte[] ReadBytes(long address, int size)
        {
            var data = new byte[size];
            for (int i = 0; i < size; i++)
                data[i] = _bytes.TryGetValue(address + i, out byte value)
                    ? value : throw new IOException($"Unmapped calendar fixture address 0x{address + i:X}");
            return data;
        }

        public void WriteBytes(long address, byte[] data)
        {
            Writes++;
            Load(address, data);
        }

        public void WriteUint32(long address, uint value) => WriteBytes(address, BitConverter.GetBytes(value));
        public string ReadWString(long address, int maxChars) => throw new NotSupportedException();
        public string ReadAscii(long address, int maxChars) => throw new NotSupportedException();
        public void WriteWStringFixed(long address, string text, int maxChars) => throw new NotSupportedException();
        public void WriteAsciiFixed(long address, string text, int maxChars) => throw new NotSupportedException();
        public void Close() { }
    }
}
