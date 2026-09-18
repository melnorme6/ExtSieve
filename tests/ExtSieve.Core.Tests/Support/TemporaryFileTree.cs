namespace ExtSieve.Core.Tests.Support;

internal sealed class TemporaryFileTree : IDisposable
{
    public TemporaryFileTree()
    {
        RootPath = Path.Combine(Path.GetTempPath(), $"ExtSieve.Tests.{Guid.NewGuid():N}");
        Directory.CreateDirectory(RootPath);
    }

    public string RootPath { get; }

    public string CreateDirectory(string relativePath)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(path);
        return path;
    }

    public string CreateFile(string relativePath, byte[] content)
    {
        var path = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public void Dispose()
    {
        if (Directory.Exists(RootPath))
        {
            Directory.Delete(RootPath, recursive: true);
        }
    }
}

internal sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
{
    private readonly Action<T> _callback = callback;

    public void Report(T value) => _callback(value);
}
