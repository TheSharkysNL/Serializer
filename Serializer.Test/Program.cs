using System.Collections.Immutable;
using System.IO.Compression;
using Serializer;
using Serializer.IO;
using Test;

string path = Environment.CurrentDirectory + "/test.txt";
using (FileWriter bufferWriter = new(path, FileMode.OpenOrCreate))
using (GZipStream writer = new(bufferWriter, CompressionLevel.Optimal, false))
{
    Test2 a = new();
    a.Serialize(writer);
}

using (FileReader bufferReader = new(path))
using (GZipStream reader = new(bufferReader, CompressionMode.Decompress, false))
{
    Test.Test b = Test.Test2.Deserialize(reader);
    Console.WriteLine();
}

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
