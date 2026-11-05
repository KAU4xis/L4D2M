using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Diagnostics;
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
    private readonly HttpClient httpClient;
    private RemoteManifest? manifest;
    private SyncPlan? plan;
    private string? gamePath;
    private bool busy;
    private bool isInterfaceHidden;
    private ModViewModel? selectedMod;
    private ModViewModel? selectedRequiredMod;
    private ModViewModel? selectedLocalMod;
    private string status = "Pronto para verificar";
    private string installedVersion = "—";
    private string availableVersion = "—";
    private string planSummary = "Selecione a pasta do jogo e verifique as atualizações.";
    private double progressPercent;
    private double currentFilePercent;
    private string currentDownloadName = "Aguardando download";
    private string currentFileProgress = "—";
    private string totalDownloadProgress = "—";
    private string downloadElapsed = "00:00";
    private string downloadEta = "—";
    private string downloadPosition = "0 / 0";

    private bool isRateLimitPaused;
    private string rateLimitMessage = string.Empty;
    private int rateLimitSecondsRemaining;
    private string rateLimitResetText = "—";

    public MainViewModel(IManifestClient manifestClient, ILocalStateStore stateStore, IModSynchronizer synchronizer,
        IGameLocator gameLocator, ILauncherSettingsStore settingsStore, HttpClient httpClient)
    {
        this.manifestClient = manifestClient;
        this.stateStore = stateStore;
        this.synchronizer = synchronizer;
        this.gameLocator = gameLocator;
        this.settingsStore = settingsStore;
        this.httpClient = httpClient;
        RefreshCommand = new AsyncCommand(RefreshAsync, () => !Busy && GamePath is not null);
        UpdateCommand = new AsyncCommand(UpdateAsync, () => !Busy && plan?.HasChanges == true);
        SelectPathCommand = new AsyncCommand(SelectPathAsync, () => !Busy);
        ToggleInterfaceCommand = new ActionCommand(() => IsInterfaceHidden = !IsInterfaceHidden);
        DeleteLocalModCommand = new AsyncParameterCommand<ModViewModel>(DeleteLocalModAsync,
            mod => !Busy && mod?.CanDelete == true);
        DeleteAllLocalModsCommand = new AsyncCommand(DeleteAllLocalModsAsync,
            () => !Busy && LocalMods.Any(mod => mod.CanDelete));
    }

    public ObservableCollection<ModViewModel> RequiredMods { get; } = [];
    public ObservableCollection<ModViewModel> LocalMods { get; } = [];
    public ModViewModel? SelectedMod { get => selectedMod; private set => Set(ref selectedMod, value); }
    public ModViewModel? SelectedRequiredMod
    {
        get => selectedRequiredMod;
        set
        {
            if (ReferenceEquals(selectedRequiredMod, value)) return;
            selectedRequiredMod = value;
            OnPropertyChanged();
            if (value is not null) SelectedMod = value;
        }
    }
    public ModViewModel? SelectedLocalMod
    {
        get => selectedLocalMod;
        set
        {
            if (ReferenceEquals(selectedLocalMod, value)) return;
            selectedLocalMod = value;
            OnPropertyChanged();
            if (value is not null) SelectedMod = value;
        }
    }
    public ICommand RefreshCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand SelectPathCommand { get; }
    public ICommand ToggleInterfaceCommand { get; }
    public ICommand DeleteLocalModCommand { get; }
    public ICommand DeleteAllLocalModsCommand { get; }
    public string? GamePath { get => gamePath; private set { Set(ref gamePath, value); NotifyCommands(); } }
    public string Status { get => status; private set => Set(ref status, value); }
    public string InstalledVersion { get => installedVersion; private set => Set(ref installedVersion, value); }
    public string AvailableVersion { get => availableVersion; private set => Set(ref availableVersion, value); }
    public string PlanSummary { get => planSummary; private set => Set(ref planSummary, value); }
    public double ProgressPercent { get => progressPercent; private set => Set(ref progressPercent, value); }
    public double CurrentFilePercent { get => currentFilePercent; private set => Set(ref currentFilePercent, value); }
    public string CurrentDownloadName { get => currentDownloadName; private set => Set(ref currentDownloadName, value); }
    public string CurrentFileProgress { get => currentFileProgress; private set => Set(ref currentFileProgress, value); }
    public string TotalDownloadProgress { get => totalDownloadProgress; private set => Set(ref totalDownloadProgress, value); }
    public string DownloadElapsed { get => downloadElapsed; private set => Set(ref downloadElapsed, value); }
    public string DownloadEta { get => downloadEta; private set => Set(ref downloadEta, value); }
    public string DownloadPosition { get => downloadPosition; private set => Set(ref downloadPosition, value); }
    public int ModCount => RequiredMods.Count;
    public string RequiredTabTitle => $"NECESSÁRIOS ({RequiredMods.Count})";
    public string LocalTabTitle => $"LOCAIS ({LocalMods.Count})";
    public string Footer => "A sincronização automática só remove arquivos do state.json; mods locais só são apagados pela lixeira.";
    public bool IsRateLimitPaused { get => isRateLimitPaused; private set => Set(ref isRateLimitPaused, value); }
    public string RateLimitMessage { get => rateLimitMessage; private set => Set(ref rateLimitMessage, value); }
    public int RateLimitSecondsRemaining { get => rateLimitSecondsRemaining; private set => Set(ref rateLimitSecondsRemaining, value); }
    public string RateLimitResetText { get => rateLimitResetText; private set => Set(ref rateLimitResetText, value); }
    public string RateLimitHelp => "GitHub (não autenticado): API REST = 60 requisições/hora por IP; downloads em github.com podem sofrer limitação/abuso (429/403, Retry-After/x-ratelimit-reset quando presentes). O launcher aguarda e continua de onde parou.";
    public bool Busy { get => busy; private set { Set(ref busy, value); NotifyCommands(); } }
    public bool IsInterfaceHidden { get => isInterfaceHidden; private set => Set(ref isInterfaceHidden, value); }
    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task InitializeAsync()
    {
        GamePath = await Task.Run(() => gameLocator.FindFromLauncherDirectory(AppContext.BaseDirectory));
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
            local = await EnrichLocalStateAsync(local, manifest, addons);
            plan = await Task.Run(() => SyncPlanner.Create(manifest, local));
            var catalog = await Task.Run(() => BuildModCatalog(manifest, local, plan, addons));
            InstalledVersion = local.InstalledVersion ?? "Não instalado";
            AvailableVersion = manifest.Latest;
            ApplyModCatalog(catalog);
            await PrepareDescriptionImagesAsync(SelectedMod);
            OnPropertyChanged(nameof(ModCount));
            OnPropertyChanged(nameof(RequiredTabTitle));
            OnPropertyChanged(nameof(LocalTabTitle));
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
            ProgressPercent = 0;
            CurrentFilePercent = 0;
            CurrentDownloadName = "Preparando downloads…";
            CurrentFileProgress = "0 B";
            TotalDownloadProgress = $"0 B / {FormatSize(plan.DownloadBytes)}";
            DownloadElapsed = "00:00";
            DownloadEta = "Calculando…";
            var totalDownloadMods = plan.Downloads.Select(item => item.Mod.Id)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            DownloadPosition = $"0 / {totalDownloadMods}";
            var timer = Stopwatch.StartNew();
            var progress = new Progress<SyncProgress>(item =>
            {
                Status = item.Stage;
                ProgressPercent = item.TotalBytes == 0 ? 0 : item.CompletedBytes * 100d / item.TotalBytes;
                CurrentFilePercent = item.CurrentFileTotalBytes == 0 ? 0 :
                    item.CurrentFileBytes * 100d / item.CurrentFileTotalBytes;
                CurrentDownloadName = item.Filename ?? "Finalizando…";
                CurrentFileProgress = $"{FormatSize(item.CurrentFileBytes)} / {FormatSize(item.CurrentFileTotalBytes)} · {CurrentFilePercent:F0}%";
                TotalDownloadProgress = $"{FormatSize(item.CompletedBytes)} / {FormatSize(item.TotalBytes)} · {ProgressPercent:F0}%";
                DownloadElapsed = FormatDuration(timer.Elapsed);
                DownloadPosition = $"{item.CompletedFiles} / {item.TotalFiles}";
                if (item.CompletedBytes > 0 && item.TotalBytes > item.CompletedBytes)
                {
                    var bytesPerSecond = item.CompletedBytes / Math.Max(timer.Elapsed.TotalSeconds, 0.1);
                    DownloadEta = bytesPerSecond <= 0 ? "Calculando…" :
                        FormatDuration(TimeSpan.FromSeconds((item.TotalBytes - item.CompletedBytes) / bytesPerSecond));
                }
                else DownloadEta = item.TotalBytes > 0 && item.CompletedBytes >= item.TotalBytes ? "Concluído" : "Calculando…";
            });
            await synchronizer.ApplyAsync(manifest, AddonsPath(), plan, progress, WaitForRateLimitAsync, CancellationToken.None);
            ProgressPercent = 100;
            CurrentFilePercent = 100;
            DownloadElapsed = FormatDuration(timer.Elapsed);
            DownloadEta = "Concluído";
            DownloadPosition = $"{totalDownloadMods} / {totalDownloadMods}";
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

    private async Task<LocalState> EnrichLocalStateAsync(LocalState local, RemoteManifest remote, string addonsPath)
    {
        if (local.Files.Count == 0) return local;

        var installedIds = local.Files.Select(file => file.ModId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metadata = local.InstalledMods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var mod in remote.Mods.Where(mod => installedIds.Contains(mod.Id)))
            metadata[mod.Id] = new(mod.Id, mod.Name, mod.Description, mod.PreviewUrl, mod.WorkshopUrl);

        var enriched = local with { SchemaVersion = 2, Mods = metadata.Values.OrderBy(mod => mod.Id).ToArray() };
        if (local.SchemaVersion < 2 || local.Mods is null || !local.InstalledMods.SequenceEqual(enriched.InstalledMods))
        {
            try
            {
                await stateStore.SaveAsync(addonsPath, enriched, CancellationToken.None);
            }
            catch (UnauthorizedAccessException)
            {
                // Metadata migration is optional during refresh. Keep using the
                // enriched data in memory when the game folder is read-only.
            }
            catch (IOException)
            {
                // A locked/read-only state file must not prevent the catalog
                // from loading. A later successful update persists schema 2.
            }
        }
        return enriched;
    }

    private static ModCatalog BuildModCatalog(RemoteManifest remote, LocalState local, SyncPlan syncPlan, string addonsPath)
    {
        var requiredMods = new List<ModViewModel>();
        var localMods = new List<ModViewModel>();

        var localFilesByMod = local.Files.GroupBy(file => file.ModId)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var downloads = syncPlan.Downloads.Select(item => item.Mod.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var mod in remote.Mods)
        {
            var isLocal = localFilesByMod.ContainsKey(mod.Id);
            var state = downloads.Contains(mod.Id) ? (isLocal ? "Atualizar" : "Baixar") : "Instalado";
            requiredMods.Add(ModViewModel.FromRemote(mod, state));
        }

        var savedMetadata = local.InstalledMods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        var remoteMetadata = remote.Mods.ToDictionary(mod => mod.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var (modId, files) in localFilesByMod.OrderBy(item => item.Key))
        {
            savedMetadata.TryGetValue(modId, out var saved);
            remoteMetadata.TryGetValue(modId, out var current);
            var missing = files.Any(file => !File.Exists(SafePath.ManagedFile(addonsPath, file.Filename)));
            var changed = !missing && files.Any(file => new FileInfo(SafePath.ManagedFile(addonsPath, file.Filename)).Length != file.Size);
            var state = missing ? "Arquivo ausente" : changed ? "Tamanho alterado" : "Instalado";
            localMods.Add(ModViewModel.FromLocal(modId, files, saved, current, state));
        }

        foreach (var file in LocalModScanner.FindUnmanaged(addonsPath, local))
            localMods.Add(ModViewModel.FromUnmanaged(file));

        return new(requiredMods, localMods);
    }

    private void ApplyModCatalog(ModCatalog catalog)
    {
        RequiredMods.Clear();
        foreach (var mod in catalog.Required) RequiredMods.Add(mod);
        LocalMods.Clear();
        foreach (var mod in catalog.Local) LocalMods.Add(mod);

        SelectedRequiredMod = RequiredMods.FirstOrDefault();
        SelectedLocalMod = null;
        SelectedMod = SelectedRequiredMod ?? LocalMods.FirstOrDefault();
    }

    public async Task SelectModAsync(ModViewModel mod)
    {
        if (RequiredMods.Contains(mod))
        {
            SelectedLocalMod = null;
            SelectedRequiredMod = mod;
        }
        else if (LocalMods.Contains(mod))
        {
            SelectedRequiredMod = null;
            SelectedLocalMod = mod;
        }
        SelectedMod = mod;
        await PrepareDescriptionImagesAsync(mod);
    }

    private async Task PrepareDescriptionImagesAsync(ModViewModel? mod)
    {
        if (mod is null) return;
        var cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "L4D2ModManager", "markdown-images");
        await mod.CacheRemoteImagesAsync(httpClient, cache, CancellationToken.None);
    }

    private async Task DeleteLocalModAsync(ModViewModel mod)
    {
        if (!mod.CanDelete || string.IsNullOrWhiteSpace(mod.LocalFilePath)) return;
        var answer = MessageBox.Show($"Apagar o mod local?\n\n{Path.GetFileName(mod.LocalFilePath)}",
            "Excluir mod local", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        await Run(async () =>
        {
            await Task.Run(() => File.Delete(mod.LocalFilePath));
            LocalMods.Remove(mod);
            if (ReferenceEquals(SelectedMod, mod))
            {
                SelectedLocalMod = null;
                SelectedMod = SelectedRequiredMod ?? LocalMods.FirstOrDefault();
            }
            OnPropertyChanged(nameof(LocalTabTitle));
            await Task.CompletedTask;
        });
    }

    private async Task DeleteAllLocalModsAsync()
    {
        var targets = LocalMods.Where(mod => mod.CanDelete && !string.IsNullOrWhiteSpace(mod.LocalFilePath)).ToArray();
        if (targets.Length == 0) return;

        var answer = MessageBox.Show(
            $"Apagar todos os {targets.Length} mods locais que não vieram do Git?\n\nOs mods gerenciados pelo modpack serão preservados.",
            "Excluir todos os mods locais", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return;

        await Run(async () =>
        {
            var result = await Task.Run(() =>
            {
                var deleted = new List<ModViewModel>();
                var failures = new List<string>();
                foreach (var mod in targets)
                {
                    try
                    {
                        File.Delete(mod.LocalFilePath!);
                        deleted.Add(mod);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        failures.Add($"{Path.GetFileName(mod.LocalFilePath)}: {exception.Message}");
                    }
                }
                return (Deleted: deleted, Failures: failures);
            });

            foreach (var mod in result.Deleted) LocalMods.Remove(mod);
            if (SelectedMod?.CanDelete == true && !LocalMods.Contains(SelectedMod))
            {
                SelectedLocalMod = null;
                SelectedMod = SelectedRequiredMod ?? LocalMods.FirstOrDefault();
            }
            OnPropertyChanged(nameof(LocalTabTitle));
            Status = $"{result.Deleted.Count} mod(s) local(is) apagado(s)";

            if (result.Failures.Count > 0)
                MessageBox.Show(string.Join("\n", result.Failures), "Alguns mods não foram apagados",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
        });
    }

    private void ShowError(Exception exception) { Status = "Falha na operação"; MessageBox.Show(exception.Message, "Erro", MessageBoxButton.OK, MessageBoxImage.Error); }

    private async Task<bool> WaitForRateLimitAsync(RateLimitPause pause, CancellationToken cancellationToken)
    {
        var wait = pause.Wait < TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : pause.Wait;
        if (wait > TimeSpan.FromMinutes(65)) wait = TimeSpan.FromMinutes(65);
        var until = DateTimeOffset.UtcNow + wait;
        var dispatcher = Application.Current?.Dispatcher;

        void Set(Action action)
        {
            if (dispatcher is null || dispatcher.CheckAccess()) action();
            else dispatcher.Invoke(action);
        }

        Set(() =>
        {
            IsRateLimitPaused = true;
            RateLimitMessage = pause.Message;
            RateLimitResetText = pause.ResetAt?.ToLocalTime().ToString("HH:mm:ss") ?? "—";
            RateLimitSecondsRemaining = (int)Math.Ceiling(wait.TotalSeconds);
            Status = "Aguardando liberação do limite do GitHub…";
            CurrentDownloadName = "Download pausado (rate limit)";
            DownloadEta = RateLimitResetText;
        });

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var remaining = until - DateTimeOffset.UtcNow;
                var seconds = Math.Max(0, (int)Math.Ceiling(remaining.TotalSeconds));
                Set(() => RateLimitSecondsRemaining = seconds);
                if (seconds <= 0) return true;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        finally
        {
            Set(() => IsRateLimitPaused = false);
        }
    }
    private static string FormatSize(long value) => value >= 1_048_576 ? $"{value / 1_048_576d:F2} MiB" : $"{value / 1024d:F1} KiB";
    private static string FormatDuration(TimeSpan value)
        => value.TotalHours >= 1 ? $"{(int)value.TotalHours:00}:{value.Minutes:00}:{value.Seconds:00}" : $"{value.Minutes:00}:{value.Seconds:00}";
    private void NotifyCommands() { (RefreshCommand as AsyncCommand)?.RaiseCanExecuteChanged(); (UpdateCommand as AsyncCommand)?.RaiseCanExecuteChanged(); (SelectPathCommand as AsyncCommand)?.RaiseCanExecuteChanged(); (DeleteLocalModCommand as AsyncParameterCommand<ModViewModel>)?.RaiseCanExecuteChanged(); (DeleteAllLocalModsCommand as AsyncCommand)?.RaiseCanExecuteChanged(); }
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null) { if (EqualityComparer<T>.Default.Equals(field, value)) return; field = value; OnPropertyChanged(name); }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));

    private sealed record ModCatalog(IReadOnlyList<ModViewModel> Required, IReadOnlyList<ModViewModel> Local);
}

