namespace L4D2ModManager.Core;

public interface IManifestClient
{
    Task<RemoteManifest> GetLatestAsync(CancellationToken cancellationToken);
}

public interface ILocalStateStore
{
    Task<LocalState> LoadAsync(string addonsPath, CancellationToken cancellationToken);
    Task SaveAsync(string addonsPath, LocalState state, CancellationToken cancellationToken);
}

public interface IModSynchronizer
{
    Task<SyncPlan> PlanAsync(RemoteManifest manifest, string addonsPath, CancellationToken cancellationToken);
    Task ApplyAsync(RemoteManifest manifest, string addonsPath, SyncPlan plan, IProgress<SyncProgress>? progress,
        Func<RateLimitPause, CancellationToken, Task<bool>>? rateLimitWait = null, CancellationToken cancellationToken = default);
}

public interface IGameLocator
{
    string? FindFromLauncherDirectory(string launcherDirectory);
    string NormalizeSelectedPath(string selectedPath);
}

public interface ILauncherSettingsStore
{
    Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken);
}
