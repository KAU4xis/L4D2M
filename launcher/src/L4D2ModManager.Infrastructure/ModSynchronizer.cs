using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using L4D2ModManager.Core;

namespace L4D2ModManager.Infrastructure;

public sealed class ModSynchronizer(HttpClient httpClient, ILocalStateStore stateStore) : IModSynchronizer
{
    private const int MaxRateLimitWaits = 120;

    public async Task<SyncPlan> PlanAsync(RemoteManifest manifest, string addonsPath, CancellationToken cancellationToken)
        => SyncPlanner.Create(manifest, await stateStore.LoadAsync(addonsPath, cancellationToken));

    public async Task ApplyAsync(RemoteManifest manifest, string addonsPath, SyncPlan plan, IProgress<SyncProgress>? progress,
        Func<RateLimitPause, CancellationToken, Task<bool>>? rateLimitWait = null, CancellationToken cancellationToken = default)
    {
        if (System.Diagnostics.Process.GetProcessesByName("left4dead2").Length > 0)
            throw new IOException("Feche o Left 4 Dead 2 antes de atualizar o modpack.");
        Directory.CreateDirectory(addonsPath);
        EnsureWriteAccess(addonsPath);
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
            var totalMods = plan.Downloads.Select(item => item.Mod.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var completedMods = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var completedBytesByMod = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < plan.Downloads.Count; index++)
            {
                var (mod, file) = plan.Downloads[index];
                cancellationToken.ThrowIfCancellationRequested();
                var temp = SafePath.ManagedFile(downloads, file.Filename);
                var modTotalBytes = plan.Downloads.Where(item => item.Mod.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))
                    .Sum(item => item.File.Size);
                completedBytesByMod.TryGetValue(mod.Id, out var modBytesBeforeFile);
                progress?.Report(new("Preparando download", mod.Name, completed, plan.DownloadBytes,
                    modBytesBeforeFile, modTotalBytes, completedMods.Count, totalMods));
                await DownloadAndVerify(file, temp, fileCompleted =>
                {
                    progress?.Report(new("Baixando", mod.Name, completed + fileCompleted, plan.DownloadBytes,
                        modBytesBeforeFile + fileCompleted, modTotalBytes, completedMods.Count, totalMods));
                }, rateLimitWait, cancellationToken);
                completed += file.Size;
                completedBytesByMod[mod.Id] = modBytesBeforeFile + file.Size;
                if (completedBytesByMod[mod.Id] >= modTotalBytes) completedMods.Add(mod.Id);
                progress?.Report(new("Validado", mod.Name, completed, plan.DownloadBytes,
                    completedBytesByMod[mod.Id], modTotalBytes, completedMods.Count, totalMods));
            }

            foreach (var removal in plan.Removals)
            {
                var target = SafePath.ManagedFile(addonsPath, removal.Filename);
                if (!File.Exists(target)) continue;
                MoveFile(target, SafePath.ManagedFile(backups, removal.Filename), false, "guardar backup");
                changedTargets.Add(target);
            }
            foreach (var (_, file) in plan.Downloads)
            {
                var target = SafePath.ManagedFile(addonsPath, file.Filename);
                if (File.Exists(target))
                    MoveFile(target, SafePath.ManagedFile(backups, file.Filename), true, "guardar versão anterior");
                MoveFile(SafePath.ManagedFile(downloads, file.Filename), target, true, "instalar mod");
                changedTargets.Add(target);
            }

