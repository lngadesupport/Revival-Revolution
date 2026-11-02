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
- `GET /v1/me` — perfil autenticado.
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
- A integração com o launcher WPF ainda não foi feita.
- Deploy público exige TLS, firewall, backups, monitoramento e política de
  privacidade/retenção.
