# Online Matchmaking — versão 0.8.1

## Interface atual

A navegação **ONLINE** abre uma janela WPF dedicada do launcher. Ela ainda não
é uma página embutida na janela principal nem uma interface dentro do jogo.

A janela oferece duas modalidades:

1. **Aleatório:** entra na fila regional `sa-east`, acompanha a posição e a
   atribuição, e permite cancelar a busca.
2. **Por código:** cria uma sala com código de seis caracteres, permite
   compartilhar o código, consultar a entrada do convidado e cancelar uma sala
   ainda aberta.

O token bearer fica no Gerenciador de Credenciais do Windows, associado ao
host da API. O endereço da API fica em
`%LOCALAPPDATA%/RevivalRevolution/ranked-endpoint.txt`. HTTP é permitido
somente para localhost em desenvolvimento; endpoints remotos exigem HTTPS.

## API usada

- `POST /v1/register` e `GET /v1/me` — identidade e restauração de sessão.
- `POST /v1/queue/join`, `GET /v1/queue`, `GET /v1/match`,
  `POST /v1/queue/leave` — matchmaking aleatório.
- `POST /v1/rooms/create`, `GET /v1/rooms/current`,
  `POST /v1/rooms/join`, `POST /v1/rooms/cancel` — salas privadas.

Os códigos expiram em 15 minutos, são de uso único e têm limite de 20
tentativas de entrada por minuto por conta. Fila aleatória e sala privada são
mutuamente exclusivas para cada jogador.

## Estado honesto da partida

A API atualmente **atribui** uma partida e entrega os dados do adversário. Isso
não significa que a conexão UDP foi estabelecida, que a bridge aceitou a
sessão, que a seleção sincronizou ou que a luta começou. A janela deixa essa
limitação explícita.

O fluxo multiplayer já existente inclui `peer_session.py`,
`autopunch_transport.py`, `selection_sync.py`, `live_fighter.py`,
`peer_native.py` e `MetalNetplayBridgeV8.dll`. A base 0.8.1 mantém esses
componentes e adiciona testes automatizados para o formato de connection code.
O antigo `fighter_input_test.py` não está versionado neste repositório; sua
ausência não deve impedir a importação do módulo principal, mas a função
diagnóstica correspondente não estará disponível.

## Próximas integrações obrigatórias

1. Implementar ciclo de vida da atribuição: aceite bilateral, timeout,
   cancelamento seguro e reconexão.
2. Emitir ticket efêmero, de uso único, vinculado a match, jogadores, slots,
   protocolo/build e expiração.
3. Adaptar `peer_session.py` para receber a autorização do ticket sem expor
   connection code reutilizável.
4. Definir o serviço de rendezvous/relay UDP. O Render hospeda a API HTTP; ele
   não substitui automaticamente um serviço UDP de transporte.
5. Conectar o launcher ao processo técnico enquanto `ufo.exe` já está aberto,
   acompanhar estados da bridge e só anunciar início quando a bridge confirmar.
6. Testar duas máquinas em redes distintas, incluindo timeout, desconexão,
   reentrada e encerramento.

Não aplicar rating competitivo até existir adjudicação confiável do resultado.
