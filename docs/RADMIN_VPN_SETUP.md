# Ranked local pela Radmin VPN (Windows)

Esta configuração substitui o endpoint hospedado por uma API executada no PC anfitrião. Ela não usa FadeHost, Render, STUN ou relay externo para a API. Todos os jogadores precisam estar conectados à mesma rede Radmin VPN.

## 1. Preparar a rede

1. Instale o Radmin VPN em cada computador.
2. O anfitrião cria uma rede privada e compartilha nome/senha apenas com os jogadores convidados.
3. Todos entram na mesma rede e confirmam que os computadores aparecem como conectados.
4. Anote o IPv4 Radmin do anfitrião (normalmente começa com 26.).

Não compartilhe as credenciais da rede com desconhecidos: qualquer membro da rede privada poderá tentar alcançar os serviços nela publicados.

## 2. Inicialização automática do servidor

A distribuição atual integra o servidor Ranked ao launcher. Na primeira execução no PC anfitrião:

1. Ligue o Radmin VPN e entre na rede privada.
2. Abra o Revival Revolution Launcher.
3. O launcher identifica automaticamente o IP Radmin configurado, instala o runtime portátil necessário se ainda não existir, solicita autorização administrativa uma única vez para as regras restritas do Firewall do Windows e registra uma tarefa oculta no Agendador de Tarefas.
4. O agente automático inicia a API em segundo plano, mantém o banco em `%LOCALAPPDATA%\\RevivalRevolution\\RankedServer\\ranked.sqlite3` e reinicia o processo se ele falhar.

Depois dessa preparação inicial, não é necessário abrir PowerShell, executar scripts ou manter uma janela de terminal aberta. A tarefa inicia no logon do Windows e aguarda a interface Radmin ficar disponível; quando o Radmin conecta, a API sobe automaticamente. O launcher também verifica a saúde da API enquanto está aberto.

No primeiro uso de PRIVATE MATCH em cada computador, o launcher configura a regra UDP de entrada para o runtime Python, limitada à rede Radmin. O Windows pode solicitar autorização administrativa uma única vez. A regra TCP da API no anfitrião fica limitada ao IP local Radmin, porta 8765 e origem 26.0.0.0/8. Nenhuma porta é aberta no roteador.

## 3. Conectar os launchers dos jogadores

Em cada launcher, na janela ONLINE, informe:

    http://IP-RADMIN-DO-ANFITRIAO:8765

Exemplo ilustrativo: http://26.XX.XX.XX:8765. O launcher aceita HTTP remoto apenas quando o host é um IPv4 26.x.x.x; HTTP para IP público ou outras redes continua bloqueado. O tráfego passa pelo túnel criptografado da Radmin VPN.

O primeiro jogador cria sua conta no servidor. O banco fica no PC anfitrião; faça backup periódico do arquivo ranked.sqlite3 com a API parada.

## 4. Verificar conectividade

Em cada PC cliente, abra no navegador:

    http://IP-RADMIN-DO-ANFITRIAO:8765/health

A resposta esperada é JSON com "ok": true e "version": "0.8.1". Depois conecte pela janela ONLINE e teste cadastro, fila e salas privadas com duas contas distintas.

Se não responder:
- confirme que ambos estão conectados à mesma rede Radmin;
- confira se o IP informado é o do anfitrião;
- confira se a janela do servidor continua aberta;
- confirme a regra de firewall "Revival Ranked - Radmin VPN only";
- teste Test-NetConnection IP-RADMIN-DO-ANFITRIAO -Port 8765 no cliente.

## 5. Transporte do jogo pela Radmin

A API Ranked e o transporte UDP do jogo são componentes diferentes. Para o transporte, use o modo direto existente em peer_session.py, sem --autopunch:

PC anfitrião (com o jogo aberto e bridge anexada):

    python peer_session.py --mode host --bind IP-RADMIN-LOCAL --advertise IP-RADMIN-LOCAL

O código de conexão será gravado em peer-code.txt na pasta raiz do projeto. Envie-o ao segundo jogador por um canal privado.

PC convidado:

    python peer_session.py --mode join --code CODIGO_RECEBIDO

Os dois PCs precisam usar o mesmo build/protocolo e ter o jogo aberto com a bridge compatível. O firewall do Windows também precisa permitir o tráfego UDP de entrada/saída do processo Python na interface Radmin; a porta UDP escolhida pelo processo é dinâmica (porta local 0), portanto o Windows pode solicitar autorização. Não abra portas no roteador.

O fluxo atual de peer_session coordena a preparação, sincronização e bridge experimental; a integração automática com a janela ONLINE/ticket Ranked ainda não existe. A fila não inicia o jogo sozinha. Não use --autopunch: esse modo depende de STUN e relay externo, contrariando o objetivo de operar somente pela Radmin.

## Limitações e segurança

- O PC anfitrião precisa ficar ligado, conectado à internet e à rede Radmin.
- Se o anfitrião desligar, API e matchmaking ficam indisponíveis. O banco permanece no disco local.
- A API atual usa HTTP sem TLS próprio. O uso remoto é restrito ao IP virtual Radmin e ao túnel privado; convide somente pessoas confiáveis.
- O servidor é uma única instância SQLite; não suporta alta disponibilidade nem múltiplos hosts.
- Isso permite teste privado controlado, não equivale a serviço público competitivo.
- A confirmação de fim de partida ainda não é prova autoritativa do resultado; não aplicar ELO automaticamente.
- Nenhum teste real entre dois computadores/jogo foi executado por esta alteração.
