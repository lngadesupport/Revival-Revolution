# Revival Revolution

**Revival Revolution** é um projeto comunitário de preservação, inicialização e jogo local/multiplayer experimental para *Metal Revolution*. Este repositório reúne o launcher moderno para Windows e a documentação necessária para preparar, validar e iniciar uma instalação existente do jogo.

## Sobre o jogo original

*Metal Revolution* é um jogo de luta com temática cyberpunk e personagens robóticos, desenvolvido pelo estúdio **NEXT Studios**. A atribuição de desenvolvimento do jogo original é ao estúdio, não a uma pessoa individual. A operação original foi publicada/comunicada pela equipe de Metal Revolution/GTarcade. Consulte as referências no final deste documento.

**Criador/desenvolvedor do jogo original: NEXT Studios.** Conforme crédito informado pelo mantenedor, o criador original do projeto comunitário/distribuição Revival Revolution é identificado como **@NC_KuroRaiden** ([Twitter/X](https://x.com/NC_KuroRaiden)). O desenvolvimento e a manutenção desta versão do launcher WPF são atribuídos a **@lngadesupport** ([GitHub](https://github.com/lngadesupport)). Esses créditos não alteram a autoria do jogo original.

## O que este projeto faz

- Oferece um launcher Windows com interface WPF moderna, tema escuro e detalhes vermelhos.
- Prepara um runtime Python portátil para executar os scripts do pacote sem exigir instalação global de Python.
- Executa o fluxo offline existente por meio de `offline_launcher.py`.
- Reúne diagnósticos para arquivos, scripts, DLLs, runtime e instalação.
- Não possui atualizador automático e não baixa, instala nem oferece links para baixar o jogo. É necessário selecionar uma instalação já existente.
- Documenta a integração multiplayer comunitária, que permanece experimental e depende das bridges/scripts já presentes na distribuição.

## Estrutura

- `launcher/RevivalRevolutionLauncher/` — projeto C# / WPF (.NET 8).
- `launcher/RevivalRevolutionLauncher/Services/` — serviços de preparação do runtime Python, extração segura, configuração, inicialização e diagnóstico. O download do runtime auxiliar não inclui arquivos do jogo.
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

## Preservação e propriedade intelectual

Este é um projeto comunitário independente, não oficial e sem afiliação com NEXT Studios, GTarcade ou titulares de Metal Revolution. O nome e os elementos de *Metal Revolution* pertencem aos respectivos titulares. Não inclua no repositório nem redistribua executáveis, DLLs, assets, dados do jogo ou pacotes proprietários sem autorização expressa. O repositório deve conter código original do launcher, documentação e ferramentas que possam ser compartilhadas legalmente.

Os scripts, bridges e DLLs existentes da distribuição não devem ser substituídos sem verificar sua compatibilidade e preservar uma cópia do estado anterior.

## Créditos e referências

- **Criador original do projeto comunitário (crédito informado pelo mantenedor):** [@NC_KuroRaiden — Twitter/X](https://x.com/NC_KuroRaiden).
- **Launcher WPF — desenvolvimento e manutenção:** [@lngadesupport — GitHub](https://github.com/lngadesupport).

- **Jogo original — NEXT Studios:** [IndieCade — Metal Revolution](https://www.indiecade.com/metal-revolution/).
- **Identificação de desenvolvedor/publicador — NEXT Studios:** [ModDB — Metal Revolution](https://www.moddb.com/games/metal-revolution).
- **Comunicado de encerramento dos servidores — Metal Revolution Team / GTarcade:** [Fórum oficial](https://forum.gtarcade.com/topic/143187).

## Estado de validação

O código WPF foi compilado com sucesso pelo workflow Windows do GitHub Actions no commit registrado na PR. A compilação não substitui testes em Windows limpo, com os pacotes reais e a instalação-alvo. Não declare uma versão como pronta para distribuição até que esses testes sejam concluídos.


## Estado da versão 0.8.1

A linha de estabilização da versão 0.8.1 está em
`release/0.8.1-stabilization`. Ela reúne a central ONLINE com matchmaking
aleatório e por código, a API Ranked, a configuração inicial de hospedagem no
Render e a validação automatizada dos protocolos de connection code.

A atribuição de uma partida no servidor ainda não significa conexão P2P nem
início da luta. O ticket de sessão, a integração com a bridge, o relay UDP
próprio e os testes em duas máquinas permanecem pendentes. A API Render
planejada atende HTTP/HTTPS; o transporte UDP exige infraestrutura separada.

A versão 0.8.1 ainda não é uma release pública. A `main` permanece intacta;
a tag estável `v0.8.1` só deve ser criada após aprovação e integração explícita.
Consulte [docs/RELEASE_0.8.1.md](docs/RELEASE_0.8.1.md) para o escopo e os
critérios de liberação.
