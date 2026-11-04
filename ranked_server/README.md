# Ranked API — desenvolvimento

Este pacote implementa identidade persistente, perfil, amigos, presença,
leaderboard e fila 1v1 usando a biblioteca padrão do Python e SQLite.

## Executar localmente

Na raiz do repositório:

```bash
python -m ranked_server --host 127.0.0.1 --port 8765 --database ranked.sqlite3
```

O serviço escuta em loopback por padrão. Em uma VPS, coloque-o atrás de um
proxy reverso com TLS e mantenha o processo Python em loopback. Não exponha
este servidor HTTP de desenvolvimento diretamente à internet.

## Host privado pela Radmin VPN (Windows)

Para hospedar a API em uma rede privada Radmin, execute na raiz do repositório:

```powershell
.\scripts\start-radmin-ranked-host.ps1
```

O script detecta o IP virtual local 26.x.x.x, vincula a API a esse endereço,
abre TCP 8765 e UDP 8766 somente para a faixa Radmin 26.0.0.0/8 e guarda o
SQLite em `%LOCALAPPDATA%\RevivalRevolution\RankedServer`. Todos os jogadores
devem entrar na mesma rede Radmin e configurar no launcher
`http://IP-RADMIN-DO-HOST:8765`. HTTP só é permitido no launcher para
localhost ou IPv4 Radmin; HTTP público continua bloqueado.

O transporte peer UDP é direto pela Radmin, usa MR1 e UDP 8766. STUN, AutoPunch,
MR2, relay de terceiros e fallback público estão desativados. O script não
configura `RANKED_RESULT_SECRET`; a adjudicação competitiva permanece bloqueada.

## Endpoints

- `GET /health` — verificação pública de saúde.
- `POST /v1/register` — JSON `{"display_name":"NICKNAME"}`; devolve UUID,
  rating inicial e token bearer mostrado uma única vez.
- `GET /v1/me` — próprio perfil (UUID, nickname, rating, avatar e estado online).
- `POST /v1/account/rotate-token` — gera uma nova credencial e invalida imediatamente a anterior; guarde a resposta em armazenamento privado.
- `GET /v1/players/{player_id}` — perfil público de outro jogador por UUID, incluindo estado online e indicador de foto.\n- `GET /v1/players/{player_id}/avatar` — recupera a foto sob demanda; listas não carregam imagens base64 em massa.
- `POST /v1/profile` — atualiza `display_name` e/ou `avatar_data`.
  Avatar aceita data URL base64 PNG/JPEG/WebP de até 256 KiB; a assinatura
  binária é validada e enviar null limpa a foto.
- `GET /v1/players/search?q=...` — busca jogadores por nickname (mínimo 2 caracteres), com indicador de foto e estado online.
- `GET /v1/friends` — amigos aceitos, solicitações recebidas e enviadas, com indicador de foto e estado online.
- `POST /v1/friends/request` — JSON `{"player_id":"UUID"}`; cria solicitação
  ou aceita automaticamente uma solicitação recíproca.
- `POST /v1/friends/accept` — aceita solicitação recebida.
- `POST /v1/friends/decline` — recusa solicitação recebida.
- `POST /v1/friends/remove` — remove amizade ou solicitação.
- `GET /v1/blocks` — lista jogadores bloqueados.
- `POST /v1/players/block` e `POST /v1/players/unblock` — bloqueia/desbloqueia;
  bloquear remove a amizade e impede novas solicitações entre as contas.
