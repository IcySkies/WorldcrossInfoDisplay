using System.Windows;
using System.Windows.Controls;

namespace WorldcrossInfoDisplay;

public partial class MainWindow : Window
{
    private MainViewModel ViewModel => (MainViewModel)DataContext;
    public MainWindow() { InitializeComponent(); Closed += async (_, _) => await ViewModel.ShutdownAsync(); }
    private async void ConnectObs_Click(object sender, RoutedEventArgs e) => await ViewModel.ToggleObsAsync();
    private async void RefreshObs_Click(object sender, RoutedEventArgs e) => await ViewModel.RefreshObsSourcesAsync();
    private async void ToggleVisibility_Click(object sender, RoutedEventArgs e) => await ViewModel.ToggleSelectedVisibilityAsync();
    private async void Apply_Click(object sender, RoutedEventArgs e) => await ViewModel.ApplyAsync();
    private async void TemplateChanged(object sender, RoutedEventArgs e) => await ViewModel.ApplyAsync();
    private void PasswordChanged(object sender, RoutedEventArgs e) => ViewModel.Settings.ObsPassword = ((PasswordBox)sender).Password;
}
