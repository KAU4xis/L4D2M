using System.Text.Json.Serialization;

namespace L4D2ModManager.Core;

public sealed record RemoteManifest(
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("latest")] string Latest,
    [property: JsonPropertyName("game")] int Game,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt,
    [property: JsonPropertyName("mods")] IReadOnlyList<RemoteMod> Mods);

public sealed record RemoteMod(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("previewUrl")] string? PreviewUrl,
    [property: JsonPropertyName("workshopUrl")] string WorkshopUrl,
    [property: JsonPropertyName("creator")] string? Creator,
    [property: JsonPropertyName("timeUpdated")] long TimeUpdated,
    [property: JsonPropertyName("files")] IReadOnlyList<RemoteFile> Files)
{
    [JsonIgnore] public long TotalSize => Files.Sum(file => file.Size);
}

public sealed record RemoteFile(
    [property: JsonPropertyName("filename")] string Filename,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("url")] string Url);

public sealed record ManagedFile(string Filename, long Size, string Sha256, string ModId);

public sealed record ManagedMod(
    string Id,
    string Name,
    string? Description,
    string? PreviewUrl,
    string WorkshopUrl);

public sealed record LocalState(
    int SchemaVersion,
    string? InstalledVersion,
    IReadOnlyList<ManagedFile> Files,
    IReadOnlyList<ManagedMod>? Mods = null)
{
    [JsonIgnore] public IReadOnlyList<ManagedMod> InstalledMods => Mods ?? [];
    public static LocalState Empty { get; } = new(2, null, [], []);
}

public sealed record SyncPlan(
    IReadOnlyList<(RemoteMod Mod, RemoteFile File)> Downloads,
    IReadOnlyList<ManagedFile> Removals,
    int UnchangedCount)
{
    public long DownloadBytes => Downloads.Sum(item => item.File.Size);
    public bool HasChanges => Downloads.Count > 0 || Removals.Count > 0;
}

public sealed record SyncProgress(
    string Stage,
    string? Filename,
    long CompletedBytes,
    long TotalBytes,
    long CurrentFileBytes = 0,
    long CurrentFileTotalBytes = 0,
    int CompletedFiles = 0,
    int TotalFiles = 0);

public sealed record RateLimitPause(
    string Message,
    TimeSpan Wait,
    DateTimeOffset? ResetAt,
    string Url,
    int Attempt,
    int MaxAttempts);

public sealed record LauncherSettings(string? GamePath);

public sealed record UnmanagedLocalMod(string Filename, string FullPath, long Size);

public static class LocalModScanner
{
    public static IReadOnlyList<UnmanagedLocalMod> FindUnmanaged(string addonsPath, LocalState state)
    {
        if (!Directory.Exists(addonsPath)) return [];

        var managed = state.Files.Select(file => file.Filename)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return new DirectoryInfo(addonsPath)
            .EnumerateFiles("*.vpk", SearchOption.TopDirectoryOnly)
            .Where(file => !managed.Contains(file.Name))
            .OrderBy(file => file.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(file => new UnmanagedLocalMod(file.Name, file.FullName, file.Length))
            .ToArray();
    }
}
