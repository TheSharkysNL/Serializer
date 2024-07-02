using System.Buffers;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.VisualBasic.CompilerServices;

namespace Serializer;

// currently public so the user of the generator doesn't have to have unsafe code allowed
// TODO: find a way to do this without unsafe code blocks
public static unsafe class DeserializeHelpers<T> 
    where T : class
{
    private static nuint virtualTable;
    private static nuint GetVirtualTable()
    {
        if (virtualTable != 0)
        {
            return virtualTable;
        }
            
        object obj = global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
            
        nuint virtTable = **(nuint**)&obj;
        virtualTable = virtTable;
            
        return virtTable;
    }
            
    public static void SetAsVirtualTable(object obj) =>
        **(nuint**)&obj = GetVirtualTable();
}

public static class DeserializeHelpers
{
    internal const byte NullByte = SerializeHelpers.NullByte;
    
    public static object? Deserialize(Stream stream)
    {
        int sizeOrNullByte = stream.ReadByte();
        if (sizeOrNullByte == NullByte)
        {
            return null;
        }

        string typeName = ReadStringEncoded(sizeOrNullByte, stream);
        Type? type = Type.GetType(typeName);
        if (type is null)
        {
            return null; // this should never happen
        }

        return DeserializeInternal(type, stream);
    }

    private static int ReadLength(int size, Stream stream)
    {
        Unsafe.SkipInit(out int length);

        ref byte byteRef = ref Unsafe.As<int, byte>(ref length);
        int readBytes = stream.Read(MemoryMarshal.CreateSpan(ref byteRef, size)); 
        
        return length;
    }

    private static string ReadStringEncoded(int size, Stream stream)
    {
        int length = ReadLength(size, stream);

        if (length <= 1024)
        {
            Span<byte> bytes = stackalloc byte[length];
            return ReadStringEncoded(bytes, stream);
        }
        else
        {
            byte[] rentedArray = ArrayPool<byte>.Shared.Rent(length);
            Span<byte> bytes = rentedArray.AsSpan(0, length);
            
            string str = ReadStringEncoded(bytes, stream);

            ArrayPool<byte>.Shared.Return(rentedArray);
            
            return str;
        }
    }

    private static string ReadStringEncoded(Span<byte> bytes, Stream stream)
    {
        int readBytes = stream.Read(bytes);
        return Encoding.UTF8.GetString(bytes);
    }

    private static object ReadUnmanagedType(Type type, Stream stream)
    {
        int size = SerializeHelpers.GetSizeOf(type);
        object value = RuntimeHelpers.GetUninitializedObject(type);

        Span<byte> boxedPosition = SerializeHelpers.GetBoxedValue(value, size);
        int readBytes = stream.Read(boxedPosition);
        return value;
    }

    private static object? DeserializeInternal(Type type, Stream stream)
    {
        int sizeOrNullByte = stream.ReadByte();
        if (sizeOrNullByte == NullByte)
        {
            return null;
        }
        
        if (SerializeHelpers.IsUnmanagedType(type))
        {
            return ReadUnmanagedType(type, stream);
        } 
        if (type.IsArray)
        {
            int length = ReadLength(sizeOrNullByte, stream);
            
            Type? elementType = type.GetElementType();
            Debug.Assert(elementType is not null);

            Array array = Array.CreateInstance(elementType, length);

            if (SerializeHelpers.IsUnmanagedType(elementType))
            {
                int size = SerializeHelpers.GetSizeOf(elementType);
                ref byte arrayReference = ref MemoryMarshal.GetArrayDataReference(array);
                int byteSize = size * length;
                
                int readBytes = stream.Read(MemoryMarshal.CreateSpan(ref arrayReference, byteSize));
                return array;
            }
            
            for (int i = 0; i < length; i++)
            {
               array.SetValue(DeserializeInternal(elementType, stream), i);
            }

            return array;
        } 
        if (type == typeof(string))
        {
            int length = ReadLength(sizeOrNullByte, stream);

            string str = new('\0', length);
            
            ref char strRef = ref Unsafe.AsRef(in str.GetPinnableReference());
            ref byte byteRef = ref Unsafe.As<char, byte>(ref strRef);
            Span<byte> bytes =
                MemoryMarshal.CreateSpan(ref byteRef, str.Length * Unsafe.SizeOf<char>());
            int bytesRead = stream.Read(bytes);
            return str;
        }
        
        object value = RuntimeHelpers.GetUninitializedObject(type);
        FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];

            if (i != 0 && field.FieldType is { IsValueType: false, IsSealed: false })
            {
                sizeOrNullByte = stream.ReadByte();
            }
            
            Type? fieldType = GetFieldType(field, stream, sizeOrNullByte);
            if (fieldType is null)
            {
                field.SetValue(value, null);
            }
            else
            {
                object? fieldValue = DeserializeInternal(fieldType, stream);
                field.SetValue(value, fieldValue);
            }
        }

        return value;
    }

    private static Type? GetFieldType(FieldInfo field, Stream stream, int sizeOrNullByte)
    {
        if (field.FieldType.IsValueType || field.FieldType.IsSealed)
        {
            return field.FieldType;
        }
        
        if (sizeOrNullByte == NullByte)
        {
            return null;
        }
        string typeName = ReadStringEncoded(sizeOrNullByte, stream);
        Type? fieldType = Type.GetType(typeName);
        return fieldType;
    }
}