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

    private delegate void UnboxMethod(ref byte destination, Type type, object? obj);

    private static UnboxMethod unbox;

    static SerializeHelpers()
    {
        DynamicMethod method = new(string.Empty, null, [typeof(byte).MakeByRefType(), typeof(Type), typeof(object)]);

        ILGenerator generator = method.GetILGenerator();
        
        generator.Emit(OpCodes.Ldarg_0);
        generator.Emit(OpCodes.Ldarg_1);

        MethodInfo? createObjectMethod =
            typeof(RuntimeHelpers).GetMethod("GetUninitializedObject", BindingFlags.Public | BindingFlags.Static);
        Debug.Assert(createObjectMethod is not null);
        generator.Emit(OpCodes.Call, createObjectMethod);

        MethodInfo? getMethodTableMethod =
            typeof(RuntimeHelpers).GetMethod("GetMethodTable", BindingFlags.NonPublic | BindingFlags.Static);
        Debug.Assert(getMethodTableMethod is not null);
        generator.Emit(OpCodes.Call, getMethodTableMethod);
        
        // TODO: find way to unbox value
        MethodInfo? boxMethod = typeof(RuntimeHelpers).GetMethod("Unbox_Nullable", BindingFlags.NonPublic | BindingFlags.Static);
        Debug.Assert(boxMethod is not null);
        generator.Emit(OpCodes.Ldarg_2);
        generator.Emit(OpCodes.Call, boxMethod);

        unbox = method.CreateDelegate<UnboxMethod>();
    }
    
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
    
    private static void WriteUnmanagedType(Type type, Stream stream, object? value)
    {
        int size = GetSizeOf(type);
        if (size <= 1024)
        {
            Span<byte> bytes = stackalloc byte[size];
            WriteUnmanagedTypeInternal(type, stream, bytes, value);
        }
        else
        {
            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(size);
            Span<byte> bytes = rentedArray.AsSpan(0, size);

            WriteUnmanagedTypeInternal(type, stream, bytes, value);

            ArrayPool<byte>.Shared.Return(rentedArray);
        }
    }

    private static void WriteUnmanagedTypeInternal(Type type, Stream stream, ReadOnlySpan<byte> bytes, object? value)
    {
        ref byte byteRef = ref MemoryMarshal.GetReference(bytes);
        unbox(ref byteRef, type, value);
        stream.Write(bytes);
    }

    private static void SerializeInternal<T>(T? value, Type? type, Stream stream)
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
                SerializeInternal(arrayValue, arrayType, stream);
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
            FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo field = fields[i];
                object? fieldValue = field.GetValue(value);
                
                Type? valueType = fieldValue?.GetType();
                WriteTypeName(valueType, stream);
                SerializeInternal(fieldValue, valueType, stream);
            }
        }
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