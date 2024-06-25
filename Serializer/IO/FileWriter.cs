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
    private int bufferLength;
    private readonly int bufferSize;
    
    public bool IsDisposed => handle.IsClosed;
    public override bool CanRead => false;
    public override bool CanSeek => true;
    public override bool CanWrite => true;
    public override long Length => RandomAccess.GetLength(handle);

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

        if (buffer is null || bufferLength == 0)
        {
            return;
        }

        RandomAccess.Write(handle, buffer.AsSpan(0, bufferPos), position);
        
        ResetBuffer();
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
        ValidateNotDisposed();
        EnsureBufferAllocated(bufferSize);

        int bufferLengthLeft = bufferLength - bufferPos;
        if (span.Length >= bufferLengthLeft)
        {
            Flush();
            
            RandomAccess.Write(handle, span, position);
            position += span.Length;
        }
        else
        {
            span.CopyTo(buffer.AsSpan(bufferPos));
            bufferPos += span.Length;
        }
    }

    [MemberNotNull(nameof(buffer))]
    private void EnsureBufferAllocated(int size)
    {
        if (buffer is null)
        {
            AllocateBuffer(size);
            bufferLength = size;
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
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetBuffer()
    {
        bufferPos = 0;
    }
    
    private void ValidateNotDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(FileReader));
    }
    
}