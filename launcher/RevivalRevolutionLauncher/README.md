# Revival Revolution Launcher — 0.8.4 (branch de desenvolvimento)

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
- Atualização do launcher por manifesto HTTPS, hash SHA-256 do pacote e hashes
  SHA-256 por arquivo, com reparo seletivo de arquivos ausentes/divergentes,
  staging, backup e rollback durante a aplicação.
- Central ONLINE com matchmaking aleatório e salas privadas por código,
  usando a API Ranked e token no Gerenciador de Credenciais do Windows.
- Instalação/reparo automático de pacotes do jogo permanece bloqueado até
  autenticação dos ZIPs por hashes confiáveis publicados em canal independente.

## Permissões e diagnóstico de inicialização

O executável solicita elevação pelo UAC do Windows (`requireAdministrator`) antes de abrir a interface. A elevação é usada para operações administrativas do launcher, como configurar a regra de firewall e preparar o servidor privado Radmin. Se o usuário cancelar o UAC, o Windows não inicia o processo.

Erros não tratados na inicialização WPF são registrados e apresentados em uma janela de erro, em vez de desaparecerem sem explicação. O diagnóstico persistente fica em `%LOCALAPPDATA%\\RevivalRevolution\\Logs\\startup-diagnostics.log`. O arquivo registra versão/runtime, estado de elevação, etapas da criação da janela e falhas durante a preparação automática do host Radmin. Falhas recuperáveis do Radmin são registradas sem encerrar a interface.

Como o processo do jogo é iniciado pelo launcher elevado, o processo filho também pode herdar o nível de integridade elevado do Windows. Isso precisa ser validado com a instalação real do jogo e com a proteção oficial antes de considerar a build estável.

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

## Segurança e distribuição

- O manifesto de atualização aponta para GitHub Releases e o pacote ZIP é
  validado por SHA-256 antes da extração.
- O hash detecta corrupção, mas manifesto e hash no mesmo canal não equivalem
  a assinatura digital independente.
- O pacote de atualização contém somente arquivos do launcher. Não incluir
  executáveis, DLLs, assets ou dados proprietários do jogo.
- A instalação/reparação da distribuição do jogo segue bloqueada enquanto não
  houver hashes autenticados por canal independente.
- O atualizador e o runtime precisam ser exercitados em Windows limpo antes
  da publicação estável.

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

## Publicar uma atualização

1. Concluir a validação de código e os testes de Windows limpo e duas máquinas.
2. Integrar o commit aprovado à `main`; a branch de estabilização não é uma
   release pública.
3. Criar e enviar a tag `v0.8.1` sobre o commit já integrado à `main`.
4. O workflow `.github/workflows/publish-launcher-release.yml` compila o
   launcher self-contained Windows x64, gera o ZIP, calcula SHA-256 e publica
   o ZIP e o manifesto como assets da GitHub Release.
5. Não criar a tag estável enquanto os critérios de validação estiverem
   pendentes.

O launcher não redistribui o jogo nem substitui os scripts/bridges originais.
