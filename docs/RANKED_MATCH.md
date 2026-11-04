# Ranked Match — arquitetura e estado

## Identidade e perfil

A identidade é um UUID emitido pelo servidor e associada a um token aleatório.
O nickname é editável e não é único; o UUID não muda quando o jogador altera
nickname ou avatar. O cliente integrado ao jogo deverá guardar o token em área
privada do usuário, nunca em arquivos públicos ou logs.

## Recursos sociais disponíveis na API

- Perfil persistente com UUID, nickname e avatar PNG/JPEG/WebP (até 256 KiB).
- Busca de jogadores por nickname e consulta pública por UUID.
- Solicitações de amizade, aceite, recusa, lista de amigos e remoção.
- Bloqueio/desbloqueio de jogadores; bloquear encerra amizade e impede novos
  pedidos entre as duas contas.
- Presença aproximada, baseada na última atividade autenticada (90 segundos), com heartbeat.
- Leaderboard ordenado pelo rating atual.
- O nickname não é único; a identidade é sempre o UUID.
- Alterar nickname/avatar não altera UUID, rating ou relações de amizade.

Clãs não foram implementados: não há evidência técnica suficiente de um sistema
existente nem de um ponto de extensão seguro no jogo.

## Protocolo existente (main)

- `peer_session.py` usa UDP, HMAC-SHA256, nonce, sequência, slot e build.
- O segredo atual vem do connection code manual; não é uma credencial de Ranked
  emitida por servidor.
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

## Inspeção das DLLs existentes

A análise estática feita pelo workflow de CI confirmou:

- `MetalImGuiOverlay.dll` é um binário nativo x64 que importa Direct3D 11 e
  contém Dear ImGui 1.91.9b. Seus textos incluem “Metal Revolution / Community
  panel”, indicação de painel alternável por F1 e status do overlay.
- `MetalNetplayBridgeV8.dll` e `MetalOfflineBridgeImGuiV3.dll` são assemblies
  .NET/Mono x86. O primeiro contém `MetalNetplayController`, com métodos de
  reflexão como `Get`, `Field`, `Set`, `Call`, `ActiveUI`, `BuiltIn`,
  `Inspect` e `SelfTest`, e referências a `MetalFury.Logic.GameApp`,
  `NetworkHandlerHub` e `MetalFury.UI.BaseUI`.
- O bridge offline referencia `LoginScreen`, `MainMenuScreen`,
  `ServerListScreen` e `WaitingScreen`.
- `Assembly-CSharp-firstpass.dll` contém componentes de terceiros, como
  `NativeGallery` e efeitos visuais; não revelou o código do Community Panel.
- `Assembly-CSharp.dll` continua vazio (0 bytes). Não foi substituído.

Essa inspeção prova que existem pontos de reflexão e componentes de overlay,
mas não prova que o painel atual aceite novas abas nem que os tipos sociais
existam no jogo. O bridge já oferece um comando somente leitura chamado
`inspect`, que consulta as telas ativas em runtime sem precisar carregar uma
cópia estática de `Assembly-CSharp.dll`. O procedimento está documentado em
[RUNTIME_INSPECTION.md](RUNTIME_INSPECTION.md).
O relatório pode ser baixado na execução do workflow
“Inspect native game integration binaries”.

## Hospedar o backend dentro da rede Radmin

Para uma rede privada, um jogador pode hospedar a API no próprio Windows:
`scripts/start-radmin-ranked-host.ps1` detecta o IP virtual Radmin (26.x.x.x),
cria regras de firewall limitadas ao adaptador e à faixa Radmin e inicia a API
em `http://IP-RADMIN:8765`. O banco SQLite fica em
`%LOCALAPPDATA%\RevivalRevolution\RankedServer\ranked.sqlite3`.

- Todos os jogadores devem estar conectados à mesma rede Radmin e apontar o
  launcher para o IP Radmin do computador que está hospedando a API.
- TCP 8765 é usado pela API; o transporte `peer_session.py` usa UDP 8766,
  vincula o socket ao IP Radmin local e rejeita endpoints que não estejam na
  faixa 26.x.x.x. Códigos MR2/AutoPunch não são aceitos.
- O launcher aceita HTTP somente para endereços IPv4 da faixa virtual 26.0.0.0/8
  ou localhost. HTTP em endereços públicos continua bloqueado; fora da VPN,
  use HTTPS.
- Isso não cria matchmaking público na internet. Não usar Render, STUN,
  AutoPunch, relay de terceiros ou fallback público.
- O script não configura `RANKED_RESULT_SECRET`. A adjudicação continua
  desativada até existir uma autoridade confiável integrada ao jogo/bridge.

## Limites atuais

Este MVP ainda não é uma temporada competitiva pública:
- cadastro aberto, sem recuperação/anti-smurf/limites de criação;
- sem aceitação de partida, lease de reconexão ou administração;
- sem ticket de sessão P2P;
- ledger de ELO, divisões e histórico implementado no backend; aplicação de resultados reais ainda bloqueada até integrar uma autoridade confiável;
- sem integração visual dentro do jogo/Community Panel;
- sem validação autoritativa do resultado.

Não altere rating com base apenas no resultado informado pelos clientes.

## Camada competitiva adicionada na rodada atual

- Fila casual (`/v1/queue/join`) e fila Ranked (`/v1/ranked/queue/join`) separadas por modo e região; não há pareamento cruzado.
- Partidas por código são marcadas `private`; a fila casual é `casual`; somente a fila `ranked` pode gerar ELO.
- Temporada inicial `season-1`, com rating persistido por jogador.
- ELO K=32, deltas zero-sum, estatísticas de vitórias/derrotas/empates e pico de rating.
- Divisões Bronze, Prata, Ouro, Platina, Diamante e Mestre.
- Histórico de partidas e leaderboard sazonal por `/v1/ranked/me` e `/v1/ranked/leaderboard`.
- Endpoint interno de resultado protegido por `RANKED_RESULT_SECRET`, sem aceitar tokens bearer de jogadores; gravação transacional e idempotente.
- O launcher consulta o resumo Ranked e exibe divisão, rating e campanha no perfil.

**Não ativar o registro de resultados ainda:** o segredo deve permanecer somente no servidor e não deve ser distribuído aos jogadores. A API ainda não recebe uma prova confiável da bridge de que o resultado foi produzido por uma partida íntegra. O endpoint interno é uma fronteira de segurança para integração futura, não um mecanismo anti-cheat pronto.

## Próximas etapas

1. Entender o protocolo de comandos do `MetalNetplayBridgeV8` e seus pontos de
   inspeção/reflexão em runtime.
2. Definir um caminho suportado para exibir perfil/amigos/leaderboard dentro
   do jogo, usando o overlay já existente apenas se houver um mecanismo real
   de extensão; não presumir que o binário aceite plugins.
3. Ligar o cliente do jogo à API social com credencial protegida.
4. Criar ticket de partida e política de reconexão antes de abrir matchmaking
   público.
5. Projetar adjudicação confiável antes de aplicar ELO.
