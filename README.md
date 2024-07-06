# What does this serializer do?

This serializer converts a type into their binary form and stores it in this way.

Example:

This
```c#
public partial class MyClass
{
    private char var1 = 'C';
    public string var2 = "this is MyClass";
}
```
Is converted into 
```c#
"C\x001\x00FThis is MyClass"
```


# How to use it

The [Serializer source code](https://github.com/TheSharkysNL/Serializer/) must be downloaded from github. Once downloaded, within the project that you want to use add these project references to your .csproj file:

```csproj
<ItemGroup>
    <ProjectReference Include="{PathToDownloadedCode}\Serializer.Generator\Serializer.Generator.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
    <ProjectReference Include="{PathToDownloadedCode}\Serializer\Serializer.csproj" />
</ItemGroup>
```

Whilst using the Serializer namespace, add the ISerializable interface to the type you want to serialize. The type that you add the interface to must be partial.

```c#
using Serializer;

public partial class MyClass : ISerializable<MyClass>
{
    // your fields and properties here
    // no need to implement the interface
    // if you implement the interface it must be partially implemented, like:
    // public partial long Serialize(string filename) 
}
```

Now you can call the Serialize and Deserialize functions using a file or stream as argument 
```c#
Test.MyClass myClass = new();
myClass.Serialize("/test.txt");

Test.MyClass myClass2 = Test.MyClass.Deserialize(new FileStream("/test.txt", FileMode.Open));
```

# Limitations

## Namespace

A type that implements ISerializable must reside in a namespace. Else an error will occurre.

```c#
namespace MyNamespace
{
    public partial class MyClass : ISerializable<MyClass>
    {
    }
}
```

## Collections

A collection type that only implements IReadOnlyCollection<T> and not ICollection<T> must have a method called Add which returns a new instance of itself with the added item. Or the serializable type that uses the collection must cast it to it's IReadOnlyCollection<T> type.

```c#
namespace MyNamespace
{
    public class MyCollection<T> : IReadOnlyCollection<T>
    {
        public partial MyCollection<T> Add(T value);
    }

    public partial class MyClass : ISerializable<MyClass>
    {
        public MyCollection<int> collection;
    }
}
```

or 

```c#
namespace MyNamespace
{
    public class MyCollection<T> : IReadOnlyCollection<T>
    {
    }

    public partial class MyClass : ISerializable<MyClass>
    {
        public IReadOnlyCollection<int> collection;
    }
}
```

No collection types currently work that don't use this above

