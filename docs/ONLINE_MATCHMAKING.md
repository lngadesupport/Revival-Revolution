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
