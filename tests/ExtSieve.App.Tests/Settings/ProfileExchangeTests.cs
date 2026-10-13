using System.Text;
using System.Text.Json;
using ExtSieve.App.LocalData;
using ExtSieve.App.Tests.Support;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Tests.Settings;

public sealed class ProfileExchangeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportRoundTripContainsOnlySelectedProfileNameAndCanonicalKeys(bool empty)
    {
        using var fixture = new MainWindowFixture();
        var path = Path.Combine(fixture.RootPath, "exchange.json");
        var profile = ExtensionProfile.Create("  Unity  ", empty ? [] : [".CS", "", ".asset", ".cs"]);
        await ProfileExchangeJson.WriteAsync(path, profile, TestContext.Current.CancellationToken);
        await using var stream = File.OpenRead(path);
        var imported = Assert.Single(await ProfileExchangeJson.ReadAsync(stream, TestContext.Current.CancellationToken));
        Assert.Equal("Unity", imported.Name);
        Assert.Equal(profile.ExtensionKeys, imported.ExtensionKeys);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        Assert.Equal(["schemaVersion", "profiles"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, json.RootElement.GetProperty("schemaVersion").GetInt32());
        var exported = Assert.Single(json.RootElement.GetProperty("profiles").EnumerateArray());
        Assert.Equal(["name", "extensions"], exported.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain(fixture.RootPath, json.RootElement.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MultiProfileImportNormalizesNamesKeysAndOrder()
    {
        using var stream = JsonStream("""
            {"schemaVersion":1,"profiles":[{"name":" Unity ","extensions":[".CS",".asset",".cs"]},{"name":"Empty","extensions":[]}]}
            """);
        var profiles = await ProfileExchangeJson.ReadAsync(stream, TestContext.Current.CancellationToken);
        Assert.Equal(["Empty", "Unity"], profiles.Select(profile => profile.Name));
        Assert.Empty(profiles[0].ExtensionKeys);
        Assert.Equal([".asset", ".cs"], profiles[1].ExtensionKeys);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":2,\"profiles\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":null}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[null]}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[{\"name\":\"A\"}]}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[{\"name\":\" \",\"extensions\":[]}]}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[{\"name\":\"A\",\"extensions\":[\"../file\"]}]}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[{\"name\":\"A\",\"extensions\":[]},{\"name\":\" a \",\"extensions\":[]}]}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[],\"sourcePath\":\"/synthetic/source\"}")]
    [InlineData("{\"schemaVersion\":1,\"profiles\":[{\"name\":\"A\",\"extensions\":[],\"files\":[\"file.txt\"]}]}")]
    public async Task InvalidUnsupportedOrExtraneousDocumentIsRejectedBeforeLocalStorage(string content)
    {
        using var stream = JsonStream(content);
        var exception = await Record.ExceptionAsync(() => ProfileExchangeJson.ReadAsync(stream, TestContext.Current.CancellationToken));
        Assert.NotNull(exception);
        Assert.True(exception is JsonException or InvalidDataException or ArgumentException);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OversizedSeekableAndProviderStreamsAreRejected(bool seekable)
    {
        using var fixture = new MainWindowFixture();
        var path = Path.Combine(fixture.RootPath, "oversized.json");
        await using (var output = File.Create(path))
        {
            var header = Encoding.UTF8.GetBytes("{\"schemaVersion\":1,\"profiles\":[]}");
            await output.WriteAsync(header, TestContext.Current.CancellationToken);
            var whitespace = Encoding.UTF8.GetBytes(new string(' ', 4096));
            for (var index = 0; index < 1025; index++) await output.WriteAsync(whitespace, TestContext.Current.CancellationToken);
        }
        await using var file = File.OpenRead(path);
        using var provider = new NonSeekableStream(file);
        await Assert.ThrowsAsync<InvalidDataException>(() => ProfileExchangeJson.ReadAsync(seekable ? file : provider, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task OversizedOrCancelledExportPreservesPreviousFileAndCleansTemporarySiblings()
    {
        using var fixture = new MainWindowFixture();
        var path = Path.Combine(fixture.RootPath, "exchange.json");
        await ProfileExchangeJson.WriteAsync(path, ExtensionProfile.Create("Original", [".txt"]), TestContext.Current.CancellationToken);
        var bytes = await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken);
        var oversized = ExtensionProfile.Create("Oversized", ["." + new string('a', ProfileExchangeJson.MaximumBytes)]);
        await Assert.ThrowsAsync<IOException>(() => ProfileExchangeJson.WriteAsync(path, oversized, TestContext.Current.CancellationToken));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ProfileExchangeJson.WriteAsync(path, ExtensionProfile.Create("New", []), cancelled.Token));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.EnumerateFiles(fixture.RootPath, "*.tmp"));
    }

    private static MemoryStream JsonStream(string content) => new(Encoding.UTF8.GetBytes(content));
    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
