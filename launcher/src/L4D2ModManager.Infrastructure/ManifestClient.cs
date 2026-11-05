using System.Net.Http.Json;
using System.Text.Json;
using L4D2ModManager.Core;

namespace L4D2ModManager.Infrastructure;

public sealed class ManifestClient(HttpClient httpClient, Uri manifestUri) : IManifestClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<RemoteManifest> GetLatestAsync(CancellationToken cancellationToken)
    {
        var manifest = await httpClient.GetFromJsonAsync<RemoteManifest>(manifestUri, JsonOptions, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidDataException("O servidor retornou um manifesto vazio.");
        if (manifest.SchemaVersion != 1 || manifest.Game != 550 || string.IsNullOrWhiteSpace(manifest.Latest))
            throw new InvalidDataException("O formato do manifesto remoto não é suportado.");
        return manifest;
    }
}
