using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Serializer.IO;

public sealed class FileReader : Stream
{
    private readonly SafeFileHandle handle;
    private long position;
    private readonly long length;

    private byte[]? buffer;
    private int bufferPos;
    private int bufferLength;
    private readonly int bufferSize;

    private const int DefaultBufferSize = 8192;

    public FileReader(string file) : this(file, FileMode.Open, 0, DefaultBufferSize, FileOptions.None)
    { }

    public FileReader(string file, long offset) : this(file, FileMode.Open, offset, DefaultBufferSize, FileOptions.None)
    { }

    public FileReader(string file, FileMode mode) : this(file, mode, 0, DefaultBufferSize, FileOptions.None)
    { }

    public FileReader(string file, FileMode mode, FileOptions options) : this(file, mode, 0, DefaultBufferSize, options)
    { }

    public FileReader(string file, FileMode mode, long offset, int bufferSize, FileOptions options)
        : this(File.OpenHandle(file, mode, FileAccess.Read, FileShare.Read, options), offset, bufferSize)
    { }

    public FileReader(SafeFileHandle handle) : this(handle, 0, DefaultBufferSize)
    { }

    public FileReader(SafeFileHandle handle, long offset) : this(handle, offset, DefaultBufferSize)
    { }


    public FileReader(SafeFileHandle handle, long offset, int bufferSize)
    {
        this.handle = handle;
        position = offset;

        length = RandomAccess.GetLength(handle); // length cannot change while file is being read
        this.bufferSize = bufferSize;
    }

    public bool IsDisposed => handle.IsClosed;

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => length;

    public override long Position
    {
        get => position;
        set => Seek(value, SeekOrigin.Begin);
    }

    public override void Flush()
    {
        bufferPos = 0;
        bufferLength = 0;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> span)
    {
        ValidateNotDisposed();
        EnsureBufferAllocated(bufferSize);
        if (position >= length)
        {
            return 0;
        }

        int bufferLengthLeft = bufferLength - bufferPos;
        if (span.Length >= buffer.Length)
        {
            ref byte bufferRef = ref MemoryMarshal.GetArrayDataReference(buffer);
            ref byte spanRef = ref MemoryMarshal.GetReference(span);

            Unsafe.CopyBlock(ref spanRef, ref Unsafe.Add(ref bufferRef, bufferPos), (uint)bufferLengthLeft);

            int readAmount = RandomAccess.Read(handle, span.Slice(bufferLengthLeft), position + bufferLengthLeft);
            ResetBuffer();
            int totalReadBytes = readAmount + bufferLengthLeft;
            position += totalReadBytes;
            return totalReadBytes;
        }
        else
        {
            int spanLength = span.Length;
            if (bufferLength == 0 | bufferLengthLeft <= 0)
            {
                bufferLength = RandomAccess.Read(handle, buffer, position);
                bufferPos = 0;
                bufferLengthLeft = bufferLength;
                spanLength = Math.Min(spanLength, bufferLength);
            }

            int copiedAmount = spanLength;
            ref byte bufferRef = ref MemoryMarshal.GetArrayDataReference(buffer);
            ref byte spanRef = ref MemoryMarshal.GetReference(span);

            if (bufferLengthLeft < spanLength)
            {
                Unsafe.CopyBlock(ref spanRef, ref Unsafe.Add(ref bufferRef, bufferPos), (uint)bufferLengthLeft);

                bufferLength = RandomAccess.Read(handle, buffer, position + bufferLengthLeft);
                bufferPos = 0;

                spanLength = Math.Min(spanLength - bufferLengthLeft, bufferLength);
                spanRef = ref Unsafe.Add(ref spanRef, bufferLengthLeft);
            }

            Unsafe.CopyBlock(ref spanRef, ref Unsafe.Add(ref bufferRef, bufferPos), (uint)spanLength);
            bufferPos += spanLength;
            position += copiedAmount;
            return copiedAmount;
        }
    }

    public override int ReadByte()
    {
        Unsafe.SkipInit(out byte b);

        Span<byte> byteSpan = new(ref b);
        int readBytes = Read(byteSpan);
        return readBytes == 0 ? -1 : b;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        ValidateNotDisposed();
        
        long oldPos = position;
        long newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.End => Length + offset,
            _ => oldPos + offset
        };
        if (newPos < 0)
        {
            throw new ArgumentException("offset cannot be less than 0", nameof(offset));
        }
        
        long difference = newPos - oldPos;
        position = newPos;

        int newBufferPos = bufferPos + (int)difference;
        if ((uint)newBufferPos < bufferLength)
            bufferPos = newBufferPos;
        else
            ResetBuffer();

        return newPos;
    }

    protected override void Dispose(bool disposing)
    {
        handle.Close();

        if (disposing)
        {
            buffer = null!;
        }
        base.Dispose(disposing);
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException(); // cannot write

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException(); // cannot write
    
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetBuffer()
    {
        bufferLength = 0;
        bufferPos = 0;
    }

    private void ValidateNotDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(FileReader));
    }
}