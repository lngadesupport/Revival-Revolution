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
- Presença aproximada, baseada na última atividade autenticada (90 segundos).
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
existam no jogo. O próximo passo é analisar o protocolo de comandos do bridge
e, se possível, usar o próprio mecanismo de reflexão em execução para descobrir
as classes reais sem depender de uma cópia estática de `Assembly-CSharp.dll`.

O relatório pode ser baixado na execução do workflow
“Inspect native game integration binaries”.

## Limites atuais

Este MVP ainda não é uma temporada competitiva pública:
- cadastro aberto, sem recuperação/anti-smurf/limites de criação;
- sem aceitação de partida, lease de reconexão ou administração;
- sem ticket de sessão P2P;
- sem aplicação de ELO/vitória/derrota;
- sem integração visual dentro do jogo/Community Panel;
- sem validação autoritativa do resultado.

Não altere rating com base apenas no resultado informado pelos clientes.

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
