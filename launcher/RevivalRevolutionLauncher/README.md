# Revival Revolution Launcher — 0.8.0 (teste Radmin)

Build experimental para Windows 10/11 x64, desenvolvido em C# e WPF sobre
.NET 8. Esta build deriva da linha técnica 0.8.1 e mantém o backend Ranked
0.8.1; o número 0.8.0 identifica somente o pacote de teste do launcher.

## Incluído

- Interface WPF escura com identidade visual preta/vermelha.
- Seleção e persistência da pasta que contém `ufo.exe` e `ufo_Data/Managed`.
- Inicialização do fluxo offline original via `offline_launcher.py`, com logs
  em `Launcher/Logs`.
- Runtime Python 3.13.17 embeddable x64, com SHA-256, extração em staging e
  teste de inicialização.
- Diagnóstico de dependências offline e componentes multiplayer.
- Atualização do launcher por manifesto HTTPS e hashes SHA-256 por arquivo.
- Central ONLINE com matchmaking aleatório e salas privadas por código,
  usando a API Ranked e token no Gerenciador de Credenciais do Windows.
- Endpoint HTTP remoto permitido somente para IPv4 virtual Radmin 26.x.x.x,
  além de HTTPS e loopback.

## Conectar ao servidor de teste Radmin

1. Conecte o PC à mesma rede Radmin VPN do anfitrião.
2. Abra o launcher e entre na janela ONLINE.
3. No campo de endereço da API, informe `http://IP-RADMIN-DO-ANFITRIAO:8765`.
4. Faça cadastro/login com uma conta própria em cada computador.

O anfitrião precisa manter a API aberta e o PC ligado. Consulte
[docs/RADMIN_VPN_SETUP.md](../../docs/RADMIN_VPN_SETUP.md).

## Limites conhecidos do teste

- O backend continua reportando versão de API 0.8.1; isso é esperado.
- Matchmaking e salas são funções do backend HTTP.
- A janela ONLINE ainda não inicia automaticamente `peer_session.py`, não
  emite ticket P2P e não abre a luta sozinha.
- Não tratar confirmação de fim de partida como resultado competitivo
  autoritativo; esta build não deve atualizar ELO com base em relato do cliente.
- Não representa release estável nem substitui o launcher de produção.

## Build local

No Windows com .NET 8 SDK:

```powershell
dotnet restore .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj
dotnet publish .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false
```
