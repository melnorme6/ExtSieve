namespace ExtSieve.App.Services;

public interface IProductAboutProvider
{
    string ProductName { get; }

    string? BuildInfo { get; }

    string? LicenseName { get; }

    string? LicenseDocumentPath { get; }

    Uri? ReleasePageUri { get; }

    Uri? SupportPageUri { get; }

    string? ThirdPartyNoticesPath { get; }
}
