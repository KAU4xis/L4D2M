# Arquitetura do launcher

## Objetivos

- Executável Windows simples, self-contained e publicável pelo GitHub Actions.
- Atualização incremental e idempotente baseada no `manifest.json`.
- Zero exclusões de arquivos que não sejam comprovadamente gerenciados.
- Downloads completamente validados antes de alterar o jogo.
- Interface substituível sem acoplar regras de sincronização ao WPF.

## Fluxo

```text
GitHub Release / manifest.json
              │
              ▼
       ManifestClient
              │
              ▼
         SyncPlanner ──────────────┐
              │                    │
              ▼                    ▼
       ModSynchronizer       LocalStateStore
              │                    │
       downloads temporários       └─ addons/.l4d2-modpack/state.json
              │
       SHA-256 + tamanho
              │
       backup → aplicação → estado
              │
       rollback em qualquer falha
```

## Limites de responsabilidade

### Core

Não acessa rede, disco, WPF ou APIs do Windows. Define dados, contratos, caminhos seguros e o plano de sincronização.

### Infrastructure

Implementa integrações externas: GitHub/HTTP, armazenamento JSON, descoberta da raiz, hash, download, transação e rollback.

### Desktop

Apresenta estado e envia comandos. Não decide quais arquivos apagar nem valida downloads.

## Segurança dos arquivos

1. O manifesto só pode fornecer nomes simples terminados em `.vpk`.
2. URLs de VPK devem usar HTTPS no domínio `github.com`.
3. Todos os downloads são feitos numa transação temporária.
4. Tamanho e SHA-256 precisam coincidir antes da aplicação.
5. Arquivos pessoais com nomes conflitantes bloqueiam a atualização e são preservados.
6. Arquivos gerenciados alterados pelo jogador bloqueiam substituição/remoção.
7. Somente entradas do estado local podem ser removidas.
8. O jogo deve estar fechado.

## Dados locais

- `%LocalAppData%/L4D2ModManager/settings.json`: última raiz escolhida.
- `left4dead2/addons/.l4d2-modpack/state.json`: versão instalada e arquivos sob gerenciamento.

## Próximas etapas

- Testes de integração da transação e rollback com HTTP simulado.
- Tela de detalhes do mod e abertura do Workshop.
- Logs persistentes e botão de exportação do diagnóstico.
- Assinatura Authenticode do executável, quando houver certificado.
- Publicação do launcher como asset da Release do modpack.
