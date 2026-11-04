# Revival Revolution Launcher — 0.8.1

Launcher comunitário para Windows 10/11 x64, desenvolvido em C# e WPF sobre
.NET 8.

## Escopo desta versão

- Interface WPF com dashboard editorial, navegação agrupada e identidade visual escura com acento vermelho.
- Sala por código integrada ao conteúdo principal, sem abrir uma segunda janela sobre o dashboard.
- Configurações em abas Geral, Interface, Rede, Dados e Suporte, com preferência de acessibilidade para reduzir animações.
- Central de diagnósticos com filtros por criticidade, resumo de integridade, avisos não bloqueantes, cópia de relatório e acesso aos logs.
- Central de comunidade com subabas Visão geral, Biblioteca e Conteúdo local; a biblioteca informa claramente que catálogo e instalação de mods ainda não estão integrados.
- Atalhos de navegação: Ctrl+1 (offline), Ctrl+2 (sala), Ctrl+3 (diagnósticos), Ctrl+4 (configurações), Ctrl+5 (comunidade) e Esc (voltar da sala).
- Seleção e persistência da pasta que contém `ufo.exe` e `ufo_Data/Managed`.
- Inicialização do fluxo offline original via `offline_launcher.py`, com logs
  em `Launcher/Logs`.
- Runtime Python 3.13.17 embeddable x64, com SHA-256, extração em staging e
  teste de inicialização.
- Diagnóstico de dependências essenciais offline e componentes opcionais
  multiplayer; a lista identifica a bridge correta `MetalNetplayBridgeV8.dll`
  e os scripts de sessão.
- Central de salas privadas por código, com conta e token protegidos pelo
  Gerenciador de Credenciais do Windows. Não há fila pública de matchmaking nem
  ranking competitivo público nesta entrega.

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

A central de sala privada cria e aceita convites por código, acompanha o estado
do adversário e restaura uma sala ou atribuição ativa ao reconectar a conta. A configuração Radmin permite API local e transporte UDP
direto pela VPN sem AutoPunch/STUN/relay externo. A janela ONLINE ainda não
inicia o processo P2P automaticamente nem abre a luta sozinha. Não anunciar
Ranked público nem aplicação de ELO nesta versão.

A sala por código é um UserControl WPF embutido no conteúdo principal do
launcher. A interface mostra o estado de disponibilidade do servidor, valida o
formato do código de convite localmente e recupera a consulta da sala quando uma
solicitação de cancelamento falha. O fluxo de conta, convite e confronto permanece separado do dashboard,
mas compartilha a mesma janela e o mesmo shell de navegação. Recursos sociais
de perfil, amigos e leaderboard não são simulados pela interface: só devem ser
adicionados quando houver serviço e integração técnica verificáveis.

