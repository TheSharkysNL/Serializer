using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Serializer;

Console.WriteLine("Hello World");
MemoryStream stream = new();
Test.Test2 a = new();
long start = Stopwatch.GetTimestamp();
a.Serialize(stream);
long end = Stopwatch.GetTimestamp();
TimeSpan elapsed = Stopwatch.GetElapsedTime(start, end);
Console.WriteLine(elapsed.TotalMilliseconds);
stream.Position = 0;

start = Stopwatch.GetTimestamp();
Test.Test b = Test.Test2.Deserialize(stream);
end = Stopwatch.GetTimestamp();
elapsed = Stopwatch.GetElapsedTime(start, end);
Console.WriteLine(elapsed.TotalMilliseconds);

namespace Test
{
    public struct S
    {
        private char c;
        private int b;

        public S(char c, int b)
        {
            this.c = c;
            this.b = b;
        }
    }

    public partial class B
    {
        private char t = 'B';
        public string d = "this is B";
        
        public B()
        {
            
        }
    }
    
    public partial class Test : ISerializable<Test>
    {
        public B b = new B();
        //
        // public string A { get; protected set; } = null;
        //
        // protected string B = "test value to see if the deserializer works";
        //
        // protected char[] C = ['c', 'd'];
        //
        // protected byte[] T = [1, 2, 3];
        //
        // protected S[] S = [new S('l', 100)];
        //
        // protected S s2 = new('t', 10);
        //
        // protected string[] strings = ["test string 1", "test string 2", "test string 3"];
        //
        // protected List<string> strings2 = ["list test string 12123123"];
        // protected List<char> charList = ['a', 'b', 'c', 'd'];
        //
        // protected IEnumerable<string> enumerable = ["enumerable string 1"];
        // protected ICollection<char> collection = ['h', 'e', 'l', 'l', 'o'];
        // protected IReadOnlyCollection<string> collection2 = ["enumerable string 2", "enumerable string 4"];
        // // private IDictionary<string, char> dict = new Dictionary<string, char> { { "hello", 'e' }, { "test", 'c' } };
        protected object test = "123, 321, 456";
        protected Dictionary<string, char> dict =  new() { { "hello", 'e' }, { "test", 'c' } };

        public Test()
        {

        }
    }

    public partial class Test2 : Test
    {
        public Test2(){}   
    }
}