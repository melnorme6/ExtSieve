using ExtSieve.Core.Models;

namespace ExtSieve.App.LocalData;

public interface IProfileStore
{
    Task<LocalDataResult<IReadOnlyList<ExtensionProfile>>> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(IReadOnlyList<ExtensionProfile> profiles, CancellationToken cancellationToken = default);
}
