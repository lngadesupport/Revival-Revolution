# Ranked Match — arquitetura e estado

## Identidade

A identidade é um UUID emitido pelo servidor e associada a um token aleatório.
O nickname é editável e não é único; UUID não muda quando o jogador altera
nickname ou avatar. O cliente dentro do jogo deve guardar o token em área
privada do usuário, nunca em arquivos públicos ou logs.

## Etapas

1. Identidade, perfil e fila 1v1 persistidos em SQLite.
2. Integração do cliente dentro do jogo/Community Panel com a API social e Ranked;
   o launcher não apresenta telas de perfil, amigos ou ranking.
3. Ticket de partida de uso limitado para autorizar a abertura do P2P.
4. Resultado verificável pela bridge e política de desconexão/reconexão.
5. ELO, histórico, leaderboard, operação em VPS Linux e monitoramento.

## Protocolo existente (main)

- `peer_session.py` usa UDP, HMAC-SHA256, nonce, sequência, slot e build.
- O segredo atual vem do connection code manual; não é uma credencial de
  Ranked emitida por servidor.
- A seleção tem proposta/aceitação/commit e sincroniza catálogo e setup.
- `live_fighter.py` compara conclusão/checksum informados pelos dois peers.
  Isso ajuda a detectar desync, mas não prova resultado contra clientes
  modificados.
- `peer_session.py` importa `fighter_input_test.FighterInputTest`, mas
  `fighter_input_test.py` não aparece na árvore atual de `main`. O módulo
  não pode iniciar até restaurar essa dependência ou refatorar o fluxo com
  base na implementação correta.
- A amostra versionada `game-response.json` diz
  `transport_connected=false` e `input_authority_enforced=false`.
- A documentação técnica atual classifica multiplayer/rollback como
  experimental e requer testes em dois PCs/redes.

## API de desenvolvimento

Veja `ranked_server/README.md`. O serviço usa biblioteca padrão e SQLite.
O padrão é escutar somente em loopback. Em VPS, usar proxy TLS e firewall.

## Limites atuais

Este MVP não é ainda uma temporada competitiva pública:
- cadastro aberto, sem recuperação/anti-smurf/limites de criação;
- sem aceitação de partida, lease de reconexão ou administração;
- sem ticket de sessão P2P;
- sem aplicação de ELO/vitória/derrota;
- sem integração visual dentro do jogo/Community Panel;
- sem validação autoritativa do resultado.

Não altere rating com base apenas no resultado informado pelos clientes.

## Recursos sociais implementados no backend (sem integração visual ainda)

- Perfil com UUID permanente, nickname editável e avatar base64 limitado a 256 KiB.
- Busca de jogadores por nickname.
- Solicitações de amizade, aceite, lista de amigos e remoção.
- Presença aproximada por última atividade (janela de 90 segundos), com heartbeat autenticado.\n- Recusa de pedidos, bloqueio e desbloqueio de jogadores; bloquear remove amizade existente.
- Endpoint de leaderboard por rating.
- O nickname não é único; a identidade é sempre o UUID.
- Alterar nickname/avatar não altera UUID, rating ou relações de amizade.
- Clãs foram deliberadamente deixados de fora: não há evidência técnica suficiente
  de um sistema de clãs existente nem de um ponto de integração no jogo.
- O leaderboard ainda não representa temporada competitiva: o rating permanece
  inicial até existir validação confiável de resultados.
