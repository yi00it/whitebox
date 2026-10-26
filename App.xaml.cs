using System.Windows;
using System.Windows.Threading;
using Whitebox.Core;

namespace Whitebox;

public partial class App : Application
{
    public const string ProjectUrl = "https://github.com/yi00it/whitebox";

    public static string Version { get; } =
        typeof(App).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "dev";

    // Developer switches: --page <key>  --shot <file.png>  --scan [ms]  --demo (placeholder user/machine names)  --theme light|dark
    public static string? StartPage { get; private set; }
    public static string? ScreenshotPath { get; private set; }
    public static bool RunScan { get; private set; }
    public static int ScanWaitMs { get; private set; } = 8000;

    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        var args = e.Args;
        string? theme = null;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--page" when i + 1 < args.Length: StartPage = args[++i]; break;
                case "--shot" when i + 1 < args.Length: ScreenshotPath = args[++i]; break;
                case "--demo": Services.SystemInfo.DemoMode = true; break;
                case "--theme" when i + 1 < args.Length: theme = args[++i]; break;
                case "--scan":
                    RunScan = true;
                    if (i + 1 < args.Length && int.TryParse(args[i + 1], out var ms)) { ScanWaitMs = ms; i++; }
                    break;
            }
        }
        ThemeManager.Initialize(theme);
        base.OnStartup(e);
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Notifier.Show("Something went wrong", e.Exception.Message, ToastKind.Error);
        e.Handled = true;
    }
}
