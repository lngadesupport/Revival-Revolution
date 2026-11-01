# Documentação técnica

## Objetivo
O Revival Revolution moderniza o acesso a uma distribuição comunitária de Metal Revolution: inicialização offline, preparação do Python portátil, instalação/reparo de arquivos e diagnóstico da distribuição.

## Componentes existentes
A distribuição original inclui scripts Python, bridges gerenciadas e nativas, overlay ImGui e protocolos baseados em arquivos de comando/status. O launcher deve atuar como interface e orquestrador desses componentes, sem reescrever seus protocolos por suposição.

## Arquivos de integração relevantes
- `offline_launcher.py`: inicialização offline e injeção das bridges existentes.
- `inject_offline.py` e `inject_overlay.py`: etapas de integração com o processo do jogo.
- `game-command.json` e `game-response.json`: protocolo de comandos/respostas.
- `netplay-status.txt`, `netplay-pid.txt`, `offline-status.txt`: estado do processo e da sessão.
- `peer_session.py`, `autopunch_transport.py`, `selection_sync.py`, `live_fighter.py`: módulos de conexão e sincronização multiplayer experimental.

## Limitações conhecidas
- A compatibilidade depende da build do jogo, das bridges e dos arquivos da distribuição.
- O multiplayer/rollback é experimental; testes em dois PCs e diferentes redes ainda são necessários.
- Não incluir os arquivos proprietários do jogo neste repositório sem autorização.
- A validação final da interface e da publicação deve ocorrer em Windows x64.
