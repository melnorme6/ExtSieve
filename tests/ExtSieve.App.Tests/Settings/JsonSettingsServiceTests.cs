using ExtSieve.App.Settings;

namespace ExtSieve.App.Tests.Settings;

public sealed class JsonSettingsServiceTests
{
    [Fact]
    public async Task MissingFileReturnsSystemDefaults()
    {
        using var fixture = new SettingsFixture();
        var service = new JsonSettingsService(fixture.SettingsPath);

        var settings = await service.LoadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(LanguageMode.System, settings.Language);
        Assert.Equal(ThemeMode.System, settings.Theme);
    }

    [Theory]
    [InlineData(LanguageMode.English)]
    [InlineData(LanguageMode.Italian)]
    [InlineData(LanguageMode.German)]
    [InlineData(LanguageMode.French)]
    [InlineData(LanguageMode.Spanish)]
    [InlineData(LanguageMode.PortugueseBrazil)]
    public async Task SavedPreferencesRoundTripWithoutSecrets(LanguageMode language)
    {
        using var fixture = new SettingsFixture();
        var service = new JsonSettingsService(fixture.SettingsPath);
        var expected = new AppSettings
        {
            Language = language,
            Theme = ThemeMode.Dark,
        };

        var cancellationToken = TestContext.Current.CancellationToken;
        await service.SaveAsync(expected, cancellationToken);
        var actual = await service.LoadAsync(cancellationToken);
        var persistedText = await File.ReadAllTextAsync(fixture.SettingsPath, cancellationToken);

        Assert.Equal(expected, actual);
        Assert.DoesNotContain("password", persistedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InvalidJsonReturnsSystemDefaults()
    {
        using var fixture = new SettingsFixture();
        Directory.CreateDirectory(Path.GetDirectoryName(fixture.SettingsPath)!);
        var cancellationToken = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(fixture.SettingsPath, "{ invalid", cancellationToken);
        var service = new JsonSettingsService(fixture.SettingsPath);

        var settings = await service.LoadAsync(cancellationToken);

        Assert.Equal(new AppSettings(), settings);
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(
            Path.GetTempPath(),
            $"ExtSieve.Tests.{Guid.NewGuid():N}");

        public string SettingsPath => Path.Combine(_directory, "settings.json");

        public void Dispose()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
    }
}
