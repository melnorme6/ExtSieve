namespace ExtSieve.Core.Services;

public sealed record FolderChildren(IReadOnlyList<string> RelativePaths, bool HasWarnings);

public interface IFolderBrowser
{
    Task<FolderChildren> GetChildrenAsync(string root, string relativeDirectory, CancellationToken cancellationToken);
}
