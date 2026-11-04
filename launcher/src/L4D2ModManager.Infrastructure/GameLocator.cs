using L4D2ModManager.Core;

namespace L4D2ModManager.Infrastructure;

public sealed class GameLocator : IGameLocator
{
    public string? FindFromLauncherDirectory(string launcherDirectory)
    {
        var root = Path.GetFullPath(launcherDirectory);
        return Directory.Exists(Path.Combine(root, "left4dead2", "addons")) ? root : null;
    }

    public string NormalizeSelectedPath(string selectedPath)
    {
        var full = Path.GetFullPath(selectedPath);
        if (Directory.Exists(Path.Combine(full, "left4dead2", "addons"))) return full;
        if (full.EndsWith(Path.Combine("left4dead2", "addons"), StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(Directory.GetParent(full)!.FullName)!.FullName;
        throw new DirectoryNotFoundException("Selecione a raiz do Left 4 Dead 2, que contém left4dead2/addons.");
    }
}
