using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Singularidi.Config;
using Singularidi.Midi;
using Singularidi.Services;
using Singularidi.ViewModels;
using Singularidi.Views;

namespace Singularidi;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var mainWindow = Services.GetRequiredService<MainWindow>();
            desktop.MainWindow = mainWindow;
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // Config
        services.AddSingleton<IConfigService, ConfigService>();
        services.AddSingleton(sp => sp.GetRequiredService<IConfigService>().Load());

        // Core
        services.AddSingleton<MidiPlaybackEngine>();

        // Services — DialogService needs the Window, which is resolved lazily
        services.AddSingleton<IDialogService>(sp =>
            new DialogService(() => sp.GetRequiredService<MainWindow>()));

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();

        // Views
        services.AddSingleton<MainWindow>();
    }
}
