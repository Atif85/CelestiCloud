using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.IO;
using CelestiCloud.Core.Jobs;
using CelestiCloud.Core.Logging;
using CelestiCloud.Core.Providers;
using CelestiCloud.GUI.ViewModels;
using CelestiCloud.GUI.Views;
using System.Threading.RateLimiting;

namespace CelestiCloud.GUI;

public partial class App : Application
{
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

            IJobLogger logger = new FileLogger(configManager.GetAppDataPath(), "CelestiCloud_GUI");

            // Create the Global Sync Engine 
            var syncEngine = new JobEngine(configManager, providerFactory, limiter, logger);

            //_ = syncEngine.StartAutoStartJobsAsync();

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(configManager, providerFactory , syncEngine),
            };

            desktop.Exit += async (sender, args) =>
            {
                await syncEngine.StopAllAsync();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}