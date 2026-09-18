using ExtSieve.Core.Models;

namespace ExtSieve.Core.Services;

public interface IZipArchiveService
{
    Task<ArchiveResult> CreateAsync(
        string destinationPath,
        ArchivePlan plan,
        bool replaceExistingDestination,
        IProgress<ArchiveProgress>? progress,
        CancellationToken cancellationToken);
}
