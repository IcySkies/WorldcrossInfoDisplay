using System.Windows;

namespace WorldcrossInfoDisplay;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var persistence = new SettingsStore();
        var settings = await persistence.LoadAsync();
        var relay = new RelayEventSource(settings.GamePath, settings.RelayExecutablePath);
        var obs = new ObsTextPublisher();
        var vm = new MainViewModel(settings, persistence, relay, obs);
        var window = new MainWindow { DataContext = vm };
        MainWindow = window;
        window.Show();
        await vm.InitializeAsync();
    }
}
