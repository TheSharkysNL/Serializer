namespace Serializer;

// currently public so the user of the generator doesn't have to have unsafe code allowed
// TODO: find a way to do this without unsafe code blocks
public static unsafe class DeserializeHelpers<T> 
    where T : class
{
    private static nuint virtualTable;
            
    private static nuint GetVirtualTable()
    {
        if (virtualTable != 0)
        {
            return virtualTable;
        }
            
        object obj = global::System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(T));
            
        nuint virtTable = **(nuint**)&obj;
        virtualTable = virtTable;
            
        return virtTable;
    }
            
    public static void SetAsVirtualTable(object obj) =>
        **(nuint**)&obj = GetVirtualTable();
}