# Ranked API — base de desenvolvimento 0.8.1

A API fornece identidade persistente, perfil social, presença, leaderboard,
fila 1v1 e salas privadas por código. É Python padrão + SQLite; não requer
dependências externas.

## Inicialização automática no Windows

A distribuição WPF inclui este pacote Python. No PC anfitrião, o launcher instala o runtime portátil, prepara as regras de firewall uma única vez e registra a tarefa oculta `Revival Revolution Ranked Host` no Agendador de Tarefas. A tarefa inicia no logon, aguarda o IPv4 Radmin configurado e mantém a API em segundo plano. O banco e os logs ficam em `%LOCALAPPDATA%\\RevivalRevolution\\RankedServer`. Não é necessário executar os scripts PowerShell manualmente a cada reinicialização.

## Executar localmente

Na raiz do repositório:

```bash
python -m ranked_server --host 127.0.0.1 --port 8765 --database ranked.sqlite3
```

O bind padrão é loopback. Em hospedagem, use o bind e a porta fornecidos pela
plataforma e mantenha HTTPS na borda. Não exponha o servidor HTTP de
desenvolvimento diretamente à internet sem uma camada de produção apropriada.

## Endpoints

- `GET /health` — saúde pública e versão da API.
- `POST /v1/register` — cria UUID, rating inicial e token bearer.
- `GET /v1/me` — perfil autenticado.
- `POST /v1/account/rotate-token` — substitui o token e invalida o anterior.
- `POST /v1/profile` — atualiza nickname e/ou avatar PNG/JPEG/WebP (até 256 KiB).
- `GET /v1/players/{player_id}` e `/avatar` — consulta perfil/foto por UUID.
- `GET /v1/players/search?q=...` — busca por nickname.
- `GET /v1/friends`, `POST /v1/friends/request|accept|decline|remove` — amizades.
- `GET /v1/blocks`, `POST /v1/players/block|unblock` — bloqueios.
- `POST /v1/presence/heartbeat` — atividade autenticada.
- `GET /v1/leaderboard?limit=100` — classificação pelo rating atual.
- `POST /v1/queue/join`, `GET /v1/queue`, `POST /v1/queue/leave` — fila aleatória.
- `GET /v1/match` — atribuição ativa do jogador; também reavalia a fila regional durante o polling para que a janela de rating avance sem depender da chegada de um terceiro jogador.
- `POST /v1/match/accept|decline` — aceite bilateral ou recusa da atribuição. O aceite inicial expira em 90 segundos; após ambos aceitarem, a partida fica pronta por até duas horas.
- `POST /v1/match/finish` — confirmação bilateral de encerramento, sem alterar rating. É apenas uma transição administrativa de estado, não uma prova de resultado nem de integridade do jogo.
- `POST /v1/rooms/create` — cria código privado de seis caracteres, com validade de 15 minutos.
- `POST /v1/rooms/join` — valida código, limita tentativas a 20 por minuto por conta e atribui a mesma partida aos dois jogadores.
- `GET /v1/rooms/current` — estado da sala do anfitrião.
- `POST /v1/rooms/cancel` — cancela sala ainda aguardando convidado.

As rotas autenticadas usam `Authorization: Bearer TOKEN`. O banco armazena
somente o SHA-256 do token. A credencial do jogador deve ficar em armazenamento
privado do cliente, nunca em logs ou arquivos públicos. A API social não é
apresentada como interface social do launcher; a integração visual pretendida
é dentro do jogo, ainda pendente de um ponto de extensão verificável.

## Ranked e transporte de partidas Radmin

- POST /v1/ranked/queue/join, GET /v1/ranked/queue e
  POST /v1/ranked/queue/leave usam uma fila separada da fila casual.
- GET /v1/ranked/match consulta somente partidas Ranked; GET /v1/match
  preserva as partidas casuais e privadas existentes.
