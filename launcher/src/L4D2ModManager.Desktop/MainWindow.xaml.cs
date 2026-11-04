using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media.Imaging;
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
            new LauncherSettingsStore());
        LoadBackground();
        Loaded += async (_, _) => await ((MainViewModel)DataContext).InitializeAsync();
    }

    private void LoadBackground()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "background.jpg");
        if (File.Exists(path)) BackgroundImage.Source = new BitmapImage(new Uri(path));
    }
}
