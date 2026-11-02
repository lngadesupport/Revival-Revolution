# Revival Revolution Launcher

Launcher comunitário para Windows 10/11 x64, desenvolvido em C# e WPF sobre .NET 8.

## Funcionalidades

- Interface WPF escura com identidade visual preta/vermelha.
- Seleção e persistência da pasta que contém `ufo.exe` e `ufo_Data/Managed`.
- Inicialização do fluxo offline original via `offline_launcher.py`, com logs em `Launcher/Logs`.
- Download do runtime Python 3.13.17 embeddable x64, sem instalação global, com SHA-256, extração em staging e teste de inicialização.
- Diagnóstico que separa dependências essenciais do modo offline de componentes opcionais do multiplayer experimental.
- Verificação automática de novas versões ao abrir o launcher, com publicação via GitHub Releases, hash SHA-256, staging, backup e rollback durante a aplicação.
- Instalação/reparo de pacotes do jogo bloqueados até que o publicador forneça hashes dos ZIPs por um canal independente e confiável.

## Build

No Windows com .NET 8 SDK:

```powershell
dotnet build .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release
```

Publicação self-contained:

```powershell
dotnet publish .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

## Configuração do mantenedor

- Antes de habilitar a instalação, publique hashes SHA-256 autenticados para cada ZIP por canal independente do próprio arquivo. Um `SHA256SUMS.json` incluído no ZIP não autentica o ZIP que o contém.
- Confirme os caminhos internos e os hashes do manifesto contra os pacotes reais.
- O launcher consulta `https://github.com/lngadesupport/Revival-Revolution/releases/latest/download/launcher-update.json` ao abrir. O manifesto deve conter `version`, `packageUrl`, `sha256`, `releaseNotes` e `files`.
- O pacote de atualização deve conter somente arquivos do launcher. Não inclua executáveis, DLLs, assets ou dados proprietários do jogo.
- O hash SHA-256 detecta corrupção; o atualizador também restringe redirecionamentos do manifesto e pacote aos hosts HTTPS oficiais do GitHub Releases. Como manifesto e hash são publicados no mesmo canal, isso não equivale a uma assinatura digital independente.

## Limites conhecidos

- O runtime embeddable pode exigir dependências do Visual C++ Redistributable x64 em máquinas sem esse componente. O projeto fixa Python 3.13.17, última versão da série 3.13 com binários oficiais para Windows; 3.13.18 é uma atualização somente de código-fonte.
- A instalação/reparação automática da distribuição está bloqueada intencionalmente até existir autenticação confiável dos pacotes. Nenhum ZIP de jogo é extraído por esse fluxo.
- Runtime e atualizador ainda precisam ser exercitados em Windows limpo antes de uma release.
- O multiplayer continua experimental e depende das bridges/scripts da distribuição.
- O workflow de CI compila e publica em Windows; não significa que os testes de integração do jogo foram aprovados.

O launcher não redistribui o jogo nem substitui os scripts/bridges originais.

## Publicar uma atualização pelo GitHub

1. Atualize a versão em `Version`, `AssemblyVersion` e `FileVersion` do projeto, mantendo a versão do assembly em quatro partes (por exemplo, `0.7.2.0`).
2. Faça merge da alteração aprovada na branch principal.
3. Crie e envie uma tag estável no commit que deseja publicar, por exemplo: `git tag v0.6.1` e `git push origin v0.6.1`.
4. O workflow `.github/workflows/publish-launcher-release.yml` compila o launcher self-contained em Windows x64, gera o ZIP, calcula SHA-256 e publica o ZIP e `launcher-update.json` como assets da GitHub Release.
5. Ao abrir, instalações anteriores consultam o manifesto da Release mais recente e oferecem a atualização. O usuário confirma o download e a aplicação; o jogo e seus arquivos não são atualizados.

A tag deve apontar para código já compilado e testado. O workflow exige a permissão `contents: write` fornecida pelo GitHub Actions. Releases preliminares não devem ser usadas para promover versões estáveis.


## Versão 0.7.2

- Refinamento do fluxo de atualização: aplicação em segundo plano, limpeza de staging após a substituição e reinicialização automática do launcher.
- Em caso de falha durante a aplicação, o atualizador restaura os arquivos anteriores e tenta reabrir a versão restaurada; consulte `Launcher/Logs/update-error.log` se houver erro.
- Layout com centro livre para preservar a arte de fundo e controles compactos nos cantos.
- A versão 0.7.2 não altera nem repara arquivos proprietários do jogo; o fluxo offline original permanece preservado.
