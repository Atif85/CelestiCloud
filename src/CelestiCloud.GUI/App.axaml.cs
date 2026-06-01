using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Providers;
using CelestiCloud.GUI.Logging;
using CelestiCloud.GUI.ViewModels;
using CelestiCloud.GUI.Views;
using System;
using System.Threading.RateLimiting;

namespace CelestiCloud.GUI;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Initialize Core Services
            var configManager = new ConfigManager();
            var providerFactory = new ProviderFactory(configManager);

            // Load Settings & Limiter
            var appSettings = configManager.LoadSettings();
            int uploadLimit = appSettings.GlobalUploadLimitKbps;
            RateLimiter? limiter = (uploadLimit > 0)
                ? BandwidthLimiterFactory.CreateLimiter(uploadLimit * 1024)
                : null;

            IJobLogger fileLogger = new FileLogger(configManager.GetAppDataPath(), "CelestiCloud_GUI");

            var uiLogger = new ObservableUiLogger(fileLogger);

            // Create the Global Job Engine 
            var jobEngine = new JobEngine(configManager, providerFactory, limiter, uiLogger);

            _ = jobEngine.StartAutoStartJobsAsync();

            _mainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(configManager, providerFactory , jobEngine, uiLogger),
            };

            CreateTrayIcon(_mainWindow, jobEngine);

            // Handle start minimized check (for startup folder launches)
            bool startMinimized = desktop.Args != null && desktop.Args.Contains("--minimized");
            if (startMinimized)
            {
                _mainWindow.WindowState = WindowState.Minimized;
                // Wait for the window to draw, then immediately hide it to tray
                Dispatcher.UIThread.Post(() => _mainWindow.Hide(), DispatcherPriority.ApplicationIdle);
            }
            else
            {
                _mainWindow.Show();
            }

            desktop.MainWindow = _mainWindow;

            desktop.Exit += async (sender, args) =>
            {
                _trayIcon?.Dispose(); // Safely removes the icon from the system taskbar on exit
                await jobEngine.StopAllAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void CreateTrayIcon(MainWindow window, JobEngine engine)
    {
        _trayIcon = new TrayIcon
        {
            Icon = window.Icon,
            ToolTipText = "CelestiCloud"
        };

        // Create System Tray Context Menu
        var trayMenu = new NativeMenu();

        var showWindowItem = new NativeMenuItem("Show Window");
        showWindowItem.Click += (sender, args) => Dispatcher.UIThread.Post(() =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });

        var exitAppItem = new NativeMenuItem("Exit Application");
        exitAppItem.Click += async (sender, args) =>
        {
            _trayIcon?.Dispose(); // Disposes the tray icon
            await engine.StopAllAsync();
            window.ForceCloseAndShutdown(); // Triggers native shut down
        };

        trayMenu.Add(showWindowItem);
        trayMenu.Add(new NativeMenuItemSeparator());
        trayMenu.Add(exitAppItem);

        _trayIcon.Menu = trayMenu;

        // Double-clicking the tray icon restores the window
        _trayIcon.Clicked += (sender, args) => Dispatcher.UIThread.Post(() =>
        {
            window.Show();
            window.WindowState = WindowState.Normal;
            window.Activate();
        });

        TrayIcon.SetIcons(this, [_trayIcon]);
    }
}