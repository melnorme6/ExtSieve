using ExtSieve.Core.Models;

namespace ExtSieve.App.LocalData;

public interface IExclusionStore : IDisposable
{
    Task<LocalDataResult<FolderExclusionSet>> LoadAsync(string sourceRoot, CancellationToken cancellationToken = default);
    Task SaveAsync(string sourceRoot, FolderExclusionSet exclusions, CancellationToken cancellationToken = default);
}