public sealed class ModViewModel : INotifyPropertyChanged
{
    private string markdownDescription;

    private ModViewModel(string id, string name, string? description, string? previewUrl, string? workshopUrl, long size, string state,
        bool canDelete = false, string? localFilePath = null, string? displayIdentifier = null)
    {
        Id = id;
        Name = name;
        var descriptionMarkdown = ToMarkdownDescription(description);
        markdownDescription = descriptionMarkdown;
        Description = CreateSummary(descriptionMarkdown);
        PreviewUrl = previewUrl;
        WorkshopUrl = workshopUrl;
        DisplaySize = size >= 1_048_576 ? $"{size / 1_048_576d:F2} MiB" : $"{size / 1024d:F1} KiB";
        State = state;
        CanDelete = canDelete;
        LocalFilePath = localFilePath;
        DisplayIdentifier = displayIdentifier ?? $"Workshop #{id}";
    }

    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public string MarkdownDescription
    {
        get => markdownDescription;
        private set
        {
            if (markdownDescription == value) return;
            markdownDescription = value;
            PropertyChanged?.Invoke(this, new(nameof(MarkdownDescription)));
        }
    }
    public string? PreviewUrl { get; }
    public string? WorkshopUrl { get; }
    public string DisplaySize { get; }
    public string State { get; }
    public bool CanDelete { get; }
    public string? LocalFilePath { get; }
    public string DisplayIdentifier { get; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public static ModViewModel FromRemote(RemoteMod mod, string state)
        => new(mod.Id, mod.Name, mod.Description, mod.PreviewUrl, mod.WorkshopUrl, mod.TotalSize, state);

    public static ModViewModel FromLocal(string id, IReadOnlyList<ManagedFile> files, ManagedMod? saved, RemoteMod? current, string state)
        => new(id,
            saved?.Name ?? current?.Name ?? $"Workshop #{id}",
            saved?.Description ?? current?.Description ?? $"{files.Count} arquivo(s) gerenciado(s) pelo launcher.",
            saved?.PreviewUrl ?? current?.PreviewUrl,
            saved?.WorkshopUrl ?? current?.WorkshopUrl,
            files.Sum(file => file.Size), state);

    public static ModViewModel FromUnmanaged(UnmanagedLocalMod file)
        => new($"local:{file.Filename}", "Mod local",
            "Arquivo local não gerenciado pelo modpack.", null, null, file.Size, "Local",
            true, file.FullPath, $"Arquivo: {file.Filename}");

    private static string ToMarkdownDescription(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "Sem descrição.";
        var text = Regex.Replace(value, @"\[img\](.*?)\[/img\]", "![]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[url=([^\]]+)\](.*?)\[/url\]", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[url\](.*?)\[/url\]", "[$1]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[b\](.*?)\[/b\]", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[i\](.*?)\[/i\]", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[u\](.*?)\[/u\]", "<u>$1</u>", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[h1\](.*?)\[/h1\]", "# $1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[h2\](.*?)\[/h2\]", "## $1", RegexOptions.IgnoreCase | RegexOptions.Singleline);
        text = Regex.Replace(text, @"\[\*\]", "- ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\[/?(?:list|quote|code)[^\]]*\]", "", RegexOptions.IgnoreCase);
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');
        text = Regex.Replace(text, @"[ \t]{2,}", " ");
        text = Regex.Replace(text, @"\n{3,}", "\n\n").Trim();
        return string.IsNullOrWhiteSpace(text) ? "Sem descrição." : text;
    }

    private static string CreateSummary(string markdown)
    {
        var text = Regex.Replace(markdown, @"!\[[^\]]*\]\([^\)]+\)", "");
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        text = Regex.Replace(text, @"[*_#>`~<]", "");
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return string.IsNullOrWhiteSpace(text) ? "Sem descrição." : text;
    }

    public async Task CacheRemoteImagesAsync(HttpClient httpClient, string cacheDirectory, CancellationToken cancellationToken)
    {
        var matches = Regex.Matches(MarkdownDescription, @"!\[[^\]]*\]\((https?://[^\s\)]+)\)", RegexOptions.IgnoreCase);
        if (matches.Count == 0) return;

        Directory.CreateDirectory(cacheDirectory);
        var rendered = MarkdownDescription;
        foreach (var url in matches.Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) continue;
                var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant();
                var extension = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
                if (extension is not (".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp")) extension = ".img";
                var path = Path.Combine(cacheDirectory, hash + extension);

                if (!File.Exists(path))
                {
                    using var response = await httpClient.GetAsync(uri, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    if (response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) != true)
                        continue;
                    var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                    if (bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024) continue;
                    await File.WriteAllBytesAsync(path, bytes, cancellationToken);
                }

                rendered = rendered.Replace(url, new Uri(path).AbsoluteUri, StringComparison.OrdinalIgnoreCase);
            }
            catch (HttpRequestException) { }
            catch (IOException) { }
        }

        MarkdownDescription = rendered;
    }
}

public sealed class AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke() ?? true;
    public async void Execute(object? parameter) { if (CanExecute(parameter)) await execute(); }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

public sealed class ActionCommand(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}

public sealed class AsyncParameterCommand<T>(Func<T, Task> execute, Func<T?, bool>? canExecute = null) : ICommand where T : class
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute?.Invoke(parameter as T) ?? parameter is T;
    public async void Execute(object? parameter) { if (parameter is T value && CanExecute(value)) await execute(value); }
    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
