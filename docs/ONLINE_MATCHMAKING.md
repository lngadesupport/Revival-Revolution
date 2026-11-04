# Matchmaking e partidas online pela Radmin VPN

## Regra de rede

O matchmaking funciona **exclusivamente pela Radmin VPN**. Os dois jogadores
precisam estar conectados à mesma rede Radmin, usando o mesmo servidor Ranked
hospedado no IPv4 Radmin do anfitrião. A fila não reúne usuários de redes
Radmin diferentes e não usa Render, STUN, AutoPunch, relay externo ou fallback
pela internet.

O endereço da API fica em
`%LOCALAPPDATA%/RevivalRevolution/ranked-endpoint.txt`. HTTP remoto só é
aceito para IPv4 Radmin VPN 26.x.x.x. Consulte
[RADMIN_VPN_SETUP.md](RADMIN_VPN_SETUP.md) para configurar o servidor local.

## Modos disponíveis

1. **Partida aleatória:** entra na fila regional `sa-east`, mostra a posição,
   consulta a fila a cada dois segundos e permite cancelar. Ao encontrar outro
   jogador elegível, o servidor cria uma partida e uma sala Radmin P2P
   vinculada aos dois jogadores. O launcher confirma automaticamente a
   atribuição nos dois clientes.
2. **Por código:** cria uma sala com código de seis caracteres, permite
   compartilhar o código, consultar a entrada do convidado e cancelar uma
   sala ainda aberta.

A fila usa a mesma região para todos os jogadores desta implantação. O
pareamento considera rating e tempo de espera; a faixa de rating aumenta
gradualmente enquanto os jogadores aguardam.

## API usada

- `POST /v1/register` e `GET /v1/me` — identidade e restauração de sessão.
- `POST /v1/queue/join`, `GET /v1/queue`, `GET /v1/match`,
  `POST /v1/queue/leave` — matchmaking aleatório.
- `POST /v1/match/accept`, `POST /v1/match/decline` e
  `POST /v1/match/finish` — aceite bilateral, recusa e confirmação
  bilateral de encerramento. A atribuição expira em 90 segundos; depois de
  ambos aceitarem, a partida fica pronta por até duas horas.
- `POST /v1/rooms/create`, `GET /v1/rooms/current`,
  `POST /v1/rooms/join`, `POST /v1/rooms/cancel` — salas por código.
- `POST /v1/rooms/start` e `POST /v1/rooms/connection-code` — inicialização
  da sessão e publicação do código MR1 do anfitrião.

O token bearer fica no Gerenciador de Credenciais do Windows, associado ao
host da API.

## Fluxo após o pareamento

1. O servidor seleciona dois jogadores na fila e cria uma sala privada
   vinculada à partida. O primeiro slot é o anfitrião; o segundo é convidado.
2. Os dois launchers aceitam automaticamente a atribuição. A partida só fica
   pronta quando ambos confirmam.
3. Quando os dois aceitam, o launcher do anfitrião inicia automaticamente o
   jogo e o peer UDP diretamente no IPv4 Radmin local, sem STUN ou relay.
4. O anfitrião publica o código MR1; o convidado o recebe pela API Radmin e
   conecta ao anfitrião na porta UDP 8766.
5. Ambos observam o handshake autenticado e a prontidão da bridge antes de
   entrarem na tela VS.

O launcher restaura a busca se for fechado enquanto o jogador ainda estiver
na fila. Ao sair do launcher durante a busca, ele tenta remover a conta da
fila. A recusa de uma partida libera os dois jogadores.

## Limites conhecidos

A API atribui partidas, controla o aceite e troca o código de conexão, mas
não autentica resultado competitivo nem altera rating. O encerramento ainda
depende de confirmação dos clientes. Não aplicar rating competitivo até
existir adjudicação confiável do resultado.

A integração de matchmaking cria salas marcadas como aleatórias e reutiliza
o fluxo de sala Radmin e a inicialização peer já usados pela partida por
código. O host do jogo é escolhido entre os dois jogadores pareados; ele
seleciona seu próprio adaptador Radmin local, sem depender de ser a máquina
que hospeda a API. Isso não substitui o
teste real com dois PCs conectados à mesma rede Radmin. Ainda precisam ser
validados timeout, desconexão, reentrada, firewall UDP 8766, compatibilidade
da bridge e a luta completa com rollback.
