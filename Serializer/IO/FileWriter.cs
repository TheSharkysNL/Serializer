using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Win32.SafeHandles;

namespace Serializer.IO;

public sealed class FileWriter : Stream
{
    private readonly SafeFileHandle handle;
    private long position;

    private byte[]? buffer;
    private int bufferPos;
    private readonly int bufferSize;
    
    public bool IsDisposed => handle.IsClosed;
    public override bool CanRead => false;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => RandomAccess.GetLength(handle) + bufferPos;

    public override long Position
    {
        get => position + bufferPos;
        set => Seek(value, SeekOrigin.Begin);
    }

    private const int DefaultBufferSize = 8192;
    public FileWriter(string file) : this(file, FileMode.Open, 0, DefaultBufferSize, FileOptions.None)
    { }

    public FileWriter(string file, long offset) : this(file, FileMode.Open, offset, DefaultBufferSize, FileOptions.None)
    { }

    public FileWriter(string file, FileMode mode) : this(file, mode, 0, DefaultBufferSize, FileOptions.None)
    { }

    public FileWriter(string file, FileMode mode, FileOptions options) : this(file, mode, 0, DefaultBufferSize, options)
    { }

    public FileWriter(string file, FileMode mode, long offset, int bufferSize, FileOptions options)
        : this(File.OpenHandle(file, mode, FileAccess.Write, FileShare.Write, options), offset, bufferSize)
    { }

    public FileWriter(SafeFileHandle handle) : this(handle, 0, DefaultBufferSize)
    { }

    public FileWriter(SafeFileHandle handle, long offset) : this(handle, offset, DefaultBufferSize)
    { }


    public FileWriter(SafeFileHandle handle, long offset, int bufferSize)
    {
        this.handle = handle;
        position = offset;
        
        this.bufferSize = bufferSize;
    }
    
    public override void Flush()
    {
        ValidateNotDisposed();

        if (buffer is null || bufferPos == 0)
        {
            return;
        }

        RandomAccess.Write(handle, buffer.AsSpan(0, bufferPos), position);
        position += bufferPos;

        bufferPos = 0;
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotImplementedException();

    public override long Seek(long offset, SeekOrigin origin)
    {
        ValidateNotDisposed();
        Flush();

        if (origin == SeekOrigin.End)
        {
            position = RandomAccess.GetLength(handle) - offset;
            return position;
        }
        
        bool startFromCurrent = origin != SeekOrigin.Begin;
        long start = Unsafe.As<bool, byte>(ref startFromCurrent) * position;
        
        long newPos = start + offset;
        position = newPos;

        return newPos;
    }

    public override void SetLength(long value)
    {
        ValidateNotDisposed();
        RandomAccess.SetLength(handle, value);
    }

    public override void Write(byte[] array, int offset, int count) =>
        Write(array.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> span)
    {
        int bufferSize = this.bufferSize;
        ValidateNotDisposed();
        EnsureBufferAllocated(bufferSize);

        if (span.Length == 0)
        {
            return;
        }
        
        if (bufferPos > 0)
        {
            int spaceLeft = bufferSize - bufferPos;   // space left in buffer
            if (spaceLeft > 0)
            {
                if (spaceLeft >= span.Length)
                {
                    span.CopyTo(buffer!.AsSpan(bufferPos));
                    bufferPos += span.Length;
                    return;
                }
                span[..spaceLeft].CopyTo(buffer!.AsSpan(bufferPos));
                bufferPos += spaceLeft;
                span = span[spaceLeft..];
            }

            Flush();
        }
        
        if (span.Length >= bufferSize)
        {
            RandomAccess.Write(handle, span, position);
            position += span.Length;
            return;
        }
        
        span.CopyTo(buffer.AsSpan(bufferPos));
        bufferPos = span.Length;
    }

    public override void WriteByte(byte value)
    {
        Write(new ReadOnlySpan<byte>(in value));
    }

    public override void Close()
    {
        handle.Close();
        GC.SuppressFinalize(this);
    }

    [MemberNotNull(nameof(buffer))]
    private void EnsureBufferAllocated(int size)
    {
        if (buffer is null)
        {
            AllocateBuffer(size);
        }
    }


    /// <summary>
    /// see <see cref="BufferedFileStreamStrategy.AllocateBuffer"/>
    /// </summary>
    /// <param name="size"></param>
    [MemberNotNull(nameof(buffer))]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AllocateBuffer(int size)
    {
        Interlocked.CompareExchange(ref buffer, GC.AllocateUninitializedArray<byte>(size), null);
    }
    
    private void ValidateNotDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(FileReader));
    }
    
}