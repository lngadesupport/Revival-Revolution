# Ranked API — desenvolvimento

Este pacote implementa o primeiro backend de identidade e fila 1v1, usando
somente a biblioteca padrão do Python e SQLite.

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
- `POST /v1/register` — JSON `{"display_name":"NOME-DO-PC"}`; devolve UUID,
  rating inicial e token bearer mostrado uma única vez.
- `GET /v1/me` — perfil autenticado (UUID, nickname, rating e avatar).
- `POST /v1/profile` — atualiza `display_name` e/ou `avatar_data`. Avatar aceita data URL base64 PNG/JPEG/WebP de até 256 KiB; enviar null limpa a foto.
- `GET /v1/players/search?q=...` — busca jogadores por nickname (mínimo 2 caracteres).
- `GET /v1/friends` — amigos aceitos, solicitações recebidas e enviadas.
- `POST /v1/friends/request` — JSON `{"player_id":"UUID"}`; cria solicitação ou aceita automaticamente uma solicitação recíproca.
- `POST /v1/friends/accept` — JSON `{"player_id":"UUID-DO-REMETENTE"}`.
- `POST /v1/friends/remove` — JSON `{"player_id":"UUID"}`.
- `GET /v1/leaderboard?limit=100` — classificação por rating.
- Presença é derivada de `last_seen` (online quando visto nos últimos 90 segundos).
- `POST /v1/queue/join` — JSON `{"region":"sa-east"}`.
- `GET /v1/queue` — posição/estado da fila.
- `POST /v1/queue/leave` — sai da fila.
- `GET /v1/match` — partida atualmente atribuída.

Use `Authorization: Bearer TOKEN`. O banco guarda somente o SHA-256 do token.
O launcher deverá guardar o token no perfil privado do usuário, fora da pasta
pública do jogo, e nunca incluí-lo em logs.

## Limitações — não pronto para produção

- Cadastro aberto; ainda não há recuperação de conta nem proteção contra
  criação abusiva de identidades.
- Não há aceitação, lease de reconexão, rate limit ou ferramentas de moderação.
- Não há ticket para autorizar a sessão UDP atual.
- O serviço não recebe vencedor e não altera ELO: checksum reportado pelos dois
  clientes detecta divergência, mas não impede cliente modificado.
- A interface social dentro do jogo ainda não foi integrada; o launcher não é\n  a interface de perfil/amigos/ranking.\n- Clãs não foram implementados: ainda não foi encontrado suporte verificável\n  no jogo nem um ponto de extensão seguro.
- Deploy público exige TLS, firewall, backups, monitoramento e política de
  privacidade/retenção.
