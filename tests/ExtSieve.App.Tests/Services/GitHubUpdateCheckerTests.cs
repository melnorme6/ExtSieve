using System.Net;
using System.Net.Http;
using System.Text.Json;
using ExtSieve.App.Services;

namespace ExtSieve.App.Tests.Services;

public sealed class GitHubUpdateCheckerTests
{
    private static readonly Uri ReleasePage = new("https://github.com/example/extsieve/releases");

    [Theory]
    [InlineData("1.1.0", "1.1.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.1.0", "v1.1.1", UpdateCheckOutcome.Available)]
    [InlineData("1.1.0", "V1.2.0", UpdateCheckOutcome.Available)]
    [InlineData("1.1.0", "2.0.0", UpdateCheckOutcome.Available)]
    [InlineData("1.1.0", "v1.0.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.9.0", "1.10.0", UpdateCheckOutcome.Available)]
    [InlineData("1.10.0", "1.9.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.1.0-rc.1", "v1.1.0", UpdateCheckOutcome.Available)]
    [InlineData("1.2.0-rc.1", "v1.1.0", UpdateCheckOutcome.UpToDate)]
    [InlineData("1.1.0+abcdef1", "v1.1.0+build.2", UpdateCheckOutcome.UpToDate)]
    public async Task ComparesStableTagsAndReturnsOnlyTheDetectedRelease(string local, string tag, UpdateCheckOutcome outcome)
    {
        var uri = new Uri(ReleasePage.AbsoluteUri + "/tag/" + tag);
        using var handler = new FakeHandler((_, _) => Task.FromResult(Release(tag, uri.AbsoluteUri)));
        using var client = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(ReleasePage, client);

        var result = await checker.CheckAsync(local, TestContext.Current.CancellationToken);

        Assert.Equal(outcome, result.Outcome);
        Assert.Equal(outcome == UpdateCheckOutcome.Available ? uri : null, result.ReleaseUri);
        Assert.Equal(outcome == UpdateCheckOutcome.Available ? tag.TrimStart('v', 'V').Split('+')[0] : null, result.Version);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task MakesOneAnonymousGetOnlyWhenExplicitlyCalled()
    {
        using var handler = new FakeHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("https://api.github.com/repos/example/extsieve/releases/latest", request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Content);
            Assert.Null(request.Headers.Authorization);
            Assert.Equal("ExtSieve/1.1.0", request.Headers.UserAgent.ToString());
            Assert.Equal(["Accept", "User-Agent", "X-GitHub-Api-Version"], request.Headers.Select(header => header.Key).Order(StringComparer.Ordinal));
            return Task.FromResult(Release("v1.1.0"));
        });
        using var client = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(ReleasePage, client);
        Assert.Equal(0, handler.CallCount);
        await checker.CheckAsync("1.1.0", TestContext.Current.CancellationToken);
        Assert.Equal(1, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Redirect)]
    public async Task NonSuccessResponsesAreTemporaryFailures(HttpStatusCode status)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        using var client = new HttpClient(handler);
        Assert.Equal(UpdateCheckOutcome.Failed,
            (await new GitHubUpdateChecker(ReleasePage, client).CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"tag_name\":12,\"html_url\":false,\"draft\":false,\"prerelease\":false}")]
    [InlineData("{\"tag_name\":\"v1.2.0\",\"html_url\":\"https://github.com/example/extsieve/releases/tag/v1.2.0\",\"draft\":null,\"prerelease\":false}")]
    public async Task InvalidOrUnexpectedJsonFailsWithoutThrowing(string json)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) }));
        using var client = new HttpClient(handler);
        Assert.Equal(UpdateCheckOutcome.Failed,
            (await new GitHubUpdateChecker(ReleasePage, client).CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
    }

    [Theory]
    [InlineData("1.2.0", true, false)]
    [InlineData("1.2.0", false, true)]
    [InlineData("1.2.0-beta.1", false, false)]
    [InlineData("invalid", false, false)]
    [InlineData("v1.2", false, false)]
    [InlineData("v01.2.0", false, false)]
    public async Task DraftsPrereleasesAndInvalidTagsAreNeverStableUpdates(string tag, bool draft, bool prerelease)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Release(tag, draft: draft, prerelease: prerelease)));
        using var client = new HttpClient(handler);
        Assert.Equal(UpdateCheckOutcome.Failed,
            (await new GitHubUpdateChecker(ReleasePage, client).CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
    }

    [Theory]
    [InlineData("https://unrelated.invalid/release")]
    [InlineData("https://github.com/another/extsieve/releases/tag/v1.2.0")]
    [InlineData("http://github.com/example/extsieve/releases/tag/v1.2.0")]
    [InlineData("https://github.com:444/example/extsieve/releases/tag/v1.2.0")]
    [InlineData("https://!@github.com/example/extsieve/releases/tag/v1.2.0")]
    [InlineData("https://github.com/example/extsieve/releases/tag/v1.2.0?private=value")]
    [InlineData("https://github.com/example/extsieve/releases/tag/v1.2.0#fragment")]
    [InlineData("https://github.com/example/extsieve/releases/download/v1.2.0/app.zip")]
    [InlineData("https://github.com/example/extsieve/releases/tag/")]
    public async Task RejectsReturnedUrlsOutsideTheConfiguredPublicReleasePages(string uri)
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Release("v1.2.0", uri)));
        using var client = new HttpClient(handler);
        Assert.Equal(UpdateCheckOutcome.Failed,
            (await new GitHubUpdateChecker(ReleasePage, client).CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task OfflineFailureCanBeRetried()
    {
        using var handler = new FakeHandler((_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("Offline")));
        using var client = new HttpClient(handler);
        var checker = new GitHubUpdateChecker(ReleasePage, client);
        Assert.Equal(UpdateCheckOutcome.Failed, (await checker.CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
        handler.Handler = (_, _) => Task.FromResult(Release("v1.1.0"));
        Assert.Equal(UpdateCheckOutcome.UpToDate, (await checker.CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task TimeoutReturnsFailureAndExplicitCancellationIsPropagated()
    {
        using var handler = new FakeHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return Release("v1.1.0");
        });
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(30) };
        var checker = new GitHubUpdateChecker(ReleasePage, client);
        Assert.Equal(UpdateCheckOutcome.Failed, (await checker.CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var pending = checker.CheckAsync("1.1.0", cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task OversizedResponseFailsWithinTheClientBufferLimit()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent(new string('x', 2048)) }));
        using var client = new HttpClient(handler) { MaxResponseContentBufferSize = 1024 };
        Assert.Equal(UpdateCheckOutcome.Failed,
            (await new GitHubUpdateChecker(ReleasePage, client).CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
    }

    [Fact]
    public async Task InvalidLocalVersionAndMissingConfigurationMakeNoRequests()
    {
        using var handler = new FakeHandler((_, _) => throw new InvalidOperationException("No HTTP request was expected."));
        using var client = new HttpClient(handler);
        Assert.Equal(UpdateCheckOutcome.Failed,
            (await new GitHubUpdateChecker(ReleasePage, client).CheckAsync("invalid", TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(UpdateCheckOutcome.Failed,
            (await new GitHubUpdateChecker(null, client).CheckAsync("1.1.0", TestContext.Current.CancellationToken)).Outcome);
        Assert.Equal(0, handler.CallCount);
    }

    private static HttpResponseMessage Release(string tag, string? uri = null, bool draft = false, bool prerelease = false) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            tag_name = tag,
            html_url = uri ?? ReleasePage.AbsoluteUri + "/tag/" + tag,
            draft,
            prerelease,
        })),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Handler { get; set; } = handler;
        public int CallCount { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Handler(request, cancellationToken);
        }
    }
}
