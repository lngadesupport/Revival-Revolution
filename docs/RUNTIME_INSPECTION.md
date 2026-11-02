# Inspeção do jogo em execução

O bridge de multiplayer já possui um comando de inspeção somente leitura. Ele
não depende de uma cópia estática válida de `Assembly-CSharp.dll`: o bridge
usa a assembly `Assembly-CSharp` que já está carregada no processo Unity.

## Método rápido (Windows)

1. Inicie o Revival Revolution pelo launcher.
2. Aguarde o jogo chegar ao menu principal e o bridge ser anexado.
3. Na pasta raiz do pacote, execute com duplo clique
   `INSPECIONAR-COMMUNITY-PANEL.bat`.
4. O relatório `CommunityPanel-Inspection-AAAAmmdd-HHMMSS.txt` será criado
   nessa mesma pasta. Envie o arquivo completo para análise.

O atalho usa o Python portátil do launcher, quando disponível. Ele não baixa
arquivos, não altera DLLs e não modifica configurações do jogo.

## Método manual

Na pasta que contém `bridge_command.py`, execute:

```powershell
python bridge_command.py inspect
```

O comando escreve uma solicitação local em `game-command.json`, aguarda a
resposta do bridge e imprime o JSON recebido.

## O que o comando realmente consulta

A implementação de `MetalNetplayController.Inspect` faz estas leituras por
reflexão:

- `matchManager.mode`: modo atual do jogo;
- todos os objetos ativos derivados de `MetalFury.UI.BaseUI`, retornando os
  nomes dos tipos encontrados;
- `fightingGame.useRollbackNetcode`: estado do rollback;
- `fightingGame.curFrame`: frame atual, quando existe uma luta.

Exemplo de formato de resposta (valores ilustrativos, não resultado de teste):

```json
{
  "state": "inspected",
  "message": "Mode=...; active UI=...; rollback=...",
  "frame": 0
}
```

## O que ainda não consulta

O comando não enumera os filhos de cada tela, textos, botões, eventos ou
hierarquia completa de GameObjects. Também não inspeciona diretamente os
controles do overlay nativo Dear ImGui. Portanto, o resultado ajuda a localizar
as telas Unity ativas, mas não prova sozinho que o Community Panel possa ser
estendido.

## Fatos confirmados pela inspeção estática do bridge

- `MetalNetplayBridge.Run` restaura o trecho de código do hook guardado em
  `hook.txt`, localiza a assembly já carregada `Assembly-CSharp`, obtém
  `MetalFury.Logic.GameApp.instance` e adiciona o componente
  `MetalNetplayController` ao GameObject do GameApp.
- `Initialize` guarda a referência da assembly e do GameApp, escreve
  `netplay-pid.txt` e publica o estado `bridge_ready`.
- O controlador lê `game-command.json` a cada 50 ms e aceita a ação
  `inspect` sem iniciar transição de menu.
- O overlay nativo é um componente separado. A análise de strings encontrou
  Dear ImGui 1.91.9b, DirectX 11, a indicação F1 para mostrar/ocultar o painel
  e o texto “Metal Revolution / Community panel”. Isso não demonstra que o
  binário aceite plugins ou novas abas.

## Se houver erro

Envie juntos, se existirem:

- `CommunityPanel-Inspection-*.txt`;
- `netplay-error.txt`;
- `game-response.json`;
- `netplay-status.txt`.

Não envie `hook.txt` nem arquivos que contenham códigos de conexão,
tokens ou credenciais. Se a resposta vier como `bridge_ready` em vez de
`inspected`, o comando não foi processado ou foi executado em outra pasta.

## Por que precisamos do resultado

Com os nomes concretos das telas em execução, poderemos decidir se há uma tela
Unity existente que pode hospedar perfil/amigos/ranking, ou se será necessário
criar uma UI separada dentro do jogo. Não vamos inventar clãs nem substituir
componentes do jogo sem evidência técnica.
