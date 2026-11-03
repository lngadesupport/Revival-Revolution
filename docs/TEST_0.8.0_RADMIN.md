# Build de teste 0.8.0 — Radmin VPN

Esta é uma build experimental do launcher para teste privado com dois ou mais PCs
na mesma rede Radmin VPN. Ela não é uma release estável.

## Versões

- Launcher: 0.8.0 (identificação do pacote de teste).
- API Ranked: 0.8.1 (o endpoint /health continuará exibindo essa versão).

## Preparação

1. O anfitrião mantém o servidor iniciado por `scripts/start-radmin-ranked-host.ps1`.
2. Todos os participantes entram na mesma rede Radmin VPN.
3. Cada participante extrai o ZIP do artefato em uma pasta separada.
4. Execute `RevivalRevolution.exe`.
5. Abra ONLINE e configure o endpoint como `http://IP-RADMIN-DO-ANFITRIAO:8765`.
6. Cada jogador cria sua própria conta e entra com ela.

## Teste recomendado

- Primeiro, validar cadastro e login em duas contas diferentes.
- Depois, testar criação/entrada em sala privada por código.
- Por último, testar a fila de matchmaking e confirmar se os dois clientes
  recebem a mesma atribuição de partida.

## Limitações

A janela ONLINE comunica-se com o backend, mas ainda não dispara automaticamente
o transporte P2P nem a bridge do jogo. A conexão TCP da API não comprova que o
tráfego UDP do jogo esteja funcional. Não usar esta build para partidas públicas
nem confiar em resultados enviados pelos clientes para atualizar ELO.

O servidor precisa permanecer aberto no PC anfitrião. Não abrir portas WAN no
roteador. Convide somente pessoas confiáveis para a rede privada Radmin.
