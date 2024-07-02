using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic.CompilerServices;

namespace Serializer;

public static class SerializeHelpers
{
    internal const byte NullByte = 5;
    
    public static void Serialize<T>(T value, Stream stream) =>
        Serialize(value, typeof(T), stream);

    public static void Serialize(object value, Stream stream) =>
        Serialize(value, value.GetType(), stream);

    private static void Serialize<T>(T value, Type type, Stream stream)
    {
        WriteTypeName(type, stream);
        
        SerializeInternal(value, type, stream);
    }

    private static void WriteTypeName(Type? type, Stream stream)
    {
        if (type is null)
        {
            stream.WriteByte(NullByte);
            return;
        }
        string? fullTypeName = type.AssemblyQualifiedName;
        if (fullTypeName is null)
        {
            stream.WriteByte(NullByte);
            return;
        }

        WriteStringEncoded(fullTypeName, stream);
    }

    private static void WriteStringEncoded(string value, Stream stream)
    {
        int count = Encoding.UTF8.GetByteCount(value);
        SerializeLength(count, stream);

        if (count <= 1024)
        {
            Span<byte> bytes = stackalloc byte[count];
            WriteStringToSpan(value.AsSpan(), bytes, stream);
        }
        else
        {
            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(count);
            Span<byte> bytes = rentedArray.AsSpan(0, count);
            WriteStringToSpan(value.AsSpan(), bytes, stream);
            ArrayPool<byte>.Shared.Return(rentedArray);
        }
    }

    private static void WriteStringToSpan(ReadOnlySpan<char> value, Span<byte> span, Stream stream)
    {
        Encoding.UTF8.GetBytes(value, span);
        stream.Write(span);
    }
    
    private static void WriteUnmanagedType(Type type, Stream stream, object value)
    {
        int size = GetSizeOf(type);
        ReadOnlySpan<byte> boxedValue = GetBoxedValue(value, size);
        stream.Write(boxedValue);
    }

    private static void SerializeInternal<T>(T? value, Type? type, Stream stream, bool isMainType = true)
    {
        if (value is null)
        {
            stream.WriteByte(NullByte);
            return;
        }
        Debug.Assert(type is not null);
        
        if (IsUnmanagedType(type))
        {
            stream.WriteByte(0);
            WriteUnmanagedType(type, stream, value);
        }
        else if (type.IsArray)
        {
            Array array = Unsafe.As<T, Array>(ref value);
            int length = array.Length;
            SerializeLength(length, stream);

            Type? arrayType = type.GetElementType();
            Debug.Assert(arrayType is not null);

            if (IsUnmanagedType(arrayType))
            {
                int size = GetSizeOf(arrayType);
                ref byte arrayReference = ref MemoryMarshal.GetArrayDataReference(array);
                int byteSize = size * length;
                
                stream.Write(MemoryMarshal.CreateReadOnlySpan(ref arrayReference, byteSize));
                return;
            }
            
            for (int i = 0; i < length; i++)
            {
                object? arrayValue = array.GetValue(i);
                SerializeInternal(arrayValue, arrayType, stream, false);
            }
        }
        else if (type == typeof(string))
        {
            string str = Unsafe.As<T, string>(ref value);
            
            SerializeLength(str.Length, stream);

            ref char strRef = ref Unsafe.AsRef(in str.GetPinnableReference());
            ref byte byteRef = ref Unsafe.As<char, byte>(ref strRef);
            ReadOnlySpan<byte> bytes =
                MemoryMarshal.CreateReadOnlySpan(ref byteRef, str.Length * Unsafe.SizeOf<char>());
            stream.Write(bytes);
        }
        else
        {
            if (!isMainType)
            {
                stream.WriteByte(0);
            }
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                object? fieldValue = field.GetValue(value);

                Type? valueType = GetAndWriteFieldType(field, fieldValue, stream);
                SerializeInternal(fieldValue, valueType, stream, false);
            }
        }
    }

    private static Type? GetAndWriteFieldType(FieldInfo field, object? fieldValue, Stream stream)
    {
        if (field.FieldType.IsValueType || field.FieldType.IsSealed)
        {
            return field.FieldType;
        }

        Type? valueType = fieldValue?.GetType();
        WriteTypeName(valueType, stream);
        return valueType;
    }

    internal static int GetSizeOf(Type type) =>
        type == typeof(char) ? 2 : Marshal.SizeOf(type);

    private static void SerializeLength(int length, Stream stream)
    {
        byte sizeofLength = (length) switch 
        {
            <= byte.MaxValue => 1,
            <= ushort.MaxValue => 2,
            <= 16777215 => 3,
            _ => 4
        };
        
        stream.WriteByte(sizeofLength);
        ref byte lengthAsByte = ref Unsafe.As<int, byte>(ref length);
        ReadOnlySpan<byte> lengthAsBytes =
            MemoryMarshal.CreateReadOnlySpan(ref lengthAsByte, sizeofLength);
        stream.Write(lengthAsBytes);
    }

    internal static unsafe Span<byte> GetBoxedValue(object value, int size)
    {
        nuint* boxedValuePointer = (*(nuint**)&value) + 1;
        ref byte byteRef = ref Unsafe.As<nuint, byte>(ref Unsafe.AsRef<nuint>(boxedValuePointer));
        return MemoryMarshal.CreateSpan(ref byteRef, size);
    }

    // https://stackoverflow.com/a/53969182
    class UnmanagedCheck<T> where T : unmanaged {}
    internal static bool IsUnmanagedType(Type type)
    {
        if (type.IsPrimitive || type.IsEnum || type.IsPointer)
        {
            return true;
        }

        if (!type.IsValueType)
        {
            return false;
        }
        
        try
        {
            typeof(UnmanagedCheck<>).MakeGenericType(type);
            return true;
        }
        catch
        {
            return false;
        }
    }
}