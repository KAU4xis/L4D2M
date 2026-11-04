using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using L4D2ModManager.Core;
using Microsoft.Win32;

namespace L4D2ModManager.Desktop;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IManifestClient manifestClient;
    private readonly ILocalStateStore stateStore;
    private readonly IModSynchronizer synchronizer;
    private readonly IGameLocator gameLocator;
    private readonly ILauncherSettingsStore settingsStore;
    private RemoteManifest? manifest;
    private SyncPlan? plan;
    private string? gamePath;
    private bool busy;
    private string status = "Pronto para verificar";
    private string installedVersion = "—";
    private string availableVersion = "—";
    private string planSummary = "Selecione a pasta do jogo e verifique as atualizações.";
    private double progressPercent;

    public MainViewModel(IManifestClient manifestClient, ILocalStateStore stateStore, IModSynchronizer synchronizer, IGameLocator gameLocator, ILauncherSettingsStore settingsStore)
    {
        this.manifestClient = manifestClient;
        this.stateStore = stateStore;
        this.synchronizer = synchronizer;
        this.gameLocator = gameLocator;
        this.settingsStore = settingsStore;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !Busy && GamePath is not null);
        UpdateCommand = new AsyncCommand(UpdateAsync, () => !Busy && plan?.HasChanges == true);
        SelectPathCommand = new AsyncCommand(SelectPathAsync, () => !Busy);
    }

    public ObservableCollection<ModViewModel> Mods { get; } = [];
    public ICommand RefreshCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand SelectPathCommand { get; }
    public string? GamePath { get => gamePath; private set { Set(ref gamePath, value); NotifyCommands(); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public string InstalledVersion { get => installedVersion; private set => Set(ref installedVersion, value); }
    public string AvailableVersion { get => availableVersion; private set => Set(ref availableVersion, value); }
    public string PlanSummary { get => planSummary; private set => Set(ref planSummary, value); }
    public double ProgressPercent { get => progressPercent; private set => Set(ref progressPercent, value); }
    public int ModCount => Mods.Count;
    public string Footer => "Somente arquivos registrados em .l4d2-modpack/state.json são removidos pelo launcher.";
    public bool Busy { get => busy; private set { Set(ref busy, value); NotifyCommands(); } }
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        GamePath = gameLocator.FindFromLauncherDirectory(AppContext.BaseDirectory);
        if (GamePath is null)
        {
            var saved = await settingsStore.LoadAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(saved.GamePath) && Directory.Exists(Path.Combine(saved.GamePath, "left4dead2", "addons")))
                GamePath = saved.GamePath;
        }
        if (GamePath is null) Status = "Selecione a pasta do Left 4 Dead 2";
        else await RefreshAsync();
    }

    private async Task SelectPathAsync()
    {
        var dialog = new OpenFolderDialog { Title = "Selecione a raiz do Left 4 Dead 2", Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        try
        {
            GamePath = gameLocator.NormalizeSelectedPath(dialog.FolderName);
            await settingsStore.SaveAsync(new(GamePath), CancellationToken.None);
            await RefreshAsync();
        }
        catch (Exception exception) { ShowError(exception); }
    }

    private async Task RefreshAsync()
    {
        if (GamePath is null) return;
        await Run(async () =>
        {
            Status = "Consultando manifesto…";
            manifest = await manifestClient.GetLatestAsync(CancellationToken.None);
            var addons = AddonsPath();
            var local = await stateStore.LoadAsync(addons, CancellationToken.None);
            plan = await synchronizer.PlanAsync(manifest, addons, CancellationToken.None);
            InstalledVersion = local.InstalledVersion ?? "Não instalado";
            AvailableVersion = manifest.Latest;
            Mods.Clear();
            foreach (var mod in manifest.Mods) Mods.Add(new(mod));
            OnPropertyChanged(nameof(ModCount));
            PlanSummary = $"{plan.Downloads.Count} download(s), {plan.Removals.Count} remoção(ões), {FormatSize(plan.DownloadBytes)} para baixar.";
            Status = plan.HasChanges ? "Atualização disponível" : "Modpack sincronizado";
        });
    }

    private async Task UpdateAsync()
    {
        if (manifest is null || plan is null || GamePath is null) return;
        var answer = MessageBox.Show($"Aplicar a versão {manifest.Latest}?\n\n{PlanSummary}", "Confirmar atualização", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        await Run(async () =>
        {
            var progress = new Progress<SyncProgress>(item =>
            {
                Status = item.Filename is null ? item.Stage : $"{item.Stage}: {item.Filename}";
                ProgressPercent = item.TotalBytes == 0 ? 0 : item.CompletedBytes * 100d / item.TotalBytes;
            });
            await synchronizer.ApplyAsync(manifest, AddonsPath(), plan, progress, CancellationToken.None);
            ProgressPercent = 100;
            await RefreshAsync();
            MessageBox.Show("Modpack atualizado com sucesso!", "L4D2 Modpack Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        });
    }

    private async Task Run(Func<Task> action)
    {
        if (Busy) return;
        Busy = true;
        try { await action(); }
        catch (Exception exception) { ShowError(exception); }
        finally { Busy = false; }
    }

    private string AddonsPath() => Path.Combine(GamePath!, "left4dead2", "addons");
    private void ShowError(Exception exception) { Status = "Falha na operação"; MessageBox.Show(exception.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error); }
    private static string FormatSize(long value) => value >= 1_048_576 ? $"{value / 1_048_576d:F2} MiB" : $"{value / 1024d:F1} KiB";
    private void NotifyCommands() { (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged(); (UpdateCommand as AsyncCommand)?.RaiseCanExecuteChanged(); (SelectPathCommand as AsyncCommand)?.RaiseCanExecuteChanged(); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
}

public sealed class ModViewModel(RemoteMod mod)
{
    public string Id => mod.Id;
    public string Name => mod.Name;
    public string Description => string.IsNullOrWhiteSpace(mod.Description) ? "Sem descrição." : mod.Description.Replace("\r", " ").Replace("\n", " ");
    public string? PreviewUrl => mod.PreviewUrl;
    public string DisplaySize => mod.TotalSize >= 1_048_576 ? $"{mod.TotalSize / 1_048_576d:F2} MiB" : $"{mod.TotalSize / 1024d:F1} KiB";
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await execute(); }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
