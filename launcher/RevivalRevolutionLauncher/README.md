# Revival Revolution Launcher

Launcher comunitário para Windows 10/11 x64, desenvolvido em C# e WPF sobre .NET 8.

## Funcionalidades no código

- Interface WPF escura com identidade visual preta/vermelha.
- Seleção e persistência da pasta que contém `ufo.exe` e `ufo_Data/Managed`.
- Inicialização do fluxo offline original via `offline_launcher.py`.
- Download do runtime Python 3.13.17 embeddable x64, sem instalação global, com validação SHA-256 antes da extração.
- Diagnóstico básico de scripts, bridges, DLLs, Python e instalação.
- Atualizador por manifesto HTTPS, hash do pacote e staging antes da aplicação.
- Instalação e reparo de pacotes de jogo deliberadamente bloqueados até que o publicador forneça hashes dos ZIPs por um canal independente e confiável.

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
- Confirme os caminhos de entrada do `SHA256SUMS.json` contra os nomes internos reais dos ZIPs.
- Configure `launcher-update.json` com URL HTTPS real. O manifesto remoto deve conter `version`, `packageUrl`, `sha256`, `releaseNotes` e `files`.
- O pacote de atualização deve conter somente arquivos do launcher. Não inclua executáveis, DLLs, assets ou dados proprietários do jogo.
- O hash SHA-256 detecta corrupção, mas não substitui assinatura digital/autenticidade do publicador.

## Limites conhecidos

- O runtime embeddable pode exigir dependências do Visual C++ Redistributable x64 em máquinas sem esse componente. O projeto fixa Python 3.13.17, última versão da série 3.13 com binários oficiais para Windows; 3.13.18 é uma atualização somente de código-fonte.
- A instalação/reparação automática da distribuição está bloqueada intencionalmente até existir autenticação confiável dos pacotes. Nenhum ZIP de jogo é extraído por esse fluxo no estado atual.
- O instalador/runtime e o atualizador ainda precisam ser exercitados em Windows limpo antes de uma release.
- O multiplayer continua experimental e depende das bridges/scripts da distribuição.
- O workflow de CI compila em Windows; não significa que os testes de integração do jogo foram aprovados.

O launcher não redistribui o jogo nem substitui os scripts/bridges originais.
