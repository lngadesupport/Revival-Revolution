# Publicar a API Ranked no Render — base 0.8.1

O repositório contém um Blueprint em `render.yaml` para criar o serviço web
`revival-revolution-ranked`.

## O que o Blueprint configura

- Runtime Python 3.12, sem dependências externas.
- Região Virginia como ponto inicial para reduzir a distância aos testadores no Brasil; pode ser alterada se o grupo de teste estiver em outra região.
- Health check em `/health`.
- Bind em `0.0.0.0` usando a porta `PORT` fornecida pelo Render.
- SQLite em `/var/data/ranked.sqlite3`.
- Disco persistente de 1 GB.
- Plano `0.5c-512mb` (equivalente ao antigo Starter).
- Deploy automático somente depois de os checks de CI passarem (`checksPass`).
- O comando de build compila o backend/scripts e executa toda a suíte `unittest`; uma falha bloqueia o deploy.

O serviço SQLite com disco persistente deve operar como instância única.
Não escale horizontalmente esse serviço nem execute múltiplos processos
concorrentes usando o mesmo arquivo SQLite. O disco também causa uma pequena
indisponibilidade durante redeploys, pois o Render para a instância antiga
antes de iniciar a nova; isso é uma limitação documentada de persistent disks.

## Publicação controlada

1. Conecte no Render a conta GitHub com acesso a
   `lngadesupport/Revival-Revolution`.
2. Crie um Blueprint a partir do repositório.
3. Selecione a branch `release/0.8.1-stabilization` durante a validação.
4. Revise plano, disco persistente, região e custo antes de criar o serviço.
5. Aguarde o deploy e verifique
   `https://SEU-SERVICO.onrender.com/health`.
6. Confirme `"ok": true` e versão `"0.8.1"` antes de configurar o launcher.
7. Execute o verificador automatizado:

   ```bash
   python scripts/check_render_api.py https://SEU-SERVICO.onrender.com
   ```

   O script exige HTTPS e valida serviço, estado e versão; ele não cria contas
   nem altera dados do banco.

Não coloque tokens de jogadores em variáveis públicas ou arquivos do
repositório. O token individual continua no Gerenciador de Credenciais do
Windows.

## Limite de função do Render

O Render Web Service deste Blueprint recebe tráfego público pela porta HTTP
configurada em `PORT`, com TLS terminado na borda. Um Private Service pode
usar outros protocolos, mas só recebe conexões pela rede privada Render; não
serve como relay UDP público para os jogadores. Consulte a documentação oficial:
[Web Services](https://render.com/docs/web-services) e
[Private Services](https://render.com/docs/private-services).

Portanto, o Blueprint hospeda a API HTTP/HTTPS e o SQLite, mas não cria um
relay UDP público nem garante conectividade P2P. O código AutoPunch atual
consulta STUN e usa por padrão um relay externo configurável. Para controlar
a infraestrutura multiplayer, precisaremos hospedar/operar um serviço UDP
compatível em separado (por exemplo, numa VPS), ou escolher outra arquitetura
de transporte e testá-la.

## Antes de abrir ao público

O Blueprint é configuração inicial, não certificação de produção. Antes de
anunciar o servidor, configurar backups e monitoramento, revisar rate limits
globais, proteção de cadastro, política de privacidade/retenção e resposta a
incidentes. A integração de ticket P2P com a bridge e o teste entre duas
máquinas continuam obrigatórios.
