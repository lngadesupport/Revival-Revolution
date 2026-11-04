# Revival Revolution Launcher — 0.8.1

Launcher comunitário para Windows 10/11 x64, desenvolvido em C# e WPF sobre
.NET 8.

## Escopo desta versão

- Interface WPF escura com identidade visual preta/vermelha.
- Seleção e persistência da pasta que contém `ufo.exe` e `ufo_Data/Managed`.
- Inicialização do fluxo offline original via `offline_launcher.py`, com logs
  em `Launcher/Logs`.
- Runtime Python 3.13.17 embeddable x64, com SHA-256, extração em staging e
  teste de inicialização.
- Diagnóstico de dependências essenciais offline e componentes opcionais
  multiplayer; a lista identifica a bridge correta `MetalNetplayBridgeV8.dll`
  e os scripts de sessão.
- Central ONLINE com matchmaking aleatório e salas privadas por código,
  usando a API Ranked e token no Gerenciador de Credenciais do Windows.

## Build

No Windows com .NET 8 SDK:

```powershell
dotnet restore .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj
dotnet build .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release --no-restore
```

Publicação self-contained:

```powershell
dotnet publish .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```

## Hospedagem Ranked pela Radmin VPN

A API Ranked pode ser executada no PC anfitrião usando
`scripts/start-radmin-ranked-host.ps1`. O processo vincula o servidor apenas
ao IPv4 Radmin local, mantém o SQLite em
`%LOCALAPPDATA%\RevivalRevolution\RankedServer\ranked.sqlite3` e cria uma
regra de firewall TCP 8765 limitada a origens Radmin (26.0.0.0/8). Os outros
launchers usam `http://IP-RADMIN-DO-ANFITRIAO:8765`. O cliente permite HTTP
remoto apenas para IPv4 26.x.x.x; HTTP público continua bloqueado.

Consulte [docs/RADMIN_VPN_SETUP.md](../../docs/RADMIN_VPN_SETUP.md) para
instalação, teste de conectividade e execução do peer UDP direto. O anfitrião
precisa permanecer ligado e conectado. Não abra portas no roteador.

## Distribuição e instalação do jogo

- O launcher não possui atualizador automático, verificação de versões ou aplicação de pacotes.
- O launcher não baixa, instala nem fornece links para baixar o jogo.
- Para jogar, selecione a pasta de uma instalação existente que contenha `ufo.exe` e `ufo_Data/Managed`.
- O runtime Python portátil necessário aos scripts auxiliares continua sendo preparado separadamente; isso não instala nem baixa o jogo.

## Estado multiplayer 0.8.1

A central ONLINE atribui partidas no servidor e integra aceite/recusa bilateral,
acompanha a confirmação do adversário e restaura uma atribuição ativa ao
reconectar a conta. A configuração Radmin permite API local e transporte UDP
direto pela VPN sem AutoPunch/STUN/relay externo. A janela ONLINE ainda não
inicia o processo P2P automaticamente nem abre a luta sozinha. Não anunciar
Ranked público nem aplicação de ELO nesta versão.

A interface ONLINE é uma janela WPF dedicada aberta pelo item de navegação;
ainda não é uma aba embutida na janela principal. Recursos sociais de perfil,
amigos e leaderboard devem permanecer dentro do jogo quando houver um ponto
de extensão técnico verificável.

