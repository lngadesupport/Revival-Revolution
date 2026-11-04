# Revival Revolution Launcher — 0.8.1

Launcher do jogo para Windows 10/11 x64, desenvolvido em C# e WPF sobre
.NET 8.

## Escopo desta versão

- Interface WPF com dashboard editorial, navegação agrupada e identidade visual escura com acento vermelho.
- Sala online integrada ao conteúdo principal, com matchmaking aleatório Radmin e entrada por código.
- Launcher focado em localizar e iniciar o jogo, buscar adversários aleatórios pela Radmin, acessar salas por código, diagnósticos e configurações; não inclui loja, biblioteca de mods ou central social.
- Configurações em abas Geral, Interface, Rede, Dados e Suporte, com validação em tempo real do endpoint Radmin e preferência de acessibilidade para reduzir animações.
- Central de diagnósticos com filtros por criticidade, resumo de integridade, avisos não bloqueantes, cópia de relatório com caminhos pessoais redigidos e acesso aos logs.
- Atalhos de navegação: Ctrl+1 (offline), Ctrl+2 (sala), Ctrl+3 (diagnósticos), Ctrl+4 (configurações) e Esc (voltar da sala).
- Seleção e persistência da pasta que contém `ufo.exe` e `ufo_Data/Managed`.
- Inicialização do fluxo offline original via `offline_launcher.py`, com logs
  em `Launcher/Logs`.
- Runtime Python 3.13.17 embeddable x64, com SHA-256, extração em staging e
  teste de inicialização.
- Diagnóstico de dependências essenciais offline e componentes opcionais
  multiplayer; a lista identifica a bridge correta `MetalNetplayBridgeV8.dll`
  e os scripts de sessão.
- Conta e token protegidos pelo Gerenciador de Credenciais do Windows; fila
  regional de matchmaking Radmin, posição e cancelamento de busca; salas
  privadas por código. Não há matchmaking global pela internet nem ranking
  competitivo público nesta entrega.

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

A tela ONLINE oferece matchmaking aleatório e sala por código. O matchmaking
pareia jogadores conectados ao mesmo servidor Ranked pela mesma rede Radmin,
cria uma sala P2P para o par, aceita a atribuição nos dois clientes e inicia
automaticamente o jogo no anfitrião quando ambos confirmam. A sala por código
mantém o início manual pelo anfitrião. A configuração Radmin usa API local e
transporte UDP direto pela VPN, sem AutoPunch/STUN/relay externo. Não anunciar
matchmaking global pela internet nem aplicação de ELO nesta versão.

A sala por código é um UserControl WPF embutido no conteúdo principal do
launcher. A interface mostra o estado de disponibilidade do servidor, valida o
formato do código de convite localmente e recupera a consulta da sala quando uma
solicitação de cancelamento falha. O fluxo de conta, convite e confronto permanece separado do dashboard,
mas compartilha a mesma janela e o mesmo shell de navegação. Não há recursos sociais, loja, biblioteca de mods, perfil público, amigos ou
leaderboard na interface do launcher.

