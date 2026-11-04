# L4D2 Modpack Manager — Launcher

Launcher Windows em WPF/.NET 10 LTS para consultar o manifesto publicado, apresentar os mods com imagens e sincronizar a instalação local de maneira transacional.

## Arquitetura

- `Core`: modelos, contratos, validação de caminhos e planejamento incremental. Não depende da interface nem de infraestrutura.
- `Infrastructure`: HTTP, descoberta do jogo, estado local, SHA-256, downloads, backup e rollback.
- `Desktop`: composição WPF/MVVM, seleção da pasta, catálogo visual e progresso.
- `Tests`: idempotência, comparação e proteção contra path traversal.

O estado fica em `left4dead2/addons/.l4d2-modpack/state.json`. Apenas arquivos registrados nesse estado podem ser removidos. Arquivos pessoais são preservados, inclusive quando possuem nome conflitante ou quando um arquivo gerenciado foi alterado localmente.

## Imagem personalizada

Coloque `background.jpg` ao lado do executável em `Assets/background.jpg`. Enquanto ela não existir, a interface usa um gradiente escuro.

## Desenvolvimento

```powershell
dotnet restore launcher/L4D2ModManager.slnx
dotnet test launcher/L4D2ModManager.slnx -c Release
dotnet build launcher/L4D2ModManager.slnx -c Release
```

## Publicação

```powershell
dotnet publish launcher/src/L4D2ModManager.Desktop/L4D2ModManager.Desktop.csproj `
  -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

O workflow `build-launcher.yml` executa os testes e disponibiliza o pacote `L4D2ModManager-win-x64` como artifact do GitHub Actions.