- POST /v1/ranked/match/accept|decline e /finish controlam o ciclo de aceite
  e encerramento sem misturar partidas casuais.
- POST /v1/ranked/match/connection-code aceita MR1 somente do host (slot 0)
  de uma partida pronta. O código deve apontar para IPv4 Radmin 26.x.x.x e
  UDP 8766; depois de publicado, não pode ser substituído.
- GET /v1/ranked/me e GET /v1/ranked/leaderboard?limit=100 retornam rating,
  divisão, V/D/E, histórico e classificação da temporada.
- O endpoint interno POST /internal/v1/matches/{match_id}/result exige
  X-Ranked-Authority comparado em tempo constante com RANKED_RESULT_SECRET.
  Não aceita bearer token de jogador. Sem segredo configurado, responde 503.
- O launcher não contém nem recebe RANKED_RESULT_SECRET. Não configurar o
  segredo até existir uma autoridade confiável integrada ao jogo/bridge.

## Rede privada Radmin

Todos os jogadores devem entrar na mesma rede Radmin. O host executa a API
TCP 8765 vinculada ao seu IP virtual Radmin; o tráfego peer usa UDP 8766
diretamente entre os IPs Radmin dos jogadores. STUN, AutoPunch, MR2, relay
externo e fallback público não fazem parte do transporte suportado. O IP da
API é o endereço do servidor, não o endereço peer do host da partida.

## Limitações conhecidas da base 0.8.1

- O matchmaking aleatório e por código cria atribuições no banco; ainda não
  inicia uma sessão P2P.
- O aceite/recusa e o encerramento bilateral já possuem estados persistidos e expiração; ainda não há ticket de transporte, lease de reconexão ou recuperação após queda. O cliente ainda precisa integrar essas rotas antes de usar o fluxo completo.
- A confirmação bilateral de encerramento é declaratória: não prova que a luta ocorreu nem qual foi o vencedor. Ela não altera rating.
- O cliente atual de P2P usa connection code manual. A API Ranked ainda não
  negocia esse segredo nem autoriza o transporte.
- `peer_session.py` agora pode ser importado e os códigos MR1/MR2 podem ser
  testados sem Windows. O diagnóstico antigo `fighter_input_test.py` é
  opcional; esse arquivo não está presente na árvore versionada.
- O resultado/checksum informado pelos dois clientes não é prova autoritativa
  contra clientes modificados. Não aplicar ELO com base apenas nesses relatos.
- Cadastro aberto ainda precisa de limites de abuso, recuperação de conta e
  controles administrativos.
- Clãs não foram implementados: não há suporte técnico verificável no jogo.
- O deploy requer TLS, backups, monitoramento, política de privacidade e
  proteção contra abuso. O serviço SQLite com disco persistente deve operar
  como instância única.

Esta base é para integração e testes controlados, não uma temporada Ranked
pública nem uma certificação de produção.


### Tickets efêmeros do transporte Ranked

- Após os dois aceites, cada participante chama POST /v1/ranked/match/ticket.
- A API emite um par de capacidades aleatórias, limitado à partida, aos dois
  UUIDs, aos slots, ao build do peer e ao protocolo. A validade máxima é de
  30 minutos e o par é renovado após expirar.
- O launcher injeta os tickets no processo peer por variáveis de ambiente;
  eles não são argumentos de linha de comando nem aparecem no status público.
- O handshake UDP exige o ticket complementar exato, o mesmo match_id, slots
  opostos, UUIDs recíprocos, build/protocolo idênticos e validade temporal.
  Isso reduz mistura de sessões e replay acidental entre partidas.
- Os tickets não são uma assinatura pública verificável pela DLL e não
  protegem contra cliente modificado. A bridge nativa está distribuída como
  binário sem fonte versionado; portanto, não há validação independente
  dentro do jogo nem prova autoritativa de vencedor. ELO permanece bloqueado
  até existir uma autoridade de jogo confiável e testada.
