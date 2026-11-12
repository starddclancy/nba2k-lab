using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Nba2k27Editor.Core.Gs;

namespace Nba2k27Editor.Core.Memory;

/// <summary>Win32 process memory access used for the live game connection.</summary>
public sealed class Win32Memory : IMemory
{
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessQueryInformation = 0x0400;

    private IntPtr _handle;

    private Win32Memory(int pid, IntPtr handle, long baseAddress, string executable)
    {
        Pid = pid;
        _handle = handle;
        BaseAddress = baseAddress;
        Executable = executable;
    }

    public bool Simulated => false;
    public int PointerSize => 8;
    public long BaseAddress { get; }
    public int Pid { get; }
    public string Executable { get; }
    public bool Attached => _handle != IntPtr.Zero;

    /// <summary>Number of successful WriteProcessMemory calls; used as write evidence.</summary>
    public int Writes { get; private set; }

    public int WriteCalls => Writes;

    public static IReadOnlyList<string> RunningExecutables(string name)
    {
        var result = new List<string>();
        foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(name)))
        {
            result.Add(process.ProcessName + ".exe");
        }
        return result;
    }

    public static Win32Memory Open(string executableName)
    {
        var matches = Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executableName));
        if (matches.Length == 0)
        {
            throw new InvalidOperationException($"{executableName} is not running");
        }
        if (matches.Length > 1)
        {
            throw new InvalidOperationException(
                $"More than one {executableName} process is running; close the extra copies first");
        }
        var process = matches[0];
        IntPtr handle = OpenProcess(
            ProcessVmOperation | ProcessVmRead | ProcessVmWrite | ProcessQueryInformation,
            false,
            process.Id);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Cannot open {executableName} (error {Marshal.GetLastWin32Error()}). Run the editor as administrator");
        }
        long baseAddress;
        try
        {
            baseAddress = process.MainModule?.BaseAddress.ToInt64() ?? 0;
        }
        catch (Exception exc)
        {
            CloseHandle(handle);
            throw new InvalidOperationException($"Cannot read the {executableName} module list: {exc.Message}");
        }
        if (baseAddress == 0)
        {
            CloseHandle(handle);
            throw new InvalidOperationException($"Cannot resolve the {executableName} module base");
        }
        return new Win32Memory(process.Id, handle, baseAddress, executableName);
    }

    public byte[] ReadBytes(long address, int size)
    {
        if (size <= 0)
        {
            return Array.Empty<byte>();
        }
        var buffer = new byte[size];
        if (!ReadProcessMemory(_handle, (IntPtr)address, buffer, (IntPtr)size, out IntPtr read) || read.ToInt64() != size)
        {
            throw new InvalidOperationException(
                $"Cannot read {size} bytes at 0x{address:X} (error {Marshal.GetLastWin32Error()})");
        }
        return buffer;
    }

    public void WriteBytes(long address, byte[] data)
    {
        if (data.Length == 0)
        {
            return;
        }
        if (!WriteProcessMemory(_handle, (IntPtr)address, data, (IntPtr)data.Length, out IntPtr written)
            || written.ToInt64() != data.Length)
        {
            throw new InvalidOperationException(
                $"Cannot write {data.Length} bytes at 0x{address:X} (error {Marshal.GetLastWin32Error()})");
        }
        Writes++;
    }

    public void WriteUint32(long address, uint value)
    {
        WriteBytes(address, BitConverter.GetBytes(value));
    }

    public string ReadWString(long address, int maxChars)
    {
        return MemoryText.DecodeUtf16(ReadBytes(address, maxChars * 2), maxChars);
    }

    public string ReadAscii(long address, int maxChars)
    {
        return MemoryText.DecodeAscii(ReadBytes(address, maxChars), maxChars);
    }

    /// <summary>
    /// Read-only enumeration of committed private pages, used to locate runtime tables that no
    /// static pointer slot currently exposes. Image and mapped-file regions are skipped because
    /// game tables always live on the private heap.
    /// </summary>
    public IReadOnlyList<(long Base, long Size)> PrivateRegions()
    {
        const uint MemCommit = 0x1000;
        const uint MemPrivate = 0x20000;
        const uint PageNoAccess = 0x01;
        const uint PageGuard = 0x100;
        var regions = new List<(long Base, long Size)>();
        int size = Marshal.SizeOf<MemoryBasicInformation>();
        long address = 0x10000;
        while (address > 0 && address < 0x7FFFFFFFFFFF)
        {
            var info = new MemoryBasicInformation();
            if (VirtualQueryEx(_handle, (IntPtr)address, ref info, (UIntPtr)size).ToUInt64() != (ulong)size)
            {
                break;
            }
            long regionBase = info.BaseAddress.ToInt64();
            long regionSize = (long)info.RegionSize.ToUInt64();
            if (regionSize <= 0)
            {
                break;
            }
            bool usable = info.State == MemCommit
                && info.Type == MemPrivate
                && (info.Protect & (PageNoAccess | PageGuard)) == 0;
            if (usable)
            {
                regions.Add((regionBase, regionSize));
            }
            long next = regionBase + regionSize;
            if (next <= address)
            {
                break;
            }
            address = next;
        }
        return regions;
    }

    /// <summary>
    /// Read-only UTF-16 search over private pages. Chunks overlap by the pattern length so a
    /// match spanning a chunk boundary is still found. Unreadable pages are skipped, not fatal.
    /// </summary>
    public IReadOnlyList<long> FindUtf16(string text, int maxHits = 32, long maxBytes = 768L * 1024 * 1024, long? near = null)
    {
        var pattern = Encoding.Unicode.GetBytes(text);
        var hits = new List<long>();
        if (pattern.Length == 0)
        {
            return hits;
        }
        const int chunk = 1 << 20;
        long scanned = 0;
        foreach (var (regionBase, regionSize) in PrivateRegions())
        {
            if (near is long anchor && (anchor < regionBase || anchor >= regionBase + regionSize))
            {
                continue;
            }
            for (long offset = 0; offset < regionSize && scanned < maxBytes; offset += chunk)
            {
                int length = (int)Math.Min(chunk + pattern.Length - 1, regionSize - offset);
                byte[] buffer;
                try
                {
                    buffer = ReadBytes(regionBase + offset, length);
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                scanned += length;
                int start = 0;
                while (start <= buffer.Length - pattern.Length)
                {
                    int found = IndexOf(buffer, pattern, start);
                    if (found < 0)
                    {
                        break;
                    }
                    hits.Add(regionBase + offset + found);
                    if (hits.Count >= maxHits)
                    {
                        return hits;
                    }
                    start = found + 1;
                }
            }
        }
        return hits;
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start)
    {
        int last = haystack.Length - needle.Length;
        for (int index = start; index <= last; index++)
        {
            int scan = 0;
            while (scan < needle.Length && haystack[index + scan] == needle[scan])
            {
                scan++;
            }
            if (scan == needle.Length)
            {
                return index;
            }
        }
        return -1;
    }

    public void WriteWStringFixed(long address, string text, int maxChars)
    {
        WriteBytes(address, MemoryText.EncodeUtf16Fixed(text, maxChars));
    }

    public void WriteAsciiFixed(long address, string text, int maxChars)
    {
        WriteBytes(address, MemoryText.EncodeAsciiFixed(text, maxChars));
    }

    public void Close()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadProcessMemory(
        IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr read);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteProcessMemory(
        IntPtr process, IntPtr address, byte[] buffer, IntPtr size, out IntPtr written);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern UIntPtr VirtualQueryEx(
        IntPtr process, IntPtr address, ref MemoryBasicInformation info, UIntPtr size);
}
