using ExtSieve.Core.Models;

namespace ExtSieve.Core.Services;

public interface IFolderScanner
{
    Task<ScanResult> ScanAsync(
        string sourceDirectory,
        IProgress<long>? discoveredFileProgress,
        CancellationToken cancellationToken);
}
