using Microsoft.Win32.SafeHandles;
using Serializer;

Console.WriteLine("Hello World");
MemoryStream stream = new();
Test.Test a = new();
a.Serialize(stream);
Console.WriteLine();

namespace Test
{
    struct S
    {
        private char c;
        private int b;
    }

    class B
    {
        private char t;
        public string d = "";
    }
    
    public partial class Test : ISerializable<Test>
    {
        public string A { get; } = "";
    
        private string B = "";

        private char[] C = [];

        private byte[] T = [];

        private S[] S = [];

        private S s2 = default;

        private string[] strings = [];

        private List<string> strings2 = [];
        private List<char> charList = [];

        private IEnumerable<string> enumerable = [];
        private ICollection<char> collection = [];
        private IReadOnlyCollection<string> collection2 = [];

        private B b = new B();

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