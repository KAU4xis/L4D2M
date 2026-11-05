using System.Text.Json;
using L4D2ModManager.Core;

namespace L4D2ModManager.Infrastructure;

public sealed class LocalStateStore : ILocalStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static string StatePath(string addonsPath) => Path.Combine(addonsPath, ".l4d2-modpack", "state.json");

    public async Task<LocalState> LoadAsync(string addonsPath, CancellationToken cancellationToken)
    {
        var path = StatePath(addonsPath);
        if (!File.Exists(path)) return LocalState.Empty;
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<LocalState>(stream, JsonOptions, cancellationToken).ConfigureAwait(false)
            ?? LocalState.Empty;
    }

    public async Task SaveAsync(string addonsPath, LocalState state, CancellationToken cancellationToken)
    {
        var path = StatePath(addonsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await using (var stream = File.Create(temporary))
            await JsonSerializer.SerializeAsync(stream, state, JsonOptions, cancellationToken).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }
}
