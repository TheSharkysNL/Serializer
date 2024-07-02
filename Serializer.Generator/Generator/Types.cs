namespace Serializer.Generator;

public static class Types
{
    public const string String = "global::System.String";
    public const string Int64 = "global::System.Int64";
    public const string MemoryMarshal = "global::System.Runtime.InteropServices.MemoryMarshal";
    public const string ReadOnlySpan = "global::System.ReadOnlySpan";
    public const string MemoryExtensions = "global::System.MemoryExtensions";
    public const string Unsafe = "global::System.Runtime.CompilerServices.Unsafe";
    public const string CollectionsMarshal = "global::System.Runtime.InteropServices.CollectionsMarshal";
    public const string ListGeneric = "global::System.Collections.Generic.List";
    public const string IReadonlyListGeneric = "global::System.Collections.Generic.IReadOnlyList";
    public const string IListGeneric = "global::System.Collections.Generic.IList";
    public const string IEnumerableGeneric = "global::System.Collections.Generic.IEnumerable";
    public const string ICollectionGeneric = "global::System.Collections.Generic.ICollection";
    public const string IReadOnlyCollectionGeneric = "global::System.Collections.Generic.IReadOnlyCollection";
    public const string Nullable = "global::System.Nullable";
    public const string Byte = "global::System.Byte";
    public const string Int32 = "global::System.Int32";
    public const string Object = "global::System.Object";
    public const string BindingFlags = "global::System.Reflection.BindingFlags";
    public const string FileReader = "global::Serializer.IO.FileReader";
    public const string FileWriter = "global::Serializer.IO.FileWriter";
    public const string SafeFileHandle = "global::Microsoft.Win32.SafeHandles.SafeFileHandle";
    public const string Stream = "global::System.IO.Stream";
    public const string DeserializeHelpers = "global::Serializer.DeserializeHelpers";
    public const string SerializeHelpers = "global::Serializer.SerializeHelpers";
    public const string Char = "global::System.Char";
    public const string Span = "global::System.Span";
    public const string IDictionaryGeneric = "global::System.Collections.Generic.IDictionary";
    public const string ISetGeneric = "global::System.Collections.Generic.ISet";
    public const string DictionaryGeneric = "global::System.Collections.Generic.Dictionary";
    public const string HashSetGeneric = "global::System.Collections.Generic.HashSet";
    private const string SerializableName = "ISerializable";
    public const string ISerializable = $"global::Serializer.{SerializableName}";
}