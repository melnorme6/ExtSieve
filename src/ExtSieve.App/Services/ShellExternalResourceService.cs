using System.Diagnostics;

namespace ExtSieve.App.Services;

public sealed class ShellExternalResourceService : IExternalResourceService
{
    public Task OpenFileAsync(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("The requested document does not exist.", fullPath);
        }

        Open(fullPath);
        return Task.CompletedTask;
    }

    public Task OpenUriAsync(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only absolute HTTPS links can be opened.", nameof(uri));
        }

        Open(uri.AbsoluteUri);
        return Task.CompletedTask;
    }

    private static void Open(string target) => Process.Start(new ProcessStartInfo
    {
        FileName = target,
        UseShellExecute = true,
    });
}
