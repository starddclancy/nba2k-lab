using Nba2k27Editor.Core.Memory;

namespace Nba2k27Editor.Core.Model;

/// <summary>Finds the current 2K27 root pointer slots by scanning executable sections.</summary>
public static class BaseScanner
{
    private sealed record Pattern(byte[] Bytes, bool[] Wildcards)
    {
        public int Length => Bytes.Length;
    }

    // 48 83 EC 78 C5 FC 10 05 ?? ?? ?? ?? C5 FC 11 44 24 50 80 39 00 75 ??
    private static readonly Pattern Roster = Build(
        "48 83 EC 78 C5 FC 10 05 ?? ?? ?? ?? C5 FC 11 44 24 50 80 39 00 75 ??");

    // 48 83 EC 78 48 8B 15 ?? ?? ?? ?? 48 85 D2 74 0E ... 48 83 C4 78 C3
    private static readonly Pattern Draft = Build(
        "48 83 EC 78 48 8B 15 ?? ?? ?? ?? 48 85 D2 74 0E 44 8B 02 31 C0 85 C9 79 0E 48 83 C4 78 C3 "
        + "45 31 C0 31 C0 85 C9 78 F2 44 39 C1 7D ED 89 4C 24 4C 81 F9 96 00 00 00 73 15 89 C8 "
        + "48 69 C0 38 09 00 00 48 01 D0 48 83 C0 08 48 83 C4 78 C3");

    // 56 48 83 EC 70 48 8B 35 ?? ?? ?? ?? C5 FC 10 05 ?? ?? ?? ?? C5 FC 11 44 24 50 80 7E 30 00 75 ??
    private static readonly Pattern Ai = Build(
        "56 48 83 EC 70 48 8B 35 ?? ?? ?? ?? C5 FC 10 05 ?? ?? ?? ?? C5 FC 11 44 24 50 80 7E 30 00 75 ??");

    private static readonly byte[] GameDataStoreBody = Hex("48 8B 05 ?? ?? ?? ?? 48 8B 80 ?? ?? ?? ?? 48 83 C4 78 C5 F8 77 C3");
    private static readonly bool[] GameDataStoreMask = Mask("48 8B 05 ?? ?? ?? ?? 48 8B 80 ?? ?? ?? ?? 48 83 C4 78 C5 F8 77 C3");
    private static readonly byte[] AiBody = Hex("48 8B 46 38 48 83 C4 70 5E C5 F8 77 C3");

