using System.Text.Json;
using ExtSieve.App.LocalData;
using ExtSieve.App.Tests.Support;
using ExtSieve.Core.Models;

namespace ExtSieve.App.Tests.Settings;

public sealed class LocalDataStoreTests
{
    [Fact]
    public async Task MissingStoresLoadEmptyAndProfilesRoundTripOnlyNamesAndKeys()
    {
        using var fixture = new MainWindowFixture();
        var absent = await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(absent.IsAvailable);
        Assert.Empty(absent.Value);
        var profile = ExtensionProfile.Create("  Documents  ", [".TXT", "", ".txt", ".PDF"]);
        await fixture.Profiles.SaveAsync([profile], TestContext.Current.CancellationToken);
        var loaded = Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value);
        Assert.Equal("Documents", loaded.Name);
        Assert.Equal(["", ".pdf", ".txt"], loaded.ExtensionKeys);
        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(fixture.RootPath, "local-data", "profiles.json"), TestContext.Current.CancellationToken));
        Assert.Equal(["SchemaVersion", "Profiles"], json.RootElement.EnumerateObject().Select(property => property.Name));
        Assert.Equal(["Name", "ExtensionKeys"], json.RootElement.GetProperty("Profiles")[0].EnumerateObject().Select(property => property.Name));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":2,\"Profiles\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Profiles\":[null]}")]
    [InlineData("{\"SchemaVersion\":1,\"Profiles\":[{\"Name\":\"Invalid\",\"ExtensionKeys\":null}]}")]
    public async Task InvalidProfileStorageIsPreservedAndCannotBeOverwritten(string content)
    {
        using var fixture = new MainWindowFixture();
        var path = Path.Combine(fixture.RootPath, "profiles.json");
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        var store = new JsonProfileStore(path);
        var result = await store.LoadAsync(TestContext.Current.CancellationToken);
        Assert.False(result.IsAvailable);
        Assert.Empty(result.Value);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken));
        Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DuplicateNamesAreRejectedAndEmptyProfilesRoundTrip()
    {
        using var fixture = new MainWindowFixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Profiles.SaveAsync(
            [ExtensionProfile.Create("Docs", [".txt"]), ExtensionProfile.Create(" docs ", [".pdf"])], TestContext.Current.CancellationToken));
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Empty", [])], TestContext.Current.CancellationToken);
        Assert.Empty(Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value).ExtensionKeys);
        Assert.Throws<ArgumentException>(() => ExtensionProfile.Create(" ", [".txt"]));
        Assert.Throws<ArgumentException>(() => ExtensionProfile.Create("Bad", ["../file"]));
    }

    [Fact]
    public async Task ExclusionsAreRelativeRootSpecificAndStoreNoAbsolutePath()
    {
        using var fixture = new MainWindowFixture();
        var other = Path.Combine(fixture.RootPath, "other");
        Assert.True((await fixture.Exclusions.LoadAsync(fixture.RootPath, TestContext.Current.CancellationToken)).IsAvailable);
        await fixture.Exclusions.SaveAsync(fixture.RootPath, new FolderExclusionSet(["src\\obj", "src/obj/deep"]), TestContext.Current.CancellationToken);
        await fixture.Exclusions.SaveAsync(other, new FolderExclusionSet(["temp"]), TestContext.Current.CancellationToken);
        Assert.Equal(["src/obj"], (await fixture.Exclusions.LoadAsync(fixture.RootPath, TestContext.Current.CancellationToken)).Value.Paths);
        Assert.Equal(["temp"], (await fixture.Exclusions.LoadAsync(other, TestContext.Current.CancellationToken)).Value.Paths);
        var json = await File.ReadAllTextAsync(Path.Combine(fixture.RootPath, "local-data", "exclusions.json"), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(fixture.RootPath, json, StringComparison.Ordinal);
        Assert.Contains(JsonExclusionStore.GetRootKey(fixture.RootPath), json, StringComparison.Ordinal);
        Assert.Equal(JsonExclusionStore.GetRootKey(fixture.RootPath), JsonExclusionStore.GetRootKey(fixture.RootPath + Path.DirectorySeparatorChar));
        await fixture.Exclusions.SaveAsync(fixture.RootPath, new FolderExclusionSet([]), TestContext.Current.CancellationToken);
        Assert.Empty((await fixture.Exclusions.LoadAsync(fixture.RootPath, TestContext.Current.CancellationToken)).Value.Paths);
        Assert.Equal(["temp"], (await fixture.Exclusions.LoadAsync(other, TestContext.Current.CancellationToken)).Value.Paths);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"SchemaVersion\":2,\"Roots\":{}}")]
    [InlineData("{\"SchemaVersion\":1,\"Roots\":{\"invalid\":[\"temp\"]}}")]
    public async Task InvalidExclusionStorageIsPreserved(string content)
    {
        using var fixture = new MainWindowFixture();
        var path = Path.Combine(fixture.RootPath, "exclusions.json");
        await File.WriteAllTextAsync(path, content, TestContext.Current.CancellationToken);
        using var store = new JsonExclusionStore(path);
        Assert.False((await store.LoadAsync(fixture.RootPath, TestContext.Current.CancellationToken)).IsAvailable);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(fixture.RootPath, new FolderExclusionSet(["temp"]), TestContext.Current.CancellationToken));
        Assert.Equal(content, await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CancelledWritePreservesPreviousDataAndLeavesNoTemporaryFile()
    {
        using var fixture = new MainWindowFixture();
        await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Profiles.SaveAsync([ExtensionProfile.Create("Images", [".png"])], cancellation.Token));
        Assert.Equal("Docs", Assert.Single((await fixture.Profiles.LoadAsync(TestContext.Current.CancellationToken)).Value).Name);
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(fixture.RootPath, "local-data"), "*.tmp"));
    }

    [Fact]
    public async Task UnavailableStorageReportsFailure()
    {
        using var fixture = new MainWindowFixture();
        var blocker = Path.Combine(fixture.RootPath, "blocker");
        await File.WriteAllTextAsync(blocker, "file", TestContext.Current.CancellationToken);
        var store = new JsonProfileStore(blocker);
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], TestContext.Current.CancellationToken));
        Assert.False((await store.LoadAsync(TestContext.Current.CancellationToken)).IsAvailable);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizedWritePreservesReadablePreviousDataAndLeavesNoTemporaryFile(bool profiles)
    {
        using var fixture = new MainWindowFixture();
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = new string('a', 150);
        var values = Enumerable.Range(0, 30000).Select(index => $".item{index:D5}{suffix}");
        var directory = Path.Combine(fixture.RootPath, "local-data");
        var path = Path.Combine(directory, profiles ? "profiles.json" : "exclusions.json");
        if (profiles) await fixture.Profiles.SaveAsync([ExtensionProfile.Create("Docs", [".txt"])], cancellationToken);
        else await fixture.Exclusions.SaveAsync(fixture.RootPath, new FolderExclusionSet(["temp"]), cancellationToken);
        var previous = await File.ReadAllBytesAsync(path, cancellationToken);

        if (profiles)
        {
            await Assert.ThrowsAsync<IOException>(() => fixture.Profiles.SaveAsync(
                [ExtensionProfile.Create("Too large", values)], cancellationToken));
            Assert.True((await fixture.Profiles.LoadAsync(cancellationToken)).IsAvailable);
        }
        else
        {
            await Assert.ThrowsAsync<IOException>(() => fixture.Exclusions.SaveAsync(
                fixture.RootPath, new FolderExclusionSet(values), cancellationToken));
            Assert.True((await fixture.Exclusions.LoadAsync(fixture.RootPath, cancellationToken)).IsAvailable);
        }
        Assert.Equal(previous, await File.ReadAllBytesAsync(path, cancellationToken));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }
}
