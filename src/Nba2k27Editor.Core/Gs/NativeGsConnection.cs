using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Nba2k27Editor.Core.Gs;

/// <summary>Native GS constants verified against the shipped game build.</summary>
public static class NativeGsConstants
{
    public const string BuildSha256 = "0ca3734d217d62197a2a88ea7485fc8f761c8b6862d3bce44616f5bf75e97c89";
    public const long BuildSize = 880513304;
    public const uint BuildTimestamp = 1789002045;
    public const uint BuildImageSize = 911572992;

    public const long RootSlotRva = 0xAA51EE8;
    public const int ArrayOffset = 0xD7C;
    public const int InternalFieldCount = 0xA2;
    public const int BlockSize = InternalFieldCount * 8;
    public const int CopyStride = 0x510;
    public const int RootWindowSize = ArrayOffset + CopyStride + BlockSize;

    /// <summary>
    /// Offset of the menu-verified CPU three-point float inside the read window. The window
    /// starts at general id 0 while three_point is general id 30, so the float sits at
    /// <c>30 * 8 + 4</c>.
    /// </summary>
    public const int TargetOffset = 30 * 8 + 4;
    public const long LegacyBlockRva0 = 178641224;
    public const long LegacyBlockRva1 = 178642520;

    public const uint MemCommit = 0x1000;
    public const uint MemPrivate = 0x20000;
    public const uint MemImage = 0x1000000;
    public const int PageSize = 0x1000;
    public const long MaxAddress = 0x7FFFFFFFFFFF;
}

[StructLayout(LayoutKind.Sequential)]
public struct MemoryBasicInformation
{
    public IntPtr BaseAddress;
    public IntPtr AllocationBase;
    public uint AllocationProtect;
    public ushort PartitionId;
    public UIntPtr RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
}

/// <summary>
/// One build-verified connection to the game, shared by the legacy and general GS readers.
/// The executable digest is checked once at open; region identity is re-checked per read.
/// </summary>
public sealed class NativeGsConnection : IDisposable
{
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmOperation = 0x0008;

    private IntPtr _handle;

    private NativeGsConnection(int pid, long created, long baseAddress, string executable,
        string digest, long size, long modified, long changed, IntPtr handle)
    {
        Pid = pid;
        Created = created;
        Base = baseAddress;
        ExecutablePath = executable;
        Digest = digest;
        FileSize = size;
        FileModified = modified;
        FileChanged = changed;
        _handle = handle;
        Epoch = new[] { (long)pid, created, baseAddress, 0L };
    }

    public int Pid { get; }
    public long Created { get; }
    public long Base { get; }
    public string ExecutablePath { get; }
    public string Digest { get; }
    public long FileSize { get; }
    public long FileModified { get; }
    public long FileChanged { get; }
    public long[] Epoch { get; private set; }
    public int WriteAttempts { get; set; }
    public long BytesRead { get; private set; }
    public int ReadCalls { get; private set; }
    public bool IsOpen => _handle != IntPtr.Zero;

    /// <summary>Open read-only and verify the build; throws when the mapping is unverified.</summary>
    public static NativeGsConnection Open()
    {
        var matches = Process.GetProcessesByName("NBA2K27");
        if (matches.Length != 1)
        {
            throw new InvalidOperationException("Expected exactly one running NBA2K27.exe");
        }
        var process = matches[0];
        int pid = process.Id;
        long created = process.StartTime.ToUniversalTime().Ticks;
        string path;
        try
        {
            path = process.MainModule?.FileName
                ?? throw new InvalidOperationException("Could not identify the game executable");
        }
        catch (Exception exc)
        {
            throw new InvalidOperationException($"Could not identify the game executable: {exc.Message}", exc);
        }
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new InvalidOperationException("This game executable has no verified native GS mapping");
        }
        if (info.Length != NativeGsConstants.BuildSize)
        {
            throw new InvalidOperationException("This game executable has no verified native GS mapping");
        }
        long size = info.Length;
        long modified = info.LastWriteTimeUtc.Ticks;
        long changed = info.CreationTimeUtc.Ticks;

