using System.Text.Json;
using L4D2ModManager.Core;

namespace L4D2ModManager.Infrastructure;

public sealed class LauncherSettingsStore : ILauncherSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "L4D2ModManager", "settings.json");

    public async Task<LauncherSettings> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(SettingsPath)) return new(null);
        await using var stream = File.OpenRead(SettingsPath);
        return await JsonSerializer.DeserializeAsync<LauncherSettings>(stream, JsonOptions, cancellationToken) ?? new(null);
    }

    public async Task SaveAsync(LauncherSettings settings, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temporary = SettingsPath + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
        File.Move(temporary, SettingsPath, true);
    }
}