            var newFiles = manifest.Mods.SelectMany(mod => mod.Files.Select(file => new ManagedFile(file.Filename, file.Size, file.Sha256, mod.Id))).ToArray();
            var newMods = manifest.Mods.Select(mod => new ManagedMod(
                mod.Id, mod.Name, mod.Description, mod.PreviewUrl, mod.WorkshopUrl)).ToArray();
            try
            {
                await stateStore.SaveAsync(addonsPath, new(2, manifest.Latest, newFiles, newMods), cancellationToken);
            }
            catch (UnauthorizedAccessException exception)
            {
                throw new IOException($"Sem permissão para atualizar o state.json em '{Path.Combine(addonsPath, ".l4d2-modpack")}'.", exception);
            }
            TryDeleteDirectory(work);
        }
        catch (Exception original)
        {
            try
            {
                foreach (var target in changedTargets) if (File.Exists(target)) File.Delete(target);
                if (Directory.Exists(backups))
                    foreach (var backup in Directory.EnumerateFiles(backups))
                        MoveFile(backup, SafePath.ManagedFile(addonsPath, Path.GetFileName(backup)), true, "restaurar backup");
                TryDeleteDirectory(work);
            }
            catch (Exception rollback)
            {
                throw new IOException($"{original.Message}\nTambém houve falha ao restaurar o backup: {rollback.Message}", original);
            }
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

    private async Task DownloadAndVerify(RemoteFile file, string target, Action<long> reportBytes,
        Func<RateLimitPause, CancellationToken, Task<bool>>? rateLimitWait, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("URL de download não confiável no manifesto.");
        using var response = await SendDownloadRequestAsync(uri, rateLimitWait, cancellationToken);
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using (var output = File.Create(target))
        {
            var buffer = new byte[128 * 1024];
            long copied = 0;
            var lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
            while (true)
            {
                var read = await source.ReadAsync(buffer, cancellationToken);
                if (read == 0) break;
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                copied += read;
                if (System.Diagnostics.Stopwatch.GetElapsedTime(lastReport).TotalMilliseconds >= 100)
                {
                    reportBytes(copied);
                    lastReport = System.Diagnostics.Stopwatch.GetTimestamp();
                }
            }
            reportBytes(copied);
        }
        var info = new FileInfo(target);
        if (info.Length != file.Size || !(await Hash(target, cancellationToken)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Falha de integridade em {file.Filename}.");
    }

    private static async Task<string> Hash(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken)).ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> SendDownloadRequestAsync(Uri uri,
        Func<RateLimitPause, CancellationToken, Task<bool>>? rateLimitWait, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= MaxRateLimitWaits; attempt++)
        {
            var response = await httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (response.IsSuccessStatusCode) return response;

            var status = (int)response.StatusCode;
            var rateLimited = response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429;
            var retryable = rateLimited || status is 500 or 502 or 503 or 504;

            if (rateLimited)
            {
                var reset = TryGetRateLimitReset(response);
                TimeSpan? resetDelta = reset.HasValue ? reset.Value - DateTimeOffset.UtcNow : null;
                var delay = response.Headers.RetryAfter?.Delta ?? resetDelta ?? TimeSpan.FromMinutes(1);
                if (delay < TimeSpan.FromSeconds(1)) delay = TimeSpan.FromSeconds(1);
                if (delay > TimeSpan.FromMinutes(65)) delay = TimeSpan.FromMinutes(65);

                var message = BuildRateLimitMessage(response, reset, uri);
                response.Dispose();

                var shouldWait = rateLimitWait is null || await rateLimitWait(
                    new(message, delay, reset, uri.ToString(), attempt + 1, MaxRateLimitWaits), cancellationToken);
                if (!shouldWait)
                    throw new OperationCanceledException("Download cancelado pelo usuário durante a espera do limite do GitHub.");
                await Task.Delay(delay, cancellationToken);
                continue;
            }

            if (!retryable || attempt >= 3)
            {
                var reason = $"{status} {response.ReasonPhrase}";
                response.Dispose();
                throw new HttpRequestException($"Falha ao baixar '{uri}': {reason}.");
            }

            var retryDelay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
            response.Dispose();
            await Task.Delay(retryDelay > TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : retryDelay, cancellationToken);
        }

        throw new HttpRequestException($"Falha ao baixar '{uri}': limite do GitHub persistiu após várias tentativas.");
    }

    private static DateTimeOffset? TryGetRateLimitReset(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-ratelimit-reset", out var values) &&
            long.TryParse(values.FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var epoch))
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(epoch).ToUniversalTime(); }
            catch (ArgumentOutOfRangeException) { }
        }
        if (response.Headers.RetryAfter?.Date is DateTimeOffset date) return date.ToUniversalTime();
        if (response.Headers.RetryAfter?.Delta is TimeSpan delta) return DateTimeOffset.UtcNow + delta;
        return null;
    }

    private static string BuildRateLimitMessage(HttpResponseMessage response, DateTimeOffset? reset, Uri uri)
    {
        var host = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ? "GitHub (download de releases)" : uri.Host;
        var resetText = reset.HasValue ? $" Liberação estimada: {reset.Value.ToLocalTime():HH:mm:ss}." : string.Empty;
        var retry = response.Headers.RetryAfter?.Delta is TimeSpan delta
            ? $" Retry-After: {Math.Ceiling(delta.TotalSeconds)}s." : string.Empty;
        var remaining = response.Headers.TryGetValues("x-ratelimit-remaining", out var r) ? r.FirstOrDefault() : null;
        var remainingText = remaining == "0" ? " Requisições restantes: 0." : string.Empty;

        return $"Limite de requisições/downloads atingido em {host}.{resetText}{retry}{remainingText}\n" +
            "O GitHub não publica um único limite fixo para downloads não autenticados em github.com; " +
            "para a API REST o limite não autenticado é 60 requisições/hora por IP (com x-ratelimit-reset). " +
            "O launcher vai aguardar e continuar de onde parou.";
    }

    private static void EnsureWriteAccess(string addonsPath)
    {
        var managerPath = Path.Combine(addonsPath, ".l4d2-modpack");
        try
        {
            Directory.CreateDirectory(managerPath);
            ProbeDirectory(addonsPath);
            ProbeDirectory(managerPath);

            var statePath = Path.Combine(managerPath, "state.json");
            if (File.Exists(statePath) && File.GetAttributes(statePath).HasFlag(FileAttributes.ReadOnly))
                throw new UnauthorizedAccessException("O arquivo state.json está marcado como somente leitura.");
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException($"Sem permissão de escrita em '{managerPath}'. Verifique as permissões da pasta antes de baixar.", exception);
        }
    }

    private static void ProbeDirectory(string directory)
    {
        var probe = Path.Combine(directory, $"write-test-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(probe, "ok");
        File.Delete(probe);
    }

    private static void MoveFile(string source, string destination, bool overwrite, string operation)
    {
        try
        {
            File.Move(source, destination, overwrite);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException($"Sem acesso para {operation}. Origem: '{source}'. Destino: '{destination}'.", exception);
        }
        catch (IOException exception)
        {
            throw new IOException($"Falha ao {operation}. Origem: '{source}'. Destino: '{destination}'. {exception.Message}", exception);
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
