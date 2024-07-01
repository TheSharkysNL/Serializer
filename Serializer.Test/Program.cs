using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Serializer;

Console.WriteLine("Hello World");
MemoryStream stream = new();
Test.Test a = new();
long start = Stopwatch.GetTimestamp();
a.Serialize(stream);
long end = Stopwatch.GetTimestamp();
TimeSpan elapsed = Stopwatch.GetElapsedTime(start, end);
Console.WriteLine(elapsed.TotalMilliseconds);
stream.Position = 0;

start = Stopwatch.GetTimestamp();
Test.Test b = Test.Test.Deserialize(stream);
end = Stopwatch.GetTimestamp();
elapsed = Stopwatch.GetElapsedTime(start, end);
Console.WriteLine(elapsed.TotalMilliseconds);

namespace Test
{
    struct S
    {
        private char c;
        private int b;

        public S(char c, int b)
        {
            this.c = c;
            this.b = b;
        }
    }

    class B
    {
        private char t;
        public string d = "";
    }
    
    public partial class Test : ISerializable<Test>
    {
        public string A { get; } = null;
    
        private string B = "test value to see if the deserializer works";

        private char[] C = ['c', 'd'];
        
        private byte[] T = [1, 2, 3];
        
        private S[] S = [new S('l', 100)];
        
        private S s2 = new('t', 10);
        
        private string[] strings = ["test string 1", "test string 2", "test string 3"];
        
        private List<string> strings2 = ["list test string 12123123"];
        private List<char> charList = ['a', 'b', 'c', 'd'];
        
        private IEnumerable<string> enumerable = ["enumerable string 1"];
        private ICollection<char> collection = ['h', 'e', 'l', 'l', 'o'];
        private IReadOnlyCollection<string> collection2 = ["enumerable string 2", "enumerable string 4"];
        
        // private B b = new B();

        public Test()
        {
            
        }

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