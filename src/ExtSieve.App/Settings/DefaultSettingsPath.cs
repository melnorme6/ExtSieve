namespace ExtSieve.App.Settings;

public static class DefaultSettingsPath
{
    public static string Get()
    {
        var applicationData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        return Path.Combine(applicationData, "ExtSieve", "settings.json");
    }
}
