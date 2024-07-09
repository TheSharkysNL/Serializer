using System.Collections.Immutable;
using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Serializer;
using Serializer.IO;
using Test;

const string path = "/Users/mcyuillian/Projects/Serializer/Serializer.Test/test.txt";
GZipStream writer = new GZipStream(new FileWriter(path, FileMode.OpenOrCreate), CompressionLevel.Optimal);

Test2 a = new();
a.Serialize(writer);

GZipStream reader = new GZipStream(new FileReader(path), CompressionMode.Decompress);

Test.Test b = Test.Test2.Deserialize(reader);
Console.WriteLine();

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
        
        public string A { get; protected set; } = null;
        
        protected string B = "test value to see if the deserializer works";
        
        protected char[] C = ['c', 'd'];
        
        protected byte[] T = [1, 2, 3];
        
        protected S[] S = [new S('l', 100)];
        
        protected S s2 = new('t', 10);
        
        protected string[] strings = ["test string 1", "test string 2", "test string 3"];
        
        protected List<string> strings2 = ["list test string 12123123"];
        protected List<char> charList = ['a', 'b', 'c', 'd'];
        
        protected IEnumerable<string> enumerable = ["enumerable string 1"];
        protected ICollection<char> collection = ['h', 'e', 'l', 'l', 'o'];
        protected ImmutableArray<string> collection2 = ["enumerable string 2", "enumerable string 4"];
        protected IDictionary<string, char> dict2 = new Dictionary<string, char> { { "hello", 'e' }, { "test", 'c' } };
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
