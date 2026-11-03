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
- `POST /v1/queue/join` — JSON `{"region":"sa-east"}`.
- `GET /v1/queue` — posição/estado da fila.
- `POST /v1/queue/leave` — sai da fila.
- `GET /v1/match` — partida atualmente atribuída.
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
- O serviço não recebe vencedor e não altera ELO: checksum reportado pelos dois
  clientes detecta divergência, mas não impede cliente modificado.
- A interface social dentro do jogo ainda não foi integrada.
- Clãs não foram implementados: ainda não foi encontrado suporte verificável
  no jogo nem um ponto de extensão seguro.
- Deploy público exige TLS, firewall, backups, monitoramento e política de
  privacidade/retenção.
