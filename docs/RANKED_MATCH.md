# Ranked Match — arquitetura e estado da versão 0.8.1

## Identidade e perfil

A identidade é um UUID emitido pelo servidor e associada a um token aleatório.
O nickname é editável e não é único; o UUID não muda quando o jogador altera
nickname ou avatar. A credencial deve ser armazenada em área privada do cliente.
A experiência social pretendida (perfil, amigos, presença e leaderboard) é
dentro do jogo, não em uma tela social do launcher.

## Recursos existentes na API

- Perfil persistente com UUID, nickname e avatar PNG/JPEG/WebP (até 256 KiB).
- Busca por nickname e consulta por UUID.
- Solicitações de amizade, aceite, recusa, lista de amigos e remoção.
- Bloqueio/desbloqueio; bloquear remove amizade e impede novos pedidos.
- Presença aproximada baseada em atividade autenticada.
- Leaderboard ordenado pelo rating atual.
- Clãs não implementados: não há ponto de extensão técnico confirmado.

## Matchmaking 0.8.1

- Fila aleatória 1v1 com região e janela de rating que se expande com a espera.
- Sala privada com código de seis caracteres, validade de 15 minutos,
  uso único, cancelamento pelo anfitrião e limite de tentativas por conta.
- Ambas as modalidades criam uma atribuição persistida no SQLite.
- A atribuição não inicia P2P e atualmente permanece `assigned`; falta
  implementar o ciclo de aceite/timeout/fim para permitir partidas sucessivas.

## Transporte multiplayer já existente

- `peer_session.py` usa UDP, HMAC-SHA256, nonce, sequência, slot e build.
- O segredo é derivado de connection code MR1/MR2 manual; não é ticket Ranked.
- `autopunch_transport.py` integra STUN e um relay externo configurável
  (padrão atual: `delthas.fr:14763`). Não devemos depender desse serviço
  externo como infraestrutura oficial; precisamos avaliar e operar nosso
  próprio relay UDP.
- `selection_sync.py` tem proposta/aceitação/commit para sincronizar
  personagens, roupas, cenário e prontidão.
- `live_fighter.py` encaminha dados entre a sessão e a bridge local e compara
  os relatos de conclusão/checksum dos dois peers. Isso ajuda a detectar
  divergências, mas não prova o resultado contra clientes modificados.
- `peer_session.py` agora trata `fighter_input_test.py` como diagnóstico
  opcional, porque esse arquivo não está presente na árvore versionada. A
  função legada não deve ser declarada testada.
- A amostra versionada `game-response.json` registra
  `transport_connected=false` e `input_authority_enforced=false`.

## Bridge e jogo

A inspeção estática identificou `MetalNetplayBridgeV8.dll` como assembly
.NET/Mono x86 com pontos de reflexão e comandos de inspeção. Isso não prova
que o jogo exponha telas sociais nem que a bridge aceite um novo fluxo Ranked.
`Assembly-CSharp.dll` continua vazio (0 bytes) e não foi substituído; não há
cópia alternativa disponível no projeto.

## Limites de segurança

- Cadastro aberto sem recuperação de conta nem controles suficientes contra
  abuso/contas alternativas.
- Sem aceite bilateral, ticket P2P, lease de reconexão ou administração.
- Sem resultado autoritativo. Não alterar ELO por resultado informado pelo
  cliente.
- O checksum final informado pelos peers não é uma fonte independente.
- O relay AutoPunch padrão é de terceiros e precisa ser substituído/validado.
- O deploy da API e o transporte UDP são componentes separados.

## Próximas etapas técnicas

1. Ciclo de vida de partidas: aceite bilateral, timeout, abandono, reconexão e
   liberação de jogadores para a próxima partida.
2. Ticket efêmero, vinculado a match/players/slots/build/protocolo, com
   expiração e consumo único.
3. Integração ticket -> peer_session -> bridge, sem reutilizar connection code
   manual como autorização competitiva.
4. Relay UDP próprio e teste de conectividade em NATs distintos.
5. Inspeção em runtime da bridge com o jogo aberto e confirmação do fluxo
   `peer_prepare`, `open_local_vs`, seleção e `live_start`.
6. Adjudicação de resultados e rating apenas depois de um mecanismo confiável.
7. Interface social dentro do jogo somente após confirmar um ponto de extensão
   suportado.

A versão 0.8.1 é uma base de integração/estabilização, não uma release pública
de Ranked competitivo.
