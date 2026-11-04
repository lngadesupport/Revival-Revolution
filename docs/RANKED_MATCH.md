# Ranked Match — integração com Radmin VPN

## Identidade e perfil

A identidade é um UUID emitido pelo servidor e associada a um token aleatório.
O nickname é editável e não é único; o UUID não muda quando o jogador altera
nickname ou avatar. A credencial permanece no Gerenciador de Credenciais do
Windows. A interface do launcher contém apenas jogo, matchmaking, salas,
diagnósticos e configurações; não é um hub social.

## Recursos de conta e classificação

- Perfil persistente com UUID, nickname e avatar.
- Rating inicial 1000 e ledger competitivo por temporada.
- ELO K=32, deltas zero-sum e divisões Bronze, Prata, Ouro, Platina,
  Diamante e Mestre.
- Estatísticas V/D/E, total de partidas, pico de rating e histórico.
- Leaderboard da temporada, incluindo contas recém-registradas.
- GET /v1/ranked/me e GET /v1/ranked/leaderboard?limit=100.
- O rating só muda por submissão ao endpoint interno autenticado; o launcher
  e o bearer token do jogador não têm autoridade para declarar vencedores.

## Matchmaking e ciclo de partida

- Filas casual e Ranked separadas por queue_mode, região e janela de rating.
  As duas filas não se misturam.
- Região usada pelo launcher: sa-east.
- Salas privadas por código continuam disponíveis e permanecem fora do ELO.
- As atribuições aleatórias são persistidas com match_type=casual ou
  match_type=ranked; salas por código usam match_type=private.
- Aceite bilateral, expiração de 90 segundos para atribuições não aceitas e
  janela de sessão pronta de até duas horas.
- O launcher oferece busca casual, busca ranqueada e leaderboard na mesma
  tela que preserva o fluxo de sala privada.
- O host da partida é o jogador no slot 0; o slot 1 conecta como convidado.
- O anfitrião inicia o jogo pela mesma OnlineGameSessionService usada pelas
  salas privadas. O código MR1 é publicado no registro da partida; o convidado
  recebe o código pelo endpoint de match e inicia a sessão.

## Transporte multiplayer — Radmin VPN somente

- peer_session.py usa UDP, HMAC-SHA256, nonce, sequência, slot e build.
- O socket local é vinculado ao IPv4 Radmin do próprio computador (26.x.x.x).
- Porta peer fixa: UDP 8766. Código de conexão: MR1.
- O backend valida o código publicado: IPv4 26.x.x.x, UDP 8766, host do slot
  0, partida pronta e código imutável após publicação.
- STUN, AutoPunch, MR2, relay de terceiros e fallback pela internet estão
  desativados. Os dois jogadores precisam estar na mesma rede Radmin.
- RadminHostService hospeda a API no endereço Radmin do anfitrião pela porta
  TCP 8765. O launcher não usa o IP da API como endereço do peer remoto; cada
  cliente seleciona seu próprio adaptador Radmin.
- selection_sync.py sincroniza personagens, roupas, cenário e prontidão.
- live_fighter.py encaminha dados entre a sessão e a bridge local e compara
  os relatos de conclusão/checksum dos dois peers. Isso detecta divergências,
  mas não prova o resultado contra clientes modificados.
- fighter_input_test.py é um diagnóstico opcional e não é declarado testado.

## Bridge e jogo

A integração usa MetalNetplayBridgeV8.dll e a sessão existente para abrir a
tela VS e sincronizar a seleção. A compilação do launcher e os testes de
protocolo não substituem testes de gameplay real em dois computadores.

## Limites de segurança e ativação competitiva

- O endpoint interno de resultado exige X-Ranked-Authority e
  RANKED_RESULT_SECRET. Sem segredo, responde 503; com bearer de jogador,
  responde 403.
- O segredo nunca deve ser distribuído no launcher nem aos jogadores.
- O ledger rejeita partidas que não sejam ranked, resultados conflitantes e
  reutilização de authority_event_id.
- Ainda não existe autoridade de jogo independente e confiável integrada à
  bridge para provar o vencedor. Por isso, não configurar o segredo nem enviar
  resultados a partir de clientes.
- A confirmação de prontidão e o checksum dos peers não constituem prova
  antitrapaça. O Ranked não deve ser anunciado como competitivo ativo até a
  adjudicação confiável estar implementada.
- O host privado atual usa SQLite persistente local. Produção requer TLS para
  API fora da rede privada, backups, logs, monitoramento e política de contas.
- O teste automatizado valida protocolo e lógica; falta executar duas máquinas
  Windows reais na mesma rede Radmin e validar handshake, firewall, queda,
  reentrada e partida completa.

## Próximas etapas técnicas

1. Emitir ticket efêmero vinculado a match, jogadores, slots, build e protocolo,
   com expiração e consumo único; integrar a validação ao peer handshake.
2. Integrar autoridade confiável à bridge/jogo para adjudicar resultado sem
   aceitar vencedor declarado pelo cliente.
3. Testar o host Windows, API TCP 8765, UDP 8766, matchmaking, sala privada e
   partida real em dois PCs.
4. Testar abandono, desconexão, reconexão, encerramento e idempotência do
   resultado; garantir que partidas interrompidas não alterem rating.
5. Preparar TLS, banco persistente gerenciado, backups, logs e operação antes
   de qualquer release pública.

Esta branch é uma integração de desenvolvimento. Não altera main, não
mescla a PR #4 e não publica uma release.


## Tickets de sessão (integração em andamento)

A API agora emite um par de tickets aleatórios para cada partida Ranked pronta.
O par é vinculado ao match_id, aos dois jogadores, aos slots, ao build e ao
protocolo, com validade de até 30 minutos. O launcher entrega as credenciais
ao processo peer pelo ambiente, e o handshake Radmin rejeita ticket ausente,
expirado, de outra partida ou de slot/build/protocolo incompatível.

Este mecanismo vincula o transporte da versão oficial do cliente à atribuição
do matchmaking; não é uma assinatura verificável dentro da bridge e não é
anti-cheat. A bridge nativa está disponível somente como DLL no repositório.
Não existe fonte para adicionar nela uma validação independente. Por isso,
o resultado final e o ELO continuam sem autoridade confiável e não devem ser
ativados em produção.