        string digest;
        byte[] header;
        try
        {
            using var stream = File.OpenRead(path);
            header = new byte[4096];
            int got = stream.Read(header, 0, header.Length);
            if (got != header.Length)
            {
                throw new InvalidOperationException("Unsupported game image header");
            }
            stream.Position = 0;
            digest = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        catch (IOException exc)
        {
            throw new InvalidOperationException($"Cannot read the game executable: {exc.Message}", exc);
        }
        CheckPeHeader(header);
        if (digest != NativeGsConstants.BuildSha256 || !StampMatches(info, size, modified, changed))
        {
            throw new InvalidOperationException("Game build changed; native GS connection stopped");
        }
        long baseAddress = process.MainModule?.BaseAddress.ToInt64() ?? 0;
        if (baseAddress == 0)
        {
            throw new InvalidOperationException("Could not identify the game module");
        }
        IntPtr handle = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Cannot open the game for native GS reads (error {Marshal.GetLastWin32Error()})");
        }
        var connection = new NativeGsConnection(pid, created, baseAddress, path, digest, size, modified, changed, handle);
        connection.Epoch[3] = 0;
        try
        {
            CheckPeHeader(connection.Read(baseAddress, 4096));
            connection.Guard();
        }
        catch (Exception)
        {
            connection.Dispose();
            throw;
        }
        return connection;
    }

    /// <summary>Open a write-capable handle for one bounded native write.</summary>
    public IntPtr OpenWriteHandle()
    {
        IntPtr handle = OpenProcess(
            ProcessQueryInformation | ProcessVmRead | ProcessVmWrite | ProcessVmOperation, false, Pid);
        if (handle == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                $"Cannot open the game for a native GS write (error {Marshal.GetLastWin32Error()})");
        }
        return handle;
    }

    public byte[] Read(long address, int size)
    {
        var buffer = new byte[size];
        ReadCalls++;
        if (!ReadProcessMemory(_handle, (IntPtr)address, buffer, (IntPtr)size, out IntPtr read)
            || read.ToInt64() != size)
        {
            throw new InvalidOperationException("Native GS read failed or was partial");
        }
        BytesRead += size;
        return buffer;
    }

    public void Guard()
    {
        if (!IsOpen)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS");
        }
        Process process;
        try
        {
            process = Process.GetProcessById(Pid);
        }
        catch (Exception exc)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS", exc);
        }
        if (!string.Equals(process.ProcessName + ".exe", "NBA2K27.exe", StringComparison.OrdinalIgnoreCase)
            || process.HasExited)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS");
        }
        long created;
        try
        {
            created = process.StartTime.ToUniversalTime().Ticks;
        }
        catch (Exception exc)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS", exc);
        }
        if (created != Created)
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS");
        }
        var info = new FileInfo(ExecutablePath);
        if (!info.Exists || !StampMatches(info, FileSize, FileModified, FileChanged))
        {
            throw new InvalidOperationException("Game process or executable changed; reconnect native GS");
        }
        CheckPeHeader(Read(Base, 4096));
    }

    /// <summary>Query one region; mirrors VirtualQueryEx so callers can validate page ownership.</summary>
    public MemoryBasicInformation Query(long address)
    {
        var info = new MemoryBasicInformation();
        int size = Marshal.SizeOf<MemoryBasicInformation>();
        UIntPtr queried = VirtualQueryEx(_handle, (IntPtr)address, ref info, (UIntPtr)size);
        if (queried.ToUInt64() != (ulong)size)
        {
            throw new InvalidOperationException("Native GS memory-region query failed or was partial");
        }
        return info;
    }

    /// <summary>
    /// Write through an already-open handle. The reader's attempt counter stays zero so the
    /// general GS reader keeps its read-only guarantee; the caller counts its own attempts.
    /// </summary>
    public static void WriteThrough(IntPtr handle, long address, byte[] data)
    {
        if (!WriteProcessMemory(handle, (IntPtr)address, data, (IntPtr)data.Length, out IntPtr written)
            || written.ToInt64() != data.Length)
        {
            throw new InvalidOperationException("Native GS write failed or was partial");
        }
    }

    public static void CloseWriteHandle(IntPtr handle) => CloseHandle(handle);

    public void Write(long address, byte[] data)
    {
        IntPtr handle = OpenWriteHandle();
        try
        {
            WriteAttempts++;
            if (!WriteProcessMemory(handle, (IntPtr)address, data, (IntPtr)data.Length, out IntPtr written)
                || written.ToInt64() != data.Length)
            {
                throw new InvalidOperationException("Native GS write failed or was partial");
            }
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    public void Dispose()
    {
        if (_handle != IntPtr.Zero)
        {
            CloseHandle(_handle);
            _handle = IntPtr.Zero;
        }
    }

    private static bool StampMatches(FileInfo info, long size, long modified, long changed) =>
        info.Length == size
        && info.LastWriteTimeUtc.Ticks == modified
        && info.CreationTimeUtc.Ticks == changed;

    /// <summary>Reject images whose PE header is not the verified 2K27 build.</summary>
    public static void CheckPeHeader(byte[] header)
    {
        if (header.Length < 4096 || header[0] != (byte)'M' || header[1] != (byte)'Z')
        {
            throw new InvalidOperationException("Unsupported game image header");
        }
        int offset = BitConverter.ToInt32(header, 60);
        if (offset < 0 || offset > header.Length - 104
            || header[offset] != (byte)'P' || header[offset + 1] != (byte)'E'
            || header[offset + 2] != 0 || header[offset + 3] != 0)
        {
            throw new InvalidOperationException("Unsupported game PE header");
        }
        ushort machine = BitConverter.ToUInt16(header, offset + 4);
        uint timestamp = BitConverter.ToUInt32(header, offset + 8);
        ushort magic = BitConverter.ToUInt16(header, offset + 24);
        uint imageSize = BitConverter.ToUInt32(header, offset + 80);
        if (machine != 0x8664 || timestamp != NativeGsConstants.BuildTimestamp
            || magic != 0x20B || imageSize != NativeGsConstants.BuildImageSize)
        {
            throw new InvalidOperationException("This game build has no verified native GS mapping");
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
    private static extern UIntPtr VirtualQueryEx(
        IntPtr process, IntPtr address, ref MemoryBasicInformation info, UIntPtr size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);
}
