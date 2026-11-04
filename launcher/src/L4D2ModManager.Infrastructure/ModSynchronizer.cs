using System.Security.Cryptography;
using L4D2ModManager.Core;

namespace L4D2ModManager.Infrastructure;

public sealed class ModSynchronizer(HttpClient httpClient, ILocalStateStore stateStore) : IModSynchronizer
{
    public async Task<SyncPlan> PlanAsync(RemoteManifest manifest, string addonsPath, CancellationToken cancellationToken)
        => SyncPlanner.Create(manifest, await stateStore.LoadAsync(addonsPath, cancellationToken));

    public async Task ApplyAsync(RemoteManifest manifest, string addonsPath, SyncPlan plan, IProgress<SyncProgress>? progress, CancellationToken cancellationToken)
    {
        if (System.Diagnostics.Process.GetProcessesByName("left4dead2").Length > 0)
            throw new IOException("Feche o Left 4 Dead 2 antes de atualizar o modpack.");
        Directory.CreateDirectory(addonsPath);
        var state = await stateStore.LoadAsync(addonsPath, cancellationToken);
        var managedNames = state.Files.Select(file => file.Filename).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, file) in plan.Downloads)
        {
            var target = SafePath.ManagedFile(addonsPath, file.Filename);
            if (File.Exists(target) && !managedNames.Contains(file.Filename))
                throw new IOException($"Já existe um mod pessoal chamado '{file.Filename}'. O arquivo foi preservado.");
        }
        await EnsureManagedFilesUnmodified(state, addonsPath, plan, cancellationToken);
        var work = Path.Combine(addonsPath, ".l4d2-modpack", $"transaction-{Guid.NewGuid():N}");
        var downloads = Path.Combine(work, "downloads");
        var backups = Path.Combine(work, "backups");
        Directory.CreateDirectory(downloads);
        Directory.CreateDirectory(backups);
        var changedTargets = new List<string>();
        try
        {
            long completed = 0;
            foreach (var (_, file) in plan.Downloads)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var temp = SafePath.ManagedFile(downloads, file.Filename);
                progress?.Report(new("Baixando", file.Filename, completed, plan.DownloadBytes));
                await DownloadAndVerify(file, temp, cancellationToken);
                completed += file.Size;
                progress?.Report(new("Validado", file.Filename, completed, plan.DownloadBytes));
            }

            foreach (var removal in plan.Removals)
            {
                var target = SafePath.ManagedFile(addonsPath, removal.Filename);
                if (!File.Exists(target)) continue;
                File.Move(target, SafePath.ManagedFile(backups, removal.Filename));
                changedTargets.Add(target);
            }
            foreach (var (_, file) in plan.Downloads)
            {
                var target = SafePath.ManagedFile(addonsPath, file.Filename);
                if (File.Exists(target))
                    File.Move(target, SafePath.ManagedFile(backups, file.Filename), true);
                File.Move(SafePath.ManagedFile(downloads, file.Filename), target, true);
                changedTargets.Add(target);
            }

            var newFiles = manifest.Mods.SelectMany(mod => mod.Files.Select(file => new ManagedFile(file.Filename, file.Size, file.Sha256, mod.Id))).ToArray();
            await stateStore.SaveAsync(addonsPath, new(1, manifest.Latest, newFiles), cancellationToken);
            Directory.Delete(work, true);
        }
        catch
        {
            foreach (var target in changedTargets) if (File.Exists(target)) File.Delete(target);
            if (Directory.Exists(backups))
                foreach (var backup in Directory.EnumerateFiles(backups)) File.Move(backup, SafePath.ManagedFile(addonsPath, Path.GetFileName(backup)), true);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            throw;
        }
    }

    private static async Task EnsureManagedFilesUnmodified(LocalState state, string addonsPath, SyncPlan plan, CancellationToken cancellationToken)
    {
        var affected = plan.Removals.Select(file => file.Filename)
            .Concat(plan.Downloads.Select(item => item.File.Filename)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var file in state.Files.Where(file => affected.Contains(file.Filename)))
        {
            var path = SafePath.ManagedFile(addonsPath, file.Filename);
            if (!File.Exists(path)) continue;
            var actual = await Hash(path, cancellationToken);
            if (!actual.Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"O arquivo gerenciado '{file.Filename}' foi modificado localmente e foi preservado.");
        }
    }

    private async Task DownloadAndVerify(RemoteFile file, string target, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("URL de download não confiável no manifesto.");
        await using var source = await httpClient.GetStreamAsync(uri, cancellationToken);
        await using (var output = File.Create(target)) await source.CopyToAsync(output, cancellationToken);
        var info = new FileInfo(target);
        if (info.Length != file.Size || !(await Hash(target, cancellationToken)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Falha de integridade em {file.Filename}.");
    }

    private static async Task<string> Hash(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }
}
