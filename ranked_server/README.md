# Ranked API — base de desenvolvimento 0.8.1

A API fornece identidade persistente, perfil social, presença, leaderboard,
fila 1v1 e salas privadas por código. É Python padrão + SQLite; não requer
dependências externas.

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
- `GET /v1/match` — atribuição ativa do jogador.
- `POST /v1/rooms/create` — cria código privado de seis caracteres, com validade de 15 minutos.
- `POST /v1/rooms/join` — valida código, limita tentativas a 20 por minuto por conta e atribui a mesma partida aos dois jogadores.
- `GET /v1/rooms/current` — estado da sala do anfitrião.
- `POST /v1/rooms/cancel` — cancela sala ainda aguardando convidado.

As rotas autenticadas usam `Authorization: Bearer TOKEN`. O banco armazena
somente o SHA-256 do token. A credencial do jogador deve ficar em armazenamento
privado do cliente, nunca em logs ou arquivos públicos. A API social não é
apresentada como interface social do launcher; a integração visual pretendida
é dentro do jogo, ainda pendente de um ponto de extensão verificável.

## Limitações conhecidas da base 0.8.1

- O matchmaking aleatório e por código cria atribuições no banco; ainda não
  inicia uma sessão P2P.
- Não há aceite bilateral, ticket de transporte, lease de reconexão nem
  encerramento confiável da atribuição. Partidas permanecem em estado
  `assigned`, portanto o jogador não deve receber a promessa de que poderá
  jogar partidas consecutivas até o ciclo de partida ser concluído.
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
