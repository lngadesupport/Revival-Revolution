# Documentação técnica

## Objetivo

O Revival Revolution oferece uma interface Windows para selecionar uma instalação existente de Metal Revolution, preparar um runtime Python portátil, iniciar o fluxo offline original e executar diagnósticos. A instalação automática dos pacotes do jogo permanece desabilitada até que os arquivos ZIP possam ser autenticados por hashes publicados em um canal independente.

## Componentes existentes

A distribuição original inclui scripts Python, bridges gerenciadas e nativas, overlay ImGui e protocolos baseados em arquivos de comando/status. O launcher atua como interface e orquestrador desses componentes, sem reescrever seus protocolos por suposição.

## Arquivos de integração relevantes

- `offline_launcher.py`: inicialização offline e injeção das bridges existentes.
- `inject_offline.py` e `inject_overlay.py`: etapas de integração com o processo do jogo.
- `game-command.json` e `game-response.json`: protocolo de comandos/respostas.
- `netplay-status.txt`, `netplay-pid.txt`, `offline-status.txt`: estado do processo e da sessão.
- `peer_session.py`, `autopunch_transport.py`, `selection_sync.py`, `live_fighter.py`: módulos de conexão e sincronização multiplayer experimental.

## Arquitetura do launcher

- `MainWindow.xaml`: shell WPF, identidade visual, navegação lateral e dashboard.
- `MainWindow.xaml.cs`: coordenação de eventos, atalhos, estado ocupado e transições.
- `OnlineMatchWindow.xaml` / `.xaml.cs`: matchmaking aleatório pela Radmin, sala por código, perfil, aceite bilateral e ciclo de conexão P2P.
- `LauncherSettingsWindow.cs`: preferências organizadas por abas (Geral, Interface, Rede, Dados e Suporte).
- `LauncherDiagnosticsWindow.cs`: resultados de integridade com filtros, resumo e exportação de relatório.
- `DownloadService`: downloads HTTPS retomáveis com validação de redirecionamento.
- `ArchiveExtractionService`: extração ZIP com limites de tamanho, proteção contra traversal, links e colisões.
- `PythonRuntimeService`: runtime Python 3.13.17, verificação SHA-256, extração em staging e teste de inicialização antes da troca.
- `GameLaunchService`: validação da pasta e execução do script offline com logs.
- `LauncherDiagnosticService`: verificação de arquivos essenciais, dependências opcionais e avisos não bloqueantes de DLLs vazias.

## Segurança e distribuição

- Os pacotes de jogo não são baixados nem extraídos pelo launcher enquanto não houver hashes autenticados por canal independente. Um manifesto contido no próprio ZIP não autentica esse ZIP.
- O launcher não contém atualizador automático nem mecanismo para baixar/instalar o jogo. O download permanece restrito ao runtime Python auxiliar.
- Não incluir arquivos proprietários do jogo neste repositório sem autorização.
- A publicação deve ocorrer em Windows x64; a compilação automatizada não substitui testes em uma instalação limpa.

## Experiência da interface

- A navegação principal mantém apenas JOGAR (modo offline, matchmaking Radmin e sala por código) e SISTEMA (diagnósticos e configurações). O launcher não inclui loja, biblioteca de mods nem central social.
- Atalhos: Ctrl+1 (modo offline), Ctrl+2 (sala por código), Ctrl+3 (diagnósticos), Ctrl+4 (configurações); Esc retorna da sala ao dashboard.
- As configurações oferecem abas Geral, Interface, Rede, Dados e Suporte, com confirmação de inicialização, redução de movimento, logs locais e validação em tempo real do endpoint Radmin manual.
- O diagnóstico distingue dependências essenciais das opcionais, apresenta avisos não bloqueantes e permite filtrar e copiar o relatório.
- A tela ONLINE permite buscar adversários aleatórios na fila regional Radmin, acompanhar posição/cancelar a busca ou usar códigos de convite de seis caracteres. O pareamento cria uma sala Radmin P2P; ambos aceitam automaticamente e o anfitrião aleatório inicia o jogo automaticamente após o aceite bilateral.
- O escopo da interface é restrito às funções necessárias para jogar e manter o launcher; não há loja, conteúdo social, catálogo de mods ou links promocionais.

## Limitações conhecidas

- A compatibilidade depende da build do jogo, das bridges e dos arquivos da distribuição.
- O matchmaking só reúne jogadores conectados à mesma rede Radmin e ao mesmo servidor Ranked hospedado nela; não existe matchmaking global pela internet. O transporte UDP/rollback continua experimental e requer teste real em dois PCs, incluindo handshake, bridge, desconexão e reentrada.
- O runtime embeddable pode depender do Visual C++ Redistributable x64 na máquina de destino.
