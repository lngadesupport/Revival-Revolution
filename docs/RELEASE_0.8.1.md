# Revival Revolution 0.8.1 — estabilização

## Incluído nesta linha

- Versão do assembly e do launcher definida como 0.8.1.
- Central ONLINE com duas opções: matchmaking aleatório e matchmaking por
  código.
- Backend com UUID, token bearer, perfil, amigos, presença, leaderboard,
  fila regional e salas privadas.
- Códigos privados de seis caracteres, expiração de 15 minutos, uso único,
  cancelamento pelo anfitrião e limite de 20 tentativas/minuto/conta.
- Token do launcher armazenado no Windows Credential Manager.
- Blueprint Render para API HTTP/HTTPS com SQLite em disco persistente.
- Diagnóstico multiplayer corrigido para identificar
  `MetalNetplayBridgeV8.dll`, `MetalGekko.dll` e os scripts de sessão.
- Importação do `peer_session.py` não falha somente porque o módulo
  diagnóstico legado `fighter_input_test.py` não está presente.
- CI compila os módulos Python de multiplayer e testa os códigos MR1/MR2.
- Atualizador compara hashes individuais e só substitui arquivos do launcher
  ausentes/divergentes; arquivos do jogo continuam protegidos.

## Não incluído / ainda bloqueado

- Deploy real da API na conta Render.
- Ticket de sessão Ranked, reconexão e integração do ciclo de aceite/encerramento no cliente. O backend agora persiste aceite bilateral, recusa, expiração e confirmação bilateral de encerramento; os novos testes ainda precisam passar no CI.
- Integração do matchmaking com o transporte P2P e com a bridge.
- Relay UDP próprio; o Render Web Service é para HTTP/HTTPS e não substitui
  um endpoint público UDP.
- Validação de sessão e teste entre duas máquinas em redes distintas.
- Resultado autoritativo, anti-cheat e atualização segura de ELO.
- Integração de perfil/amigos/leaderboard dentro do jogo.
- Reparação de arquivos proprietários do jogo: continua bloqueada até que
  existam hashes confiáveis dos pacotes por canal independente.

## Ajustes de preparação para Render

- O Blueprint continua configurado para uma única instância web com disco persistente SQLite.
- Foi adicionado `scripts/check_render_api.py` para validar a URL HTTPS atribuída pelo Render, o payload de saúde e a versão 0.8.1 sem criar contas ou alterar dados.
- Corrigida a exclusividade de partidas: jogadores com partida `ready` também ficam impedidos de criar ou entrar em salas privadas.
- Foram adicionados testes HTTP de integração para saúde, cadastro, autenticação e acesso sem token.

## Critérios mínimos antes da tag estável v0.8.1

- [ ] CI Python atualizado: testes adicionais cobrem exclusividade entre partidas prontas e salas privadas, health check HTTP, cadastro/autenticação e rejeição de chamadas sem token. A execução desta revisão ainda precisa ficar verde.
- [x] Build Windows x64, verificação do artefato publicado e smoke tests do reparo seletivo aprovados no CI.
- [ ] Teste do launcher em Windows limpo.
- [ ] Teste de runtime Python e atualização com SHA-256, staging e rollback.
- [ ] Diagnóstico validado contra a instalação real do usuário.
- [ ] Sessão P2P validada em dois PCs e redes distintas.
- [ ] API implantada e monitorada; persistência e backup restauráveis.
- [ ] Ticket de transporte e reconexão definidos e integrados.
- [ ] Testes adicionais do ciclo de vida e da API HTTP aprovados no CI; cliente Online integrado às rotas de aceite/recusa/encerramento.
- [ ] Nenhuma promoção para `main` ou publicação de tag sem aprovação explícita.

## Branch e segurança

O trabalho ocorre em `release/0.8.1-stabilization`. A `main` e a
`release/0.7.2-stabilization` permanecem intocadas. A tag `v0.8.1` só deve
ser criada depois que o commit estiver integrado à `main` e todos os gates
acima aplicáveis à release tiverem sido aprovados.
