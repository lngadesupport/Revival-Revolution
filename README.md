# Revival Revolution

Projeto comunitário para o launcher moderno do Metal Revolution modificado Revival Revolution.

## Launcher WPF (versão 0.6.0)

O código inicial do launcher está em `launcher/RevivalRevolutionLauncher/`.

### Requisitos de desenvolvimento
- Windows 10/11 x64
- .NET 8 SDK para compilar/publicar
- Visual Studio 2022 com workload Desktop development with .NET, ou CLI do .NET

### Compilar
```powershell
dotnet build .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release
```

### Publicar sem exigir .NET instalado no computador do jogador
```powershell
dotnet publish .\launcher\RevivalRevolutionLauncher\RevivalRevolutionLauncher.csproj -c Release -r win-x64 --self-contained true
```

A publicação self-contained inclui o runtime .NET necessário. O SDK continua necessário apenas na máquina de desenvolvimento. O modo single-file não é forçado para evitar incompatibilidades com componentes nativos/WPF.

## Escopo atual
- Interface WPF horizontal com tema escuro/vermelho.
- Instalação dos pacotes Pixeldrain e extração ZIP com validação de caminhos.
- Download retomável/cancelável.
- Runtime Python embeddable x64.
- Verificação SHA-256 e reparo seletivo quando o manifesto é reconhecido.
- Diagnósticos de arquivos do jogo, scripts, DLLs e runtime.
- Atualizador do launcher por manifesto HTTPS (precisa configurar um host real antes de usar).

## Avisos importantes
- Os pacotes Pixeldrain e o manifesto de integridade precisam ser conferidos contra os arquivos reais da distribuição antes de uma publicação pública.
- O atualizador remoto não funcionará enquanto `launcher-update.json` contiver o domínio de exemplo.
- Não distribua arquivos proprietários do jogo sem autorização.
- O multiplayer do projeto-base permanece experimental; o launcher não transforma isso em funcionalidade garantida.
- A compilação e os testes WPF precisam ser executados em Windows com .NET 8 SDK. O ambiente de preparação não tem o SDK e não permite declarar que a compilação foi validada.

## Integração com os componentes existentes
O launcher preserva os nomes e arquivos de configuração legados esperados pelos scripts Python. Não substitua bridges, DLLs ou scripts do jogo sem verificar a compatibilidade com a distribuição em uso.
