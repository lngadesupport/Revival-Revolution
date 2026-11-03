# Integração Online do launcher

## Primeira entrega

A navegação ONLINE abre uma central WPF para o matchmaking 1v1. Ela usa a API
Ranked existente:

- POST /v1/register para criar a identidade na primeira conexão;
- GET /v1/me para restaurar a conta;
- POST /v1/queue/join para entrar na fila sa-east;
- GET /v1/match e GET /v1/queue para acompanhar a atribuição;
- POST /v1/queue/leave para cancelar.

O token bearer é armazenado no Gerenciador de Credenciais do Windows, associado
ao host da API. O endereço da API fica em %LOCALAPPDATA%/RevivalRevolution.
HTTP é aceito somente para localhost durante desenvolvimento; servidores
remotos precisam de HTTPS.

## Limites explícitos desta etapa

O servidor atual é uma API de desenvolvimento e escuta em loopback por padrão.
Ele ainda não tem aceitação de partida, ticket P2P ou validação autoritativa de
resultado. A interface mostra a partida atribuída, mas não inicia a conexão nem
abre a luta automaticamente. Isso será ligado após o endpoint de ticket e o
protocolo de autorização da bridge serem implementados e testados em duas
máquinas.

## Segunda entrega — modalidades de matchmaking

A central ONLINE apresenta duas modalidades:

- **Aleatório:** mantém o fluxo de fila regional `/v1/queue/join`, com consulta periódica de `/v1/match` e `/v1/queue`.
- **Por código:** o anfitrião chama `POST /v1/rooms/create`, compartilha o código de seis caracteres e acompanha `GET /v1/rooms/current`. O convidado informa o código em `POST /v1/rooms/join`. O código expira em 15 minutos, não pode ser reutilizado e a sala pode ser cancelada pelo anfitrião.

O backend impede que o mesmo jogador permaneça na fila aleatória e crie/entre em sala privada simultaneamente. As salas e atribuições são persistidas no SQLite.

A atribuição de partida não equivale à conexão de jogo: emissão/validação de ticket, integração do ticket com a bridge e teste P2P entre duas máquinas continuam pendentes. O launcher não declara a luta iniciada.
