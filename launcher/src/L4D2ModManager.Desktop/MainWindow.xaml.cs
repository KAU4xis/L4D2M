using System.IO;
using System.Net.Http;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using L4D2ModManager.Infrastructure;

namespace L4D2ModManager.Desktop;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        var state = new LocalStateStore();
        var locator = new GameLocator();
        DataContext = new MainViewModel(
            new ManifestClient(http, new("https://github.com/KAU4xis/L4D2M/releases/latest/download/manifest.json")),
            state,
            new ModSynchronizer(http, state),
            locator,
            new LauncherSettingsStore(),
            http);
        Loaded += Window_Loaded;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= Window_Loaded;
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var backgroundTask = LoadBackgroundAsync();
        var initializationTask = ((MainViewModel)DataContext).InitializeAsync();
        await Task.WhenAll(backgroundTask, initializationTask);
    }

    private async Task LoadBackgroundAsync()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "background.png");
        if (!File.Exists(path)) return;

        var source = await Task.Run(() =>
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        });
        BackgroundImage.Source = source;
    }

    private async void ModItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem item || item.DataContext is not ModViewModel mod) return;
        item.IsSelected = true;
        if (DataContext is MainViewModel viewModel) await viewModel.SelectModAsync(mod);
    }

    private void SteamLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme != Uri.UriSchemeHttps ||
            !e.Uri.Host.Equals("steamcommunity.com", StringComparison.OrdinalIgnoreCase))
            return;

        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
