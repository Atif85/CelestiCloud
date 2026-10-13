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
using CelestiCloud.GUI.Services;
using CelestiCloud.GUI.ViewModels;
using CelestiCloud.GUI.Views;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;

namespace CelestiCloud.GUI;

public partial class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    private static Mutex? _singleInstanceMutex;
    private readonly CancellationTokenSource _ipcServerCts = new();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Generate unique identifiers scoped to the specific OS user account
            string safeUser = string.Join("_", Environment.UserName.Split(Path.GetInvalidFileNameChars()));
            string mutexName = $"CelestiCloud_Mutex_{safeUser}";
            string pipeName = $"CelestiCloud_IPC_{safeUser}";

            bool isFirstInstance;
            try
            {
                _singleInstanceMutex = new Mutex(true, mutexName, out isFirstInstance);
            }
            catch (AbandonedMutexException)
            {
                // Previous instance crashed without properly releasing mutex
                isFirstInstance = true;
            }

            if (!isFirstInstance)
            {
                // An instance already exists for this user: Signal it to restore/focus
                SignalExistingInstance(pipeName);

                // Immediately exit this secondary duplicate instance
                Environment.Exit(0);
                return;
            }

            // We are the primary instance: Listen for future duplicate launch attempts
            StartIpcServer(pipeName);

            // Initialize the application normally
            InitializeNormalApp(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    // Sends an IPC message to the existing instance asking it to unminimize and focus.
    private static void SignalExistingInstance(string pipeName)
    {
        try
        {
            using var client = new NamedPipeClientStream(
                ".",
                pipeName,
                PipeDirection.Out,
                PipeOptions.CurrentUserOnly);

            // Wait up to 1 second for the server instance to respond
            client.Connect(1000);

            using var writer = new StreamWriter(client);
            writer.WriteLine("RESTORE");
            writer.Flush();
        }
        catch
        {
            // If the pipe connection fails (e.g., existing instance is terminating), ignore silently
        }
    }

    // Background server listening for restore signals from secondary launch attempts.
    private void StartIpcServer(string pipeName)
    {
        Task.Run(async () =>
        {
            while (!_ipcServerCts.Token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(_ipcServerCts.Token);

                    using var reader = new StreamReader(server);
                    var message = await reader.ReadLineAsync();

                    if (message == "RESTORE")
                    {
                        Dispatcher.UIThread.Post(() => _mainWindow?.RestoreAndFocus());
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    await Task.Delay(250);
                }
            }
        });
    }

    private void InitializeNormalApp(IClassicDesktopStyleApplicationLifetime desktop)
    {
        // Initialize Core Services
        var configManager = new ConfigManager();

        // Load Settings & Limiter
        var appSettings = configManager.LoadSettings();
        int uploadLimit = appSettings.GlobalUploadLimitKbps;
        RateLimiter? limiter = (uploadLimit > 0)
            ? BandwidthLimiterFactory.CreateLimiter(uploadLimit * 1024)
            : null;

        IJobLogger fileLogger = new FileLogger(configManager.GetAppDataPath(), "CelestiCloud_GUI");
        var uiLogger = new ObservableUiLogger(fileLogger);

        // Create the Global Job Engine 
        var providerFactory = new ProviderFactory(configManager, uiLogger);
        var jobEngine = new JobEngine(configManager, providerFactory, limiter, uiLogger);

        _ = jobEngine.StartAutoStartJobsAsync();

        _mainWindow = new MainWindow();

        var notificationService = new AvaloniaNotificationService(_mainWindow);

        jobEngine.NotificationRequested += (title, message, severity) =>
        {
            var level = severity.ToLower() switch
            {
                "success" => NotificationLevel.Success,
                "warning" => NotificationLevel.Warning,
                "error" => NotificationLevel.Error,
                _ => NotificationLevel.Info
            };
            notificationService.Show(title, message, level);
        };

        NetworkChange.NetworkAvailabilityChanged += (sender, e) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (e.IsAvailable)
                {
                    notificationService.Show(
                        "Network Restored",
                        "Connected to the internet. Resuming synchronization operations...",
                        NotificationLevel.Success);
                }
                else
                {
                    notificationService.Show(
                        "Network Lost",
                        "Connection lost. Pausing sync operations...",
                        NotificationLevel.Error);
                }
            });
        };

        _mainWindow.DataContext = new MainWindowViewModel(
            configManager,
            providerFactory,
            jobEngine,
            uiLogger,
            notificationService);

        CreateTrayIcon(_mainWindow, jobEngine);

        // Handle start minimized check (for startup folder launches)
        bool startMinimized = desktop.Args != null && desktop.Args.Contains("--minimized");
        if (startMinimized)
        {
            _mainWindow.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.Post(() => _mainWindow.Hide(), DispatcherPriority.ApplicationIdle);
        }
        else
        {
            _mainWindow.Show();
        }

        desktop.MainWindow = _mainWindow;

        desktop.Exit += async (sender, args) =>
        {
            _ipcServerCts.Cancel();
            _ipcServerCts.Dispose();
            _trayIcon?.Dispose();

            if (_singleInstanceMutex != null)
            {
                try
                {
                    _singleInstanceMutex.ReleaseMutex();
                }
                catch { /* Ignore thread affinity mismatches on forced close */ }
                _singleInstanceMutex.Dispose();
            }

            await jobEngine.StopAllAsync();
        };
    }

    private void CreateTrayIcon(MainWindow window, JobEngine engine)
    {
        _trayIcon = new TrayIcon
        {
            Icon = window.Icon,
            ToolTipText = "CelestiCloud"
        };

        var trayMenu = new NativeMenu();

        var showWindowItem = new NativeMenuItem("Show Window");
        showWindowItem.Click += (sender, args) => Dispatcher.UIThread.Post(() => window.RestoreAndFocus());

        var exitAppItem = new NativeMenuItem("Exit Application");
        exitAppItem.Click += async (sender, args) =>
        {
            _trayIcon?.Dispose();
            await engine.StopAllAsync();
            window.ForceCloseAndShutdown();
        };

        trayMenu.Add(showWindowItem);
        trayMenu.Add(new NativeMenuItemSeparator());
        trayMenu.Add(exitAppItem);

        _trayIcon.Menu = trayMenu;

        // Double-clicking the tray icon restores the window
        _trayIcon.Clicked += (sender, args) => Dispatcher.UIThread.Post(() => window.RestoreAndFocus());

        TrayIcon.SetIcons(this, [_trayIcon]);
    }
}