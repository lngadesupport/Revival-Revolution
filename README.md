# Revival Revolution

**Revival Revolution** é um projeto comunitário de preservação, inicialização e jogo local/multiplayer experimental para *Metal Revolution*. Este repositório reúne o launcher moderno para Windows e a documentação necessária para preparar, validar e iniciar uma instalação existente do jogo.

## Sobre o jogo original

*Metal Revolution* é um jogo de luta com temática cyberpunk e personagens robóticos, desenvolvido pelo estúdio **NEXT Studios**. A atribuição de desenvolvimento do jogo original é ao estúdio, não a uma pessoa individual. A operação original foi publicada/comunicada pela equipe de Metal Revolution/GTarcade. Consulte as referências no final deste documento.

**Criador/desenvolvedor do jogo original: NEXT Studios.** Não encontramos, nos arquivos fornecidos para este repositório, uma atribuição verificável de uma pessoa individual como criador do projeto comunitário Revival Revolution; por isso, não inventamos um nome. Os créditos de quem mantém este repositório ficam associados à conta GitHub que publica as alterações, salvo indicação explícita em contrário.

## O que este projeto faz

- Oferece um launcher Windows com interface WPF moderna, tema escuro e detalhes vermelhos.
- Prepara um runtime Python portátil para executar os scripts do pacote sem exigir instalação global de Python.
- Baixa os pacotes de distribuição configurados pelo mantenedor e extrai arquivos ZIP com proteção contra caminhos maliciosos.
- Verifica arquivos por SHA-256 e permite reparar arquivos ausentes ou alterados quando o manifesto corresponde ao pacote.
- Executa o fluxo offline existente por meio de `offline_launcher.py`.
- Reúne diagnósticos para arquivos, scripts, DLLs, runtime e instalação.
- Prepara um mecanismo de atualização do launcher baseado em manifesto HTTPS.
- Documenta a integração multiplayer comunitária, que permanece experimental e depende das bridges/scripts já presentes na distribuição.

## Estrutura

- `launcher/RevivalRevolutionLauncher/` — projeto C# / WPF (.NET 8).
- `launcher/RevivalRevolutionLauncher/Services/` — serviços de download, instalação, integridade, runtime, diagnóstico e atualização.
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

## Configuração antes de uma versão pública

1. Conferir os IDs e o formato real dos pacotes de distribuição antes de habilitar a instalação automática.
2. Conferir o formato e os caminhos de `SHA256SUMS.json` contra os ZIPs reais.
3. Hospedar o manifesto e o pacote de atualização em HTTPS e substituir o endereço de exemplo em `launcher-update.json`.
4. Executar build, instalação, reparo, inicialização offline e diagnóstico em Windows x64.
5. Testar multiplayer em duas máquinas independentes. O rollback/netplay não deve ser anunciado como universalmente compatível.

## Preservação e propriedade intelectual

Este é um projeto comunitário independente, não oficial e sem afiliação com NEXT Studios, GTarcade ou titulares de Metal Revolution. O nome e os elementos de *Metal Revolution* pertencem aos respectivos titulares. Não inclua no repositório nem redistribua executáveis, DLLs, assets, dados do jogo ou pacotes proprietários sem autorização expressa. O repositório deve conter código original do launcher, documentação e ferramentas que possam ser compartilhadas legalmente.

Os scripts, bridges e DLLs existentes da distribuição não devem ser substituídos sem verificar sua compatibilidade e preservar uma cópia do estado anterior.

## Créditos e referências

- **Jogo original — NEXT Studios:** [IndieCade — Metal Revolution](https://www.indiecade.com/metal-revolution/).
- **Identificação de desenvolvedor/publicador — NEXT Studios:** [ModDB — Metal Revolution](https://www.moddb.com/games/metal-revolution).
- **Comunicado de encerramento dos servidores — Metal Revolution Team / GTarcade:** [Fórum oficial](https://forum.gtarcade.com/topic/143187).

## Estado de validação

O código foi preparado para .NET 8 WPF. A compilação e os testes finais precisam ser executados em Windows com o SDK instalado. Não declare uma versão como pronta para distribuição até que o workflow completo seja validado com os pacotes reais e a instalação-alvo.
