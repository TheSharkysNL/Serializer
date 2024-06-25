using Microsoft.Win32.SafeHandles;

namespace Serializer;

public interface ISerializable<T>
    where T : ISerializable<T>
{
    public static abstract T Deserialize(string filename);
    public static abstract T Deserialize(string filename, long offset);
    public static abstract T Deserialize(SafeFileHandle handle);
    public static abstract T Deserialize(SafeFileHandle handle, long offset);
    public static abstract T Deserialize(Stream stream);

    public long Serialize(string filename);
    public long Serialize(string filename, long offset);
    public long Serialize(SafeFileHandle filename);
    public long Serialize(SafeFileHandle filename, long offset);
    public long Serialize(Stream stream);
}