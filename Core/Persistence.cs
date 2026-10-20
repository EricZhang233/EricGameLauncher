using System.Security.Cryptography;
using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace EricGameLauncher;

public static class AtomicFile
{
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp." + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(content);
                stream.Write(bytes);
                stream.Flush(true);
            }
            if (File.Exists(path)) File.Replace(temporary, path, path + ".bak", true);
            else File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public sealed class DataFileException(string path, Exception? inner = null)
    : IOException(string.Format(Text.T("DataFile_Error"), path), inner);

internal static class ConfigPersistence
{
    internal static readonly object Gate = new();
    internal static readonly ISerializer Serializer = new SerializerBuilder().WithTypeConverter(new TimestampYaml()).Build();
    internal static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithTypeConverter(new TimestampYaml())
        .WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

    internal static IDisposable Enter(string path)
    {
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant())));
        var mutex = new Mutex(false, "Local\\EricGameLauncher.Data." + key);
        try
        {
            try { if (!mutex.WaitOne(TimeSpan.FromSeconds(20))) throw new TimeoutException(Text.T("DataFile_Busy")); }
            catch (AbandonedMutexException) { }
            return new Lease(mutex);
        }
        catch { mutex.Dispose(); throw; }
    }

    internal static T Read<T>(string path) where T : new()
    {
        if (!File.Exists(path)) return new T();
        try
        {
            string yaml = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(yaml)) throw new InvalidDataException();
            return Deserializer.Deserialize<T>(yaml) ?? throw new InvalidDataException();
        }
        catch (Exception ex) when (ex is not DataFileException) { throw new DataFileException(path, ex); }
    }

    internal static T Clone<T>(T value) => Deserializer.Deserialize<T>(Serializer.Serialize(value!));
    internal static bool Equal<T>(T left, T right) => Serializer.Serialize(left!) == Serializer.Serialize(right!);

    private sealed class Lease(Mutex mutex) : IDisposable
    {
        public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
    }
}
