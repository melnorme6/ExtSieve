using Avalonia;

namespace ExtSieve.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var applicationMutex = OperatingSystem.IsWindows()
            ? new Mutex(initiallyOwned: false, @"Local\ExtSieve_E85F83F4_50BD_4B20_8B81_8D3793EAE130")
            : null;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
