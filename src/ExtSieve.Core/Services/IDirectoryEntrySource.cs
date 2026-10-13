namespace ExtSieve.Core.Services;

public interface IDirectoryEntrySource
{
    IEnumerable<string> EnumerateEntries(string directory);
}

public sealed class DirectoryEntrySource : IDirectoryEntrySource
{
    public IEnumerable<string> EnumerateEntries(string directory) => Directory.EnumerateFileSystemEntries(directory);
}