- `POST /v1/presence/heartbeat` — confirma atividade autenticada.
- `GET /v1/leaderboard?limit=100` — classificação por rating atual, com indicador de foto e estado online.
- `POST /v1/queue/join` — entra na fila casual; JSON `{"region":"sa-east"}`.
- `GET /v1/queue` — posição/estado da fila casual ou ranqueada atual.
- `POST /v1/queue/leave` — sai da fila ativa.
- `GET /v1/match` — partida casual atualmente atribuída.
- `POST /v1/ranked/queue/join` — entra na fila Ranked separada, pareada por rating.
- `GET /v1/ranked/queue` e `GET /v1/ranked/match` — estado da fila e partida Ranked.
- `POST /v1/rooms/create` — cria uma sala privada com código aleatório de 6 caracteres; expira em 15 minutos.
- `POST /v1/rooms/join` — JSON `{"code":"ABC234"}`; valida o código, limita tentativas a 20 por minuto por conta e atribui a mesma partida aos dois jogadores.
- `GET /v1/rooms/current` — estado da sala criada pelo jogador anfitrião e partida quando o convidado entra.
- `POST /v1/rooms/cancel` — cancela uma sala ainda aguardando convidado.

As respostas sociais mostram presença online/offline aproximada, não o horário exato da última atividade.\n\nUse `Authorization: Bearer TOKEN`. O banco guarda somente o SHA-256 do token.
O cliente integrado ao jogo deverá guardar o token em armazenamento privado
do usuário, nunca em arquivos públicos ou logs. O launcher não apresenta telas
sociais.

## Limitações — não pronto para produção

- Cadastro aberto; ainda não há recuperação de conta nem proteção contra
  criação abusiva de identidades.
- Salas por código e fila aleatória atribuem partidas, mas ainda não há aceitação de partida, lease de reconexão, rate limit ou administração.
- Não há ticket para autorizar a sessão UDP atual.
- A API não aceita resultados via token de jogador. O ledger só pode ser acionado
  pela autoridade interna; essa autoridade de jogo ainda não foi integrada. O
  checksum reportado pelos dois clientes detecta divergência, mas não impede
  cliente modificado.
- A interface social dentro do jogo ainda não foi integrada.
- Clãs não foram implementados: ainda não foi encontrado suporte verificável
  no jogo nem um ponto de extensão seguro.
- Deploy público exige TLS, firewall, backups, monitoramento e política de
  privacidade/retenção.


## Camada competitiva — primeira implementação

- `GET /v1/ranked/me` — rating, divisão, vitórias, derrotas, empates, partidas e histórico recente da conta autenticada.
- `GET /v1/ranked/leaderboard?limit=100` — classificação da temporada ativa.
- Salas privadas por código permanecem `private`, e a fila casual permanece `casual`; nenhuma das duas altera ELO.
- `POST /internal/v1/matches/{match_id}/result` — endpoint interno para autoridade confiável registrar vencedor ou empate. Exige o cabeçalho `X-Ranked-Authority` com o segredo configurado em `RANKED_RESULT_SECRET`. O endpoint retorna 503 quando o segredo não está configurado e nunca aceita o bearer token do jogador como autoridade.
- O ledger aplica ELO K=32, atualiza os dois ratings em uma transação SQLite, mantém deltas zero-sum, estatísticas, pico de rating e histórico.
- Reenvios do mesmo resultado são idempotentes: uma partida só altera rating uma vez. Resultados de partidas inexistentes, encerradas ou com vencedor fora do confronto são rejeitados.
- Divisões atuais: Bronze (abaixo de 1200), Prata (1200), Ouro (1400), Platina (1600), Diamante (1800) e Mestre (2000).

**Limite competitivo importante:** esta rodada cria o ledger e a fronteira de confiança da API, mas ainda não existe uma autoridade de jogo dedicada que comprove o vencedor da luta. Portanto, não configure nem distribua `RANKED_RESULT_SECRET` ao launcher ou aos jogadores. Sem integração com uma autoridade confiável da bridge/partida, os resultados não devem ser enviados e o modo não deve ser anunciado como Ranked competitivo ativo. O endpoint interno é infraestrutura para essa integração, não prova de que o jogo já valida resultados contra clientes modificados.

O servidor de desenvolvimento continua devendo ser executado atrás de TLS em produção. A migração das tabelas competitivas é aditiva e preserva contas e ratings existentes.
