using Microsoft.Win32.SafeHandles;

namespace Serializer;

public interface ISerializable<out T>
    where T : ISerializable<T>
{
    /// <summary>
    ///     Deserializes the type <typeparamref name="T"/>
    ///     using the data within the given file
    /// </summary>
    /// <param name="filepath">The path to the file where the <typeparamref name="T"/> object is stored</param>
    /// <returns>The <typeparamref name="T"/> object that was deserialized from the file</returns>
    public static abstract T Deserialize(string filepath);
    /// <summary>
    ///     <inheritdoc cref="Deserialize(string)" path="/summary"/>
    ///     at the given offset
    /// </summary>
    /// <param name="offset">The offset to start at within the given file</param>
    /// <param name="filepath"><inheritdoc cref="Deserialize(string)" path="/param[@name='filepath']"/></param>
    /// <returns><inheritdoc cref="Deserialize(string)" path="/returns"/></returns>
    public static abstract T Deserialize(string filepath, long offset);
    
    /// <summary>
    ///     <inheritdoc cref="Deserialize(string)" path="/summary"/>
    /// </summary>
    /// <param name="handle">The handle to the file where the <typeparamref name="T"/> object is stored</param>
    /// <returns><inheritdoc cref="Deserialize(string)" path="/returns"/></returns>
    public static abstract T Deserialize(SafeFileHandle handle);
    /// <summary>
    ///     <inheritdoc cref="Deserialize(string, long)" path="/summary"/>
    /// </summary>
    /// <param name="handle"><inheritdoc cref="Deserialize(SafeFileHandle)" path="/param[@name='handle']"/></param>
    /// <param name="offset"><inheritdoc cref="Deserialize(string, long)" path="/param[@name='offset']"/></param>
    /// <returns><inheritdoc cref="Deserialize(string)" path="/returns"/></returns>
    public static abstract T Deserialize(SafeFileHandle handle, long offset);
    /// <summary>
    ///     Deserializes the type <typeparamref name="T"/>
    ///     using the data within the given <paramref name="stream"/>
    /// </summary>
    /// <param name="stream">The <see cref="Stream"/> to deserialize</param>
    /// <returns>The <typeparamref name="T"/> object that was deserialized from the <paramref name="stream"/></returns>
    public static abstract T Deserialize(Stream stream);

    /// <summary>
    ///     Serializes the type <typeparamref name="T"/>
    ///     to the given file
    /// </summary>
    /// <param name="filepath">The path to the file where the <typeparamref name="T"/> will be serialized to</param>
    /// <returns>The amount of bytes that were serialized</returns>
    public long Serialize(string filepath);
    
    /// <summary>
    ///     <inheritdoc cref="Serialize(string)" path="/summary"/>
    /// at the given offset
    /// </summary>
    /// <param name="offset"><inheritdoc cref="Deserialize(string, long)" path="/param[@name='offset']"/></param>
    /// <param name="filepath"><inheritdoc cref="Serialize(string)" path="/param[@name='filepath']"/></param>
    /// <returns><inheritdoc cref="Serialize(string)" path="/returns"/></returns>
    public long Serialize(string filepath, long offset);
    /// <summary>
    ///     <inheritdoc cref="Serialize(string)" path="/summary"/>
    /// </summary>
    /// <param name="handle">The handle to the file where the <typeparamref name="T"/> will be serialized to</param>
    /// <returns><inheritdoc cref="Serialize(string)" path="/returns"/></returns>
    public long Serialize(SafeFileHandle handle);
    /// <summary>
    ///     <inheritdoc cref="Serialize(string, long)" path="/summary"/>
    /// </summary>
    /// <param name="offset"><inheritdoc cref="Deserialize(string, long)" path="/param[@name='offset']"/></param>
    /// <param name="handle"><inheritdoc cref="Serialize(SafeFileHandle)" path="/param[@name='handle']"/></param>
    /// <returns><inheritdoc cref="Serialize(string)" path="/returns"/></returns>
    public long Serialize(SafeFileHandle handle, long offset);
    /// <summary>
    ///     Serializes the type <typeparamref name="T"/>
    ///     to the given <paramref name="stream"/>
    /// </summary>
    /// <param name="stream">The <see cref="Stream"/> where the <typeparamref name="T"/> will be serialized to</param>
    /// <returns>
    ///     <inheritdoc cref="Serialize(string)" path="/returns"/>.
    ///     If <see cref="Stream.CanSeek"/> is <see langword="false"/> it returns 0
    /// </returns>
    public long Serialize(Stream stream);
}