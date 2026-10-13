namespace ExtSieve.App.Services;

public interface IUpdateChecker
{
    Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken);
}

public enum UpdateCheckOutcome
{
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(
    UpdateCheckOutcome Outcome,
    string? Version = null,
    Uri? ReleaseUri = null);
