using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.Core.Editing;

public sealed record PatchRow(long Address, byte Before, byte After, byte Mask);

/// <summary>Runs the field encoders without touching the process, then reports byte patches.</summary>
public sealed class Overlay : IMemory
{
    private readonly IMemory _memory;
    private readonly Dictionary<long, byte> _before = new();
    private readonly Dictionary<long, byte> _after = new();

    public Overlay(IMemory memory)
    {
        _memory = memory;
    }

    public bool Simulated => _memory.Simulated;
    public int PointerSize => _memory.PointerSize;
    public long BaseAddress => _memory.BaseAddress;
    public int Pid => _memory.Pid;
    public bool Attached => _memory.Attached;
    public IDictionary<long, byte> Before => _before;
    public IDictionary<long, byte> After => _after;
    public IReadOnlyCollection<long> TouchedAddresses => _after.Keys;

    public byte[] ReadBytes(long address, int size)
    {
        var raw = _memory.ReadBytes(address, size);
        for (int offset = 0; offset < size; offset++)
        {
            if (_after.TryGetValue(address + offset, out byte pending))
            {
                raw[offset] = pending;
            }
        }
        return raw;
    }

    public void WriteBytes(long address, byte[] data)
    {
        var current = _memory.ReadBytes(address, data.Length);
        for (int offset = 0; offset < data.Length; offset++)
        {
            long location = address + offset;
            if (!_before.ContainsKey(location))
            {
                _before[location] = current[offset];
            }
            _after[location] = data[offset];
        }
    }

    public void WriteUint32(long address, uint value) => WriteBytes(address, BitConverter.GetBytes(value));

    public string ReadWString(long address, int maxChars) =>
        MemoryText.DecodeUtf16(ReadBytes(address, maxChars * 2), maxChars);

    public string ReadAscii(long address, int maxChars) =>
        MemoryText.DecodeAscii(ReadBytes(address, maxChars), maxChars);

    public void WriteWStringFixed(long address, string text, int maxChars) =>
        WriteBytes(address, MemoryText.EncodeUtf16Fixed(text, maxChars));

    public void WriteAsciiFixed(long address, string text, int maxChars) =>
        WriteBytes(address, MemoryText.EncodeAsciiFixed(text, maxChars));

    public void Close()
    {
    }

    public IReadOnlyList<PatchRow> Patches()
    {
        var rows = new List<PatchRow>();
        foreach (var address in _after.Keys.OrderBy(value => value))
        {
            byte before = _before[address];
            byte after = _after[address];
            if (before != after)
            {
                rows.Add(new PatchRow(address, before, after, (byte)(before ^ after)));
            }
        }
        return rows;
    }
}
