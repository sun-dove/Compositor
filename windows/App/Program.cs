using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Velopack;

namespace Compositor.Windows;

internal static class Program {
    internal static string[] Arguments = [];
    [STAThread]
    public static int Main(string[] args) {
        Arguments = args;
        // Installing a cached package at startup would bypass our signature verification.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var exit = AppBuilder.Configure<CompositorApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
        return exit == 0 ? Environment.ExitCode : exit;
    }
}

public sealed class CompositorApp : Application {
    public override void Initialize() { RequestedThemeVariant = ThemeVariant.Dark; Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted() {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime) lifetime.MainWindow = new EditorWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
