# L4D2 Modpack Manager

Gerenciador incremental de modpacks do Left 4 Dead 2. O GitHub Actions consulta a Oficina Steam, baixa alterações por SteamCMD, verifica os VPKs e publica GitHub Releases incrementais.

## Cadastrar mods

Edite `config/list.json`:

```json
{
  "game": 550,
  "mods": [
    {
      "id": "3566800973",
      "enabled": true,
      "redistributionAuthorized": false
    },
    {
      "id": "https://steamcommunity.com/sharedfiles/filedetails/?id=2396847377",
      "enabled": true,
      "redistributionAuthorized": false
    }
  ]
}
```

O campo `id` aceita um ID numérico ou uma URL completa do Workshop. IDs duplicados são ignorados depois da primeira ocorrência e a ordem original é preservada.

Mantenha `redistributionAuthorized` como `false` até ter autorização real do autor para republicar o arquivo em uma GitHub Release.

## Validar antes de enviar

Requer Python 3.12 ou superior:

```powershell
python -m pip install -r requirements.txt
python -m scripts.validate_list config/list.json
python -m unittest discover -s tests -v
```

O workflow `.github/workflows/build.yml` repete as verificações e publica quando `config/list.json` muda. O workflow `check-updates.yml` verifica o Workshop diariamente às 05:17 no horário de Brasília (08:17 UTC).

## Publicação no GitHub

1. Suba o repositório para a conta desejada com a branch padrão `main`.
2. Em **Settings → Actions → General → Workflow permissions**, habilite **Read and write permissions**.
3. Cadastre em `config/list.json` somente itens cuja redistribuição esteja autorizada.
4. Marque esses itens com `"redistributionAuthorized": true` e faça push.

O workflow usa o `GITHUB_TOKEN` automático; não é necessário cadastrar token pessoal. A publicação falha por inteiro se um item estiver indisponível, pertencer a outro jogo, não gerar VPK ou se algum upload não puder ser confirmado.

Cada versão é criada inicialmente como rascunho. Os VPKs recebem nomes legíveis derivados do título no Workshop, mais o Workshop ID e parte do SHA-256. Cada execução cria a próxima versão PATCH (`1.0.1`, `1.0.2` etc.), mesmo quando o conteúdo do Workshop não mudou; nesse caso os VPKs anteriores são reutilizados. Cada Release contém somente um `manifest.json`.

O manifesto da Release mais recente é a fonte canônica. O workflow o recupera antes de comparar alterações, portanto uma eventual falha ao enviar o commit de `manifest.json` para uma branch protegida não provoca republicação dos mesmos arquivos.

Itens com `redistributionAuthorized: false` não entram no manifesto nem são baixados/republicados por este pipeline.

## Estado do projeto

- Fase 1: estrutura, contrato da lista, normalização, validação e testes.
- Fase 2: consulta de metadados e download autorizado via SteamCMD.
- Fase 3: comparação incremental, versionamento, GitHub Releases e manifesto.
- Fase 4 pendente: atualizador Windows.

