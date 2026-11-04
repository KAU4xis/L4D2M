namespace L4D2ModManager.Core;

public static class SyncPlanner
{
    public static SyncPlan Create(RemoteManifest manifest, LocalState state)
    {
        var desired = manifest.Mods
            .SelectMany(mod => mod.Files.Select(file => (Mod: mod, File: file)))
            .ToDictionary(item => item.File.Filename, StringComparer.OrdinalIgnoreCase);
        var installed = state.Files.ToDictionary(file => file.Filename, StringComparer.OrdinalIgnoreCase);
        var downloads = desired.Values
            .Where(item => !installed.TryGetValue(item.File.Filename, out var local) ||
                           !string.Equals(local.Sha256, item.File.Sha256, StringComparison.OrdinalIgnoreCase) ||
                           local.Size != item.File.Size)
            .ToArray();
        var removals = installed.Values.Where(file => !desired.ContainsKey(file.Filename)).ToArray();
        return new(downloads, removals, desired.Count - downloads.Length);
    }
}

public static class SafePath
{
    public static string ManagedFile(string addonsPath, string filename)
    {
        if (string.IsNullOrWhiteSpace(filename) || filename != Path.GetFileName(filename) ||
            filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !filename.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Nome de arquivo inseguro no manifesto: {filename}");
        var root = Path.GetFullPath(addonsPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(root, filename));
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O manifesto tentou acessar um caminho fora de addons.");
        return candidate;
    }
}
