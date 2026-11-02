# Ranked Match — arquitetura e estado

## Identidade

O launcher usa o hostname do Windows apenas como nome público. A identidade
competitiva é um UUID emitido pelo servidor e associado a um token aleatório.
O token deve ser mantido no perfil privado do usuário e nunca ser registrado
em logs ou incluído no repositório.

## Etapas

1. Identidade, perfil e fila 1v1 persistidos em SQLite.
2. Integração WPF: registrar, restaurar credencial local, consultar perfil,
   entrar/sair da fila e apresentar a partida encontrada.
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
- sem integração com o launcher;
- sem validação autoritativa do resultado.

Não altere rating com base apenas no resultado informado pelos clientes.
