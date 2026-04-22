using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShadowLink.Application.ViewModels;
using ShadowLink.Localization;
using ShadowLink.Services;

namespace ShadowLink;

public partial class App : Avalonia.Application
{
    private CompositionRoot? _compositionRoot;
    private MainWindowViewModel? _mainWindowViewModel;
    private Boolean _isInitializationStarted;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            ShadowLinkText.Initialize(new GetTextLocalizationService());
            _compositionRoot = new CompositionRoot();
            _mainWindowViewModel = _compositionRoot.CreateMainWindowViewModel();
            MainWindow mainWindow = new MainWindow
            {
                DataContext = _mainWindowViewModel
            };
            mainWindow.Icon = AppWindowIconLoader.Load();

            desktop.MainWindow = mainWindow;
            desktop.Exit += HandleDesktopExit;
            mainWindow.Opened += HandleMainWindowOpened;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private async void HandleMainWindowOpened(Object? sender, EventArgs e)
    {
        if (_isInitializationStarted || _mainWindowViewModel is null || sender is not MainWindow mainWindow)
        {
            return;
        }

        _isInitializationStarted = true;
        mainWindow.Opened -= HandleMainWindowOpened;
        try
        {
            await _mainWindowViewModel.InitializeAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            Trace.TraceError("Application initialization failed: {0}", ex);
        }
    }

    private async void HandleDesktopExit(Object? sender, ControlledApplicationLifetimeExitEventArgs e)
    {
        if (_compositionRoot is not null)
        {
            try
            {
                await _compositionRoot.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Trace.TraceError("Application shutdown failed: {0}", ex);
            }
        }
    }
}
