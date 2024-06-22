using Microsoft.Win32.SafeHandles;
using Serializer;

Console.WriteLine("Hello World");

namespace Test
{
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

        public static partial Test Deserialize(string filename);
        public static partial Test Deserialize(string filename, long offset);
        public static partial Test Deserialize(SafeFileHandle handle);
        public static partial Test Deserialize(SafeFileHandle handle, long offset);

        public partial long Serialize(string filename);
        public partial long Serialize(string filename, long offset);
        public partial long Serialize(SafeFileHandle filename);
        public partial long Serialize(SafeFileHandle filename, long offset);
    }
}