    public static (Dictionary<string, long> Bases, Dictionary<string, string> Failures) FindBases(
        IMemory memory, string moduleName, int chunkBytes = 0x100000)
    {
        var bases = new Dictionary<string, long>(StringComparer.Ordinal);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        var routes = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal)
        {
            ["GameDataStore"] = new(),
            ["DraftClass"] = new(),
            ["AITuning"] = new(),
        };
        if (!string.Equals(moduleName, "NBA2K27.exe", StringComparison.OrdinalIgnoreCase)
            || !memory.Attached || memory.BaseAddress == 0)
        {
            var message = "An attached NBA2K27 reader is required";
            return (bases, routes.Keys.ToDictionary(key => key, _ => message, StringComparer.Ordinal));
        }
        if (chunkBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkBytes), "Chunk size must be positive");
        }
        try
        {
            foreach (var (regionAddress, regionSize) in CodeRegions(memory, memory.BaseAddress))
            {
                var overlap = Array.Empty<byte>();
                for (long offset = 0; offset < regionSize; offset += chunkBytes)
                {
                    int wanted = (int)Math.Min(chunkBytes, regionSize - offset);
                    var chunk = memory.ReadBytes(regionAddress + offset, wanted);
                    var data = new byte[overlap.Length + chunk.Length];
                    Buffer.BlockCopy(overlap, 0, data, 0, overlap.Length);
                    Buffer.BlockCopy(chunk, 0, data, overlap.Length, chunk.Length);
                    long dataStart = regionAddress + offset - overlap.Length;
                    CollectRoutes(data, dataStart, routes);
                    overlap = data.Length > 191 ? data[^191..] : data;
                }
            }
        }
        catch (Exception exc) when (exc is IOException or InvalidOperationException or ArgumentException)
        {
            var message = exc.Message;
            return (bases, routes.Keys.ToDictionary(key => key, _ => message, StringComparer.Ordinal));
        }
        foreach (var pair in routes)
        {
            if (pair.Value.Count == 1)
            {
                bases[pair.Key] = pair.Value.First() - memory.BaseAddress;
            }
            else
            {
                failures[pair.Key] = pair.Value.Count == 0
                    ? $"{pair.Key}: no matching getter"
                    : $"{pair.Key}: ambiguous getter";
            }
        }
        return (bases, failures);
    }

    private static void CollectRoutes(byte[] data, long dataStart, Dictionary<string, HashSet<long>> routes)
    {
        foreach (var (kind, pattern) in new[] { ("GameDataStore", Roster), ("DraftClass", Draft), ("AITuning", Ai) })
        {
            for (int start = 0; start + pattern.Length <= data.Length; start++)
            {
                if (!Matches(data, start, pattern.Bytes, pattern.Wildcards))
                {
                    continue;
                }
                int end = start + pattern.Length;
                if (kind == "DraftClass")
                {
                    routes[kind].Add(dataStart + start + 11 + BitConverter.ToInt32(data, start + 7));
                    continue;
                }
                int branch = end + (sbyte)data[end - 1];
                if (branch < end || branch < 0)
                {
                    continue;
                }
                if (kind == "GameDataStore")
                {
                    if (branch + 22 > data.Length
                        || !Matches(data, branch, GameDataStoreBody, GameDataStoreMask))
                    {
                        continue;
                    }
                    routes[kind].Add(dataStart + branch + 7 + BitConverter.ToInt32(data, branch + 3));
                }
                else
                {
                    if (branch + AiBody.Length > data.Length
                        || !Matches(data, branch, AiBody, Enumerable.Repeat(false, AiBody.Length).ToArray()))
                    {
                        continue;
                    }
                    routes[kind].Add(dataStart + start + 12 + BitConverter.ToInt32(data, start + 8));
                }
            }
        }
    }

    private static List<(long Address, long Size)> CodeRegions(IMemory memory, long baseAddress)
    {
        var dos = memory.ReadBytes(baseAddress, 64);
        if (dos[0] != (byte)'M' || dos[1] != (byte)'Z')
        {
            throw new InvalidOperationException("Missing DOS header");
        }
        int peOffset = BitConverter.ToInt32(dos, 60);
        if (peOffset < 64 || peOffset > 0x100000)
        {
            throw new InvalidOperationException("Invalid PE header offset");
        }
        var pe = memory.ReadBytes(baseAddress + peOffset, 24);
        if (pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0 || pe[4] != 0x64 || pe[5] != 0x86)
        {
            throw new InvalidOperationException("Expected an AMD64 PE image");
        }
        int count = BitConverter.ToUInt16(pe, 6);
        int optionalSize = BitConverter.ToUInt16(pe, 20);
        if (count < 1 || count > 96 || optionalSize < 64 || optionalSize > 4096)
        {
            throw new InvalidOperationException("Invalid PE header bounds");
        }
        var optional = memory.ReadBytes(baseAddress + peOffset + 24, optionalSize);
        if (optional[0] != 0x0b || optional[1] != 0x02)
        {
            throw new InvalidOperationException("Expected PE32+");
        }
        long imageSize = BitConverter.ToUInt32(optional, 56);
        long headersEnd = peOffset + 24L + optionalSize + count * 40L;
        if (headersEnd > imageSize)
        {
            throw new InvalidOperationException("Section headers exceed image");
        }
        var headers = memory.ReadBytes(baseAddress + peOffset + 24 + optionalSize, count * 40);
        var regions = new List<(long, long)>();
        for (int offset = 0; offset + 40 <= headers.Length; offset += 40)
        {
            uint size = BitConverter.ToUInt32(headers, offset + 8);
            uint rva = BitConverter.ToUInt32(headers, offset + 12);
            uint flags = BitConverter.ToUInt32(headers, offset + 36);
            if ((flags & 0x20000000) != 0 && size != 0)
            {
                if (rva < headersEnd || rva + (long)size > imageSize)
                {
                    throw new InvalidOperationException("Executable section exceeds image");
                }
                regions.Add((baseAddress + rva, size));
            }
        }
        if (regions.Count == 0)
        {
            throw new InvalidOperationException("No executable sections");
        }
        return regions;
    }

    private static bool Matches(byte[] data, int start, byte[] pattern, bool[] wildcards)
    {
        if (start < 0 || start + pattern.Length > data.Length)
        {
            return false;
        }
        for (int index = 0; index < pattern.Length; index++)
        {
            if (!wildcards[index] && data[start + index] != pattern[index])
            {
                return false;
            }
        }
        return true;
    }

    private static Pattern Build(string text)
    {
        var bytes = Hex(text);
        return new Pattern(bytes, Mask(text));
    }

    private static byte[] Hex(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = new byte[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            bytes[index] = parts[index] == "??" ? (byte)0 : Convert.ToByte(parts[index], 16);
        }
        return bytes;
    }

    private static bool[] Mask(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var mask = new bool[parts.Length];
        for (int index = 0; index < parts.Length; index++)
        {
            mask[index] = parts[index] == "??";
        }
        return mask;
    }
}
