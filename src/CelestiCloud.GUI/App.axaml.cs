using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CelestiCloud.Core.Config;
using CelestiCloud.Core.Providers;
using CelestiCloud.GUI.ViewModels;
using CelestiCloud.GUI.Views;

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
            var configManager = new ConfigManager();
            var providerFactory = new ProviderFactory(configManager);

            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(configManager, providerFactory),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}