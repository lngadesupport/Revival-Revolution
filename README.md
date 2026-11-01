# Revival Revolution

**Revival Revolution** é um projeto comunitário de preservação, inicialização e jogo local/multiplayer experimental para *Metal Revolution*. Este repositório reúne o launcher moderno para Windows e a documentação necessária para preparar, validar e iniciar uma instalação existente do jogo.

## Sobre o jogo original

*Metal Revolution* é um jogo de luta com temática cyberpunk e personagens robóticos, desenvolvido pelo estúdio **NEXT Studios**. A atribuição de desenvolvimento do jogo original é ao estúdio, não a uma pessoa individual. A operação original foi publicada/comunicada pela equipe de Metal Revolution/GTarcade. Consulte as referências no final deste documento.

**Criador/desenvolvedor do jogo original: NEXT Studios.** Não encontramos, nos arquivos fornecidos para este repositório, uma atribuição verificável de uma pessoa individual como criador do projeto comunitário Revival Revolution; por isso, não inventamos um nome. Os créditos de quem mantém este repositório ficam associados à conta GitHub que publica as alterações, salvo indicação explícita em contrário.

## O que este projeto faz

- Oferece um launcher Windows com interface WPF moderna, tema escuro e detalhes vermelhos.
- Prepara um runtime Python portátil para executar os scripts do pacote sem exigir instalação global de Python.
- Executa o fluxo offline existente por meio de `offline_launcher.py`.
- Reúne diagnósticos para arquivos, scripts, DLLs, runtime e instalação.
- Prepara um mecanismo de atualização do launcher baseado em manifesto HTTPS.
- Mantém instalação/reparo automático de pacotes do jogo desabilitados até que hashes SHA-256 dos arquivos ZIP sejam autenticados por canal independente. Um manifesto de checksums dentro do próprio ZIP não prova a autenticidade do pacote.
- Documenta a integração multiplayer comunitária, que permanece experimental e depende das bridges/scripts já presentes na distribuição.

## Estrutura

- `launcher/RevivalRevolutionLauncher/` — projeto C# / WPF (.NET 8).
- `launcher/RevivalRevolutionLauncher/Services/` — serviços de download, extração segura, configuração, runtime, inicialização, diagnóstico e atualização.
- `launcher/RevivalRevolutionLauncher/Properties/PublishProfiles/` — perfil de publicação Windows x64 self-contained.
- `docs/` — documentação técnica e avisos de compatibilidade.

## Compilar e publicar

Requisitos de desenvolvimento: Windows 10/11 x64 e .NET 8 SDK.

```powershell
dotnet build .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release
```

Publicação para jogadores sem .NET instalado:

```powershell
dotnet publish .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

O pacote self-contained inclui o runtime .NET necessário. O SDK só é necessário na máquina que compila. O single-file não é forçado por causa das particularidades de WPF e componentes nativos.

## Atualizações do launcher\n\nO launcher consulta automaticamente as GitHub Releases ao iniciar. Para publicar uma versão, crie uma tag `vX.Y.Z` sobre um commit já integrado à `main`; o workflow publica o ZIP self-contained e o manifesto com SHA-256. Tags com sufixo, como `v0.7.0-rc.1`, são publicadas como pré-lançamentos e não substituem a Release estável mais recente. Consulte `launcher/RevivalRevolutionLauncher/README.md` para o procedimento.\n\n## Configuração antes de uma versão pública

1. Obter e publicar hashes SHA-256 confiáveis dos pacotes de distribuição por canal independente dos ZIPs.
2. Conferir o formato e os caminhos de `SHA256SUMS.json` contra os ZIPs reais.
3. O atualizador consulta o manifesto oficial em `https://github.com/lngadesupport/Revival-Revolution/releases/latest/download/launcher-update.json`; publique versões com tags `vX.Y.Z` somente depois que o commit estiver integrado à `main`.
4. Executar build, preparação do Python portátil, inicialização offline e diagnóstico em Windows x64. A instalação/reparação automática só deve ser reativada após autenticação dos pacotes.
5. Testar multiplayer em duas máquinas independentes. O rollback/netplay não deve ser anunciado como universalmente compatível.

## Preservação e propriedade intelectual

Este é um projeto comunitário independente, não oficial e sem afiliação com NEXT Studios, GTarcade ou titulares de Metal Revolution. O nome e os elementos de *Metal Revolution* pertencem aos respectivos titulares. Não inclua no repositório nem redistribua executáveis, DLLs, assets, dados do jogo ou pacotes proprietários sem autorização expressa. O repositório deve conter código original do launcher, documentação e ferramentas que possam ser compartilhadas legalmente.

Os scripts, bridges e DLLs existentes da distribuição não devem ser substituídos sem verificar sua compatibilidade e preservar uma cópia do estado anterior.

## Créditos e referências

- **Jogo original — NEXT Studios:** [IndieCade — Metal Revolution](https://www.indiecade.com/metal-revolution/).
- **Identificação de desenvolvedor/publicador — NEXT Studios:** [ModDB — Metal Revolution](https://www.moddb.com/games/metal-revolution).
- **Comunicado de encerramento dos servidores — Metal Revolution Team / GTarcade:** [Fórum oficial](https://forum.gtarcade.com/topic/143187).

## Estado de validação

O código WPF foi compilado com sucesso pelo workflow Windows do GitHub Actions no commit registrado na PR. A compilação não substitui testes em Windows limpo, com os pacotes reais e a instalação-alvo. Não declare uma versão como pronta para distribuição até que esses testes sejam concluídos.
