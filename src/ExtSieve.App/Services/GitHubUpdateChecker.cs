using System.Net.Http;
using System.Text.Json;

namespace ExtSieve.App.Services;

public sealed class GitHubUpdateChecker : IUpdateChecker
{
    private static readonly HttpClient SharedClient = new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
    })
    {
        Timeout = TimeSpan.FromSeconds(15),
        MaxResponseContentBufferSize = 1024 * 1024,
    };

    private readonly HttpClient _client;
    private readonly Uri? _releasePage;

    public GitHubUpdateChecker(Uri? releasePage) : this(releasePage, SharedClient) { }

    public GitHubUpdateChecker(Uri? releasePage, HttpClient client)
    {
        _releasePage = AssemblyProductAboutProvider.GetPublicReleasePageUri(releasePage?.AbsoluteUri);
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var local = SemanticVersion.Parse(currentVersion);
        if (_releasePage is null || local is null) return new(UpdateCheckOutcome.Failed);

        var endpoint = new Uri("https://api.github.com/repos" + _releasePage.AbsolutePath.TrimEnd('/') + "/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.ParseAdd("ExtSieve/" + local.Value);
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2026-03-10");

        try
        {
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return new(UpdateCheckOutcome.Failed);

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False
                || !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("html_url", out var url) || url.ValueKind != JsonValueKind.String)
                return new(UpdateCheckOutcome.Failed);

            var tagName = tag.GetString()!;
            var remote = SemanticVersion.Parse(tagName.StartsWith('v') || tagName.StartsWith('V') ? tagName[1..] : tagName);
            var releaseUri = GetReleaseUri(url.GetString());
            if (remote is null || remote.IsPrerelease || releaseUri is null) return new(UpdateCheckOutcome.Failed);

            return remote.CompareTo(local) > 0
                ? new(UpdateCheckOutcome.Available, remote.Value, releaseUri)
                : new(UpdateCheckOutcome.UpToDate);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new(UpdateCheckOutcome.Failed);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or IOException)
        {
            return new(UpdateCheckOutcome.Failed);
        }
    }

    private Uri? GetReleaseUri(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || !string.Equals(uri.Host, _releasePage!.Host, StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return null;

        var prefix = _releasePage.AbsolutePath.TrimEnd('/') + "/tag/";
        return uri.AbsolutePath.Length > prefix.Length
            && uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? uri : null;
    }
}
