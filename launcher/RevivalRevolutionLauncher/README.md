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
- Atualização do launcher por manifesto HTTPS, hash SHA-256 do pacote e hashes
  SHA-256 por arquivo, com reparo seletivo de arquivos ausentes/divergentes,
  staging, backup e rollback durante a aplicação.
- Central ONLINE com matchmaking aleatório e salas privadas por código,
  usando API Ranked e token no Gerenciador de Credenciais do Windows.
- Instalação/reparo automático de pacotes do jogo permanece bloqueado até
  autenticação dos ZIPs por hashes confiáveis publicados em canal independente.

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

A central ONLINE atribui partidas no servidor, mas ainda não inicia P2P nem
abre a luta automaticamente. O transporte experimental existente depende de
bridge, scripts, AutoPunch/STUN e validação em duas máquinas. A API Render
planejada hospeda HTTP/HTTPS; o relay UDP precisa de infraestrutura compatível
separada. Não anunciar Ranked público nem aplicação de ELO nesta versão.

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
