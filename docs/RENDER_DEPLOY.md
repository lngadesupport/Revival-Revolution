# Publicar a API Ranked no Render

O repositório contém um Blueprint em `render.yaml` para criar o serviço web
`revival-revolution-ranked`.

## O que o Blueprint configura

- Runtime Python 3.12, sem dependências externas.
- Health check em `/health`.
- Bind em `0.0.0.0` usando a porta `PORT` fornecida pelo Render.
- SQLite em `/var/data/ranked.sqlite3`.
- Disco persistente de 1 GB montado em `/var/data`.

O serviço está configurado no plano Starter porque o banco SQLite precisa de
disco persistente. O plano gratuito não oferece esse disco para o serviço web;
usar armazenamento efêmero poderia apagar contas, salas e dados quando a
instância reiniciasse ou fosse substituída.

## Publicação

1. Entre no Render e conecte a conta GitHub que tem acesso a
   `lngadesupport/Revival-Revolution`.
2. Crie um **Blueprint** e selecione o repositório.
3. Selecione a branch `feature/ranked-match-backend-v2` enquanto a integração
   estiver em validação.
4. Revise o plano Starter e o disco persistente antes de confirmar a criação.
5. Aguarde o deploy e abra `https://SEU-SERVICO.onrender.com/health`.
6. Quando a resposta indicar `"ok": true`, copie a URL base HTTPS e informe-a
   no campo de endereço da API Ranked no launcher.

Não coloque tokens de jogadores em variáveis públicas nem em arquivos do
repositório. O token individual continua guardado no Gerenciador de Credenciais
do Windows.

## Antes de abrir ao público

O Blueprint é uma configuração inicial de hospedagem, não uma certificação de
produção. Antes de anunciar o servidor, configure backups do disco, monitore
erros e uso, estabeleça política de privacidade e implemente proteção contra
abuso/rate limiting. A integração de ticket P2P com a bridge ainda precisa ser
concluída; publicar a API não inicia partidas de jogo por si só.
