using Microsoft.Win32.SafeHandles;
using Serializer;

Console.WriteLine("Hello World");

struct S
{
    private char c;
    private int b;
}

public partial class Test : ISerializable<Test>
{
    public string A { get; }
    
    private string B;

    private char[] C;

    private byte[] T;

    private S[] S;

    private S s2;

    private string[] strings;
    
    public static Test Deserialize(string filename)
    {
        throw new NotImplementedException();
    }

    public static Test Deserialize(string filename, long offset)
    {
        throw new NotImplementedException();
    }

    public static Test Deserialize(SafeFileHandle handle)
    {
        throw new NotImplementedException();
    }

    public static Test Deserialize(SafeFileHandle handle, long offset)
    {
        throw new NotImplementedException();
    }

    public long Serialize(string filename)
    {
        throw new NotImplementedException();
    }

    public long Serialize(string filename, long offset)
    {
        throw new NotImplementedException();
    }

    public long Serialize(SafeFileHandle handle)
    {
        throw new NotImplementedException();
    }

    public long Serialize(SafeFileHandle handle, long offset)
    {
        throw new NotImplementedException();
    }
}