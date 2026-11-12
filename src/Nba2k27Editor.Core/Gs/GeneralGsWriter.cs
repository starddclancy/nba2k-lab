namespace Nba2k27Editor.Core.Gs;

/// <summary>A native GS field write that failed after the write attempt started.</summary>
public sealed class GeneralGsWriteException : Exception
{
    public GeneralGsWriteException(string message, int? bytesWritten) : base(message)
    {
        BytesWritten = bytesWritten;
    }

    public int? BytesWritten { get; }
}

/// <summary>A GS transaction that failed; the journal records what was restored.</summary>
public sealed class GeneralGsTransactionException : Exception
{
    public GeneralGsTransactionException(string message, string journal, bool needsReview) : base(message)
    {
        Journal = journal;
        NeedsReview = needsReview;
    }

    public string Journal { get; }
    public bool NeedsReview { get; }
}

/// <summary>
/// Writes one mapped primary-array float at a time. The reader stays read-only: writes go
/// through a separate handle and are counted here, never on the reader connection.
/// </summary>
public sealed class NativeGeneralGsWriter
{
    private readonly NativeGeneralGsReader _reader;

    public NativeGeneralGsWriter(NativeGeneralGsReader reader)
    {
        _reader = reader;
    }

    public int WriteAttempts { get; private set; }

    private static long Address(GeneralGsSnapshot snapshot, string key) =>
        snapshot.Root + NativeGsConstants.ArrayOffset + GsFieldIds.FieldOffset(key);

    public byte[] ReadTarget(GeneralGsSnapshot snapshot, string key)
    {
        _reader.Guard(snapshot);
        var raw = _reader.Connection.Read(Address(snapshot, key), 4);
        _reader.Guard(snapshot);
        if (raw.Length != 4)
        {
            throw new InvalidOperationException("Native GS field read was partial");
        }
        return raw;
    }

    public void Write(GeneralGsSnapshot snapshot, string key, byte[] before, byte[] raw)
    {
        GsFieldIds.FieldOffset(key);
        if (before.Length != 4)
        {
            throw new ArgumentException("Expected the current four GS field bytes");
        }
        GsFieldIds.CheckRaw(raw);
        _reader.Guard(snapshot);
        IntPtr handle = _reader.Connection.OpenWriteHandle();
        int? bytesWritten = null;
        try
        {
            if (!ReadTarget(snapshot, key).SequenceEqual(before))
            {
                throw new InvalidOperationException("Native GS field changed before writing");
            }
            _reader.Guard(snapshot);
            WriteAttempts++;
            NativeGsConnection.WriteThrough(handle, Address(snapshot, key), raw);
            bytesWritten = raw.Length;
            if (!ReadTarget(snapshot, key).SequenceEqual(raw))
            {
                throw new InvalidOperationException("Native GS field readback differs from the target");
            }
        }
        catch (Exception exc)
        {
            throw new GeneralGsWriteException(exc.Message, bytesWritten);
        }
        finally
        {
            NativeGsConnection.CloseWriteHandle(handle);
        }
    }
}
