# Online Matchmaking — versão 0.8.1

## Interface atual

A navegação **ONLINE** abre uma janela WPF dedicada do launcher. Ela ainda não
é uma página embutida na janela principal nem uma interface dentro do jogo.

A janela oferece duas modalidades:

1. **Aleatório:** entra na fila regional, acompanha a posição e a atribuição,
   e permite cancelar a busca.
2. **Por código:** cria uma sala com código de seis caracteres, permite
   compartilhar o código, consultar a entrada do convidado e cancelar uma sala
   ainda aberta.

O token bearer fica no Gerenciador de Credenciais do Windows, associado ao
host da API. O endereço da API fica em
`%LOCALAPPDATA%/RevivalRevolution/ranked-endpoint.txt`. HTTPS é aceito; HTTP
remoto só é aceito para IPv4 Radmin VPN 26.x.x.x. HTTP localhost continua
permitido para desenvolvimento. Consulte [RADMIN_VPN_SETUP.md](RADMIN_VPN_SETUP.md)
para iniciar a API local no PC anfitrião.

## API usada

- `POST /v1/register` e `GET /v1/me` — identidade e restauração de sessão.
- `POST /v1/queue/join`, `GET /v1/queue`, `GET /v1/match`,
  `POST /v1/queue/leave` — matchmaking aleatório.
- `POST /v1/match/accept`, `POST /v1/match/decline` e
  `POST /v1/match/finish` — ciclo de aceite bilateral, recusa e confirmação
  bilateral de encerramento. O aceite expira em 90 segundos; após ambos
  aceitarem, a partida fica pronta por até duas horas. O encerramento não
  altera rating e não autentica o resultado.
- `POST /v1/rooms/create`, `GET /v1/rooms/current`,
  `POST /v1/rooms/join`, `POST /v1/rooms/cancel` — salas privadas.

Os códigos expiram em 15 minutos, são de uso único e têm limite de 20
tentativas de entrada por minuto por conta. Fila aleatória e sala privada são
mutuamente exclusivas para cada jogador.

## Estado honesto da partida

A API **atribui** uma partida e entrega os dados do adversário. A janela WPF
integra aceite e recusa: cada jogador confirma individualmente, acompanha
o aceite do adversário, pode cancelar a atribuição pronta e recupera uma
partida ativa ao restaurar a conta.

Isso ainda não significa que a conexão UDP foi estabelecida, que a bridge
aceitou a sessão, que a seleção sincronizou ou que a luta começou. A fila não
inicia a luta automaticamente e ainda não aplica resultado/rating.

## Transporte direto pela Radmin VPN

O transporte experimental existente inclui `peer_session.py`,
`autopunch_transport.py`, `selection_sync.py`, `live_fighter.py`,
`peer_native.py` e `MetalNetplayBridgeV8.dll`. Para operar somente pela
Radmin, use `peer_session.py` em modo direto com o IPv4 Radmin local no
parâmetro `--bind` e `--advertise`, e não use `--autopunch`. O modo
AutoPunch usa STUN e relay externo e não faz parte desta configuração.

A criação do connection code e a entrada do convidado ainda são etapas
manuais; a janela ONLINE não inicia o processo peer nem emite ticket de
sessão. A bridge precisa estar disponível e o jogo aberto nos dois PCs.

## Próximas integrações obrigatórias

1. Integrar a janela ONLINE ao processo técnico de peer session e à bridge,
   usando os IPs Radmin recebidos e sem depender de STUN/relay.
2. Emitir autorização/ticket efêmero de uso único, vinculado a match,
   jogadores, slots, protocolo/build e expiração.
3. Integrar confirmação de encerramento após a bridge sinalizar fim real da
   luta; implementar lease de sessão e reconexão segura.
4. Testar duas máquinas conectadas pela mesma rede Radmin, incluindo timeout,
   desconexão, reentrada e encerramento.

Não aplicar rating competitivo até existir adjudicação confiável do resultado.
