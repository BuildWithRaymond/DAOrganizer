using System.ComponentModel;
using Arbiter.Interop.Win32;

namespace Arbiter.Interop.Process;

public class ProcessMemoryStream : Stream
{
    private bool _isDisposed;
    
    private readonly ProcessAccessFlags _accessFlags;
    private readonly bool _leaveOpen;
    private long _position = 0x400000;

    private readonly byte[] _readBuffer = new byte[4096];
    private readonly byte[] _writeBuffer = new byte[4096];

    public IntPtr ProcessHandle { get; private set; }

    public override bool CanSeek => true;
    public override bool CanRead => ProcessHandle != IntPtr.Zero && _accessFlags.HasFlag(ProcessAccessFlags.Read);
    public override bool CanWrite => ProcessHandle != IntPtr.Zero && _accessFlags.HasFlag(ProcessAccessFlags.Write);

    public override long Length => IntPtr.MaxValue;

    public override long Position
    {
        get
        {
            CheckIfDisposed();
            return _position;
        }
        set
        {
            CheckIfDisposed();
            if (value < 0 || value > Length)
            {
                throw new ArgumentOutOfRangeException(nameof(Position));
            }
            _position = value;
        }
    }

    public ProcessMemoryStream(IntPtr handle, ProcessAccessFlags accessFlags, bool leaveOpen = false)
    {
        ProcessHandle = handle;
        _accessFlags = accessFlags;
        _leaveOpen = leaveOpen;
    }

    ~ProcessMemoryStream()
    {
        Dispose(false);
    }

    public static ProcessMemoryStream Open(int processId, ProcessAccessFlags accessFlags)
    {
        var nativeFlags = Win32ProcessAccessFlags.QueryInformation | Win32ProcessAccessFlags.VmOperation;
        if (accessFlags.HasFlag(ProcessAccessFlags.Read))
        {
            nativeFlags |= Win32ProcessAccessFlags.VmRead;
        }

        if (accessFlags.HasFlag(ProcessAccessFlags.Write))
        {
            nativeFlags |= Win32ProcessAccessFlags.VmWrite;
        }

        var handle = NativeMethods.OpenProcess(nativeFlags, false, processId);
        return handle == IntPtr.Zero ? throw new Win32Exception() : new ProcessMemoryStream(handle, accessFlags);
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        CheckIfDisposed();
        Position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => unchecked(Position + offset),
            SeekOrigin.End => unchecked(Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        return Position;
    }

    public override void SetLength(long value)
    {
        CheckIfDisposed();
        throw new NotSupportedException("Setting the length of a process memory stream is not supported");
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        CheckIfDisposed();

        if (!CanRead)
        {
            throw new InvalidOperationException("Stream is not readable");
        }

        if (Position + count > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Cannot read past the end of the stream");
        }

        var remaining = count;
        while (remaining > 0)
        {
            var blockSize = Math.Min(remaining, _readBuffer.Length);
            if (!NativeMethods.ReadProcessMemory(ProcessHandle, checked((IntPtr)Position), _readBuffer, blockSize,
                    out var bytesRead))
            {
                throw new Win32Exception();
            }

            var readCount = (int)bytesRead;
            Buffer.BlockCopy(_readBuffer, 0, buffer, offset, readCount);

            Position += readCount;
            offset += readCount;
            remaining -= readCount;
        }

        return count;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        CheckIfDisposed();

        if (!CanWrite)
        {
            throw new InvalidOperationException("Stream is not writable");
        }

        if (Position + count > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "Cannot write past the end of the stream");
        }

        var remaining = count;
        while (remaining > 0)
        {
            var blockSize = Math.Min(remaining, _writeBuffer.Length);
            Buffer.BlockCopy(buffer, offset, _writeBuffer, 0, blockSize);
            if (!NativeMethods.WriteProcessMemory(ProcessHandle, checked((IntPtr)Position), _writeBuffer, blockSize,
                    out var bytesWritten))
            {
                throw new Win32Exception();
            }

            var writeCount = (int)bytesWritten;

            Position += writeCount;
            offset += writeCount;
            remaining -= writeCount;
        }
    }

    public override void Flush()
    {
        CheckIfDisposed();
        // Do nothing
    }

    public override void Close()
    {
        CheckIfDisposed();
        if (_leaveOpen || ProcessHandle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.CloseHandle(ProcessHandle);
        ProcessHandle = IntPtr.Zero;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (_isDisposed)
        {
            return;
        }

        if (isDisposing)
        {
            // Cleanup managed resources here
        }

        Close();
        _isDisposed = true;
    }

    private void CheckIfDisposed() => ObjectDisposedException.ThrowIf(_isDisposed, this);
}