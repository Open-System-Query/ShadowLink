using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;

namespace ShadowLink;

internal static class Program
{
    [STAThread]
    public static void Main(String[] args)
    {
        Trace.AutoFlush = true;
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            Trace.TraceError("Unhandled exception: {0}", eventArgs.ExceptionObject);
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            Trace.TraceError("Unobserved task exception: {0}", eventArgs.Exception);
            eventArgs.SetObserved();
        };

        if (StartupControlElevation.TryHandleStartupElevation(args))
        {
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        IconProvider.Current.Register<FontAwesomeIconProvider>();

        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
