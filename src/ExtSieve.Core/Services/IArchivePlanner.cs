using ExtSieve.Core.Models;

namespace ExtSieve.Core.Services;

public interface IArchivePlanner
{
    ArchivePlan CreatePlan(ArchiveRequest request);
}
