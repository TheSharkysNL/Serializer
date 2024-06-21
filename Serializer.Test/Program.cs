using Microsoft.Win32.SafeHandles;
using Serializer;

Console.WriteLine("Hello World");

public partial class Test : ISerializable<Test>
{
    public string A { get; }

    public string C
    {
        get
        {
            return B;
        }
    }

    public string D => B;

    private string B;
    
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

    public long Serialize(SafeFileHandle filename)
    {
        throw new NotImplementedException();
    }

    public long Serialize(SafeFileHandle filename, long offset)
    {
        throw new NotImplementedException();
    }
}