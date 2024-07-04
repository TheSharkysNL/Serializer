namespace Serializer.Builders;

public readonly struct Generic
{
    public readonly string Name;
    public readonly IReadOnlyList<string> Wheres;

    public Generic(string name) 
        : this(name, (IReadOnlyList<string>)Array.Empty<string>()) {}
    
    public Generic(string name, string[] wheres) 
        : this(name, (IReadOnlyList<string>)wheres) {}

    public Generic(string name, IReadOnlyList<string> wheres)
    {
        this.Name = name;
        this.Wheres = wheres;
    }
}