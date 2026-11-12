using System.Text;

namespace Nba2k27Editor.Core.Memory;

/// <summary>Deterministic byte store used by offline tests and dry runs.</summary>
public sealed class SimulatedMemory : IMemory
{
    private readonly byte[] _data;

    public SimulatedMemory(int size = 1 << 20)
    {
        _data = new byte[size];
    }

    public bool Simulated => true;
    public int PointerSize => 8;
    public long BaseAddress { get; set; } = 0x10000000;
    public int Pid => 0;
    public bool Attached => true;
    public int Writes { get; private set; }
    public int Reads { get; private set; }
    public Action<long, byte[]>? OnWrite { get; set; }
    public int? FailOnWrite { get; set; }

    public byte[] Snapshot() => (byte[])_data.Clone();

    public void Load(long address, byte[] data) => Array.Copy(data, 0, _data, address, data.Length);

    public byte[] ReadBytes(long address, int size)
    {
        Check(address, size);
        Reads++;
        var buffer = new byte[size];
        Array.Copy(_data, address, buffer, 0, size);
        return buffer;
    }

    public void WriteBytes(long address, byte[] data)
    {
        Check(address, data.Length);
        Writes++;
        Array.Copy(data, 0, _data, address, data.Length);
        OnWrite?.Invoke(address, data);
        if (FailOnWrite is int limit && Writes >= limit)
        {
            throw new IOException("Injected failure after a partial write");
        }
    }

    public void WriteUint32(long address, uint value) => WriteBytes(address, BitConverter.GetBytes(value));

    public string ReadWString(long address, int maxChars) => MemoryText.DecodeUtf16(ReadBytes(address, maxChars * 2), maxChars);

    public string ReadAscii(long address, int maxChars) => MemoryText.DecodeAscii(ReadBytes(address, maxChars), maxChars);

    public void WriteWStringFixed(long address, string text, int maxChars) =>
        WriteBytes(address, MemoryText.EncodeUtf16Fixed(text, maxChars));

    public void WriteAsciiFixed(long address, string text, int maxChars) =>
        WriteBytes(address, MemoryText.EncodeAsciiFixed(text, maxChars));

    public void Close()
    {
    }

    private void Check(long address, int size)
    {
        if (address < 0 || size < 0 || address + size > _data.Length)
        {
            throw new IOException($"Unmapped address 0x{address:X} ({size} bytes)");
        }
    }
}
