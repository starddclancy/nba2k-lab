using System.Text;

namespace Nba2k27Editor.Core.Memory;

/// <summary>Byte level view of the game process, mirroring the Python memory layer.</summary>
public interface IMemory
{
    bool Simulated { get; }
    int PointerSize { get; }
    long BaseAddress { get; }
    int Pid { get; }
    bool Attached { get; }

    byte[] ReadBytes(long address, int size);
    void WriteBytes(long address, byte[] data);
    void WriteUint32(long address, uint value);
    string ReadWString(long address, int maxChars);
    string ReadAscii(long address, int maxChars);
    void WriteWStringFixed(long address, string text, int maxChars);
    void WriteAsciiFixed(long address, string text, int maxChars);
    void Close();
}

internal static class MemoryText
{
    public static string DecodeUtf16(byte[] raw, int maxChars)
    {
        int limit = Math.Min(raw.Length, maxChars * 2);
        var builder = new StringBuilder(maxChars);
        for (int index = 0; index + 1 < limit; index += 2)
        {
            char value = (char)(raw[index] | (raw[index + 1] << 8));
            if (value == '\0')
            {
                break;
            }
            builder.Append(value);
        }
        return builder.ToString();
    }

    public static string DecodeAscii(byte[] raw, int maxChars)
    {
        int limit = Math.Min(raw.Length, maxChars);
        int length = 0;
        while (length < limit && raw[length] != 0)
        {
            length++;
        }
        return Encoding.Latin1.GetString(raw, 0, length);
    }

    public static byte[] EncodeUtf16Fixed(string text, int maxChars)
    {
        var buffer = new byte[maxChars * 2];
        int count = Math.Min(text.Length, maxChars);
        for (int index = 0; index < count; index++)
        {
            char value = text[index];
            buffer[index * 2] = (byte)(value & 0xFF);
            buffer[index * 2 + 1] = (byte)(value >> 8);
        }
        return buffer;
    }

    public static byte[] EncodeAsciiFixed(string text, int maxChars)
    {
        var buffer = new byte[maxChars];
        int count = Math.Min(text.Length, maxChars);
        for (int index = 0; index < count; index++)
        {
            char value = text[index];
            buffer[index] = value <= 0xFF ? (byte)value : (byte)'?';
        }
        return buffer;
    }
}
