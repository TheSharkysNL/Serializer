using Microsoft.Win32.SafeHandles;
using Serializer;

Console.WriteLine("Hello World");

namespace Test
{
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

        private List<string> strings2;
        private List<char> charList;

        private IEnumerable<string> enumerable;
        private IEnumerable<char> enumerable2;


        public static partial Test Deserialize(string filename);
        public static partial Test Deserialize(string filename, long offset);
        public static partial Test Deserialize(SafeFileHandle handle);
        public static partial Test Deserialize(SafeFileHandle handle, long offset);

        public partial long Serialize(string filename);
        public partial long Serialize(string filename, long offset);
        public partial long Serialize(SafeFileHandle handle);
        public partial long Serialize(SafeFileHandle handle, long offset);
    }
}