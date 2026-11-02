# Inspeção do jogo em execução

O projeto já possui um comando de inspeção somente leitura no bridge de
multiplayer. Ele não depende de uma cópia estática válida de
`Assembly-CSharp.dll`: o bridge procura a assembly `Assembly-CSharp` que já
está carregada no processo Unity.

## Como executar

1. Inicie o jogo pelo fluxo normal do launcher Revival Revolution.
2. Aguarde o bridge de multiplayer ser anexado. A pasta do bridge deve conter
   `netplay-pid.txt` e `game-response.json`.
3. Abra um terminal na pasta que contém `bridge_command.py`.
4. Execute:

```powershell
python bridge_command.py inspect
```

O comando escreve uma solicitação local em `game-command.json`, aguarda a
resposta do bridge e imprime o JSON recebido.

## O que a inspeção retorna

O método `MetalNetplayController.Inspect` consulta por reflexão:

- modo atual do `matchManager`;
- telas ativas derivadas de `MetalFury.UI.BaseUI`;
- se o objeto de luta atual está usando rollback;
- frame atual, quando existe uma partida em andamento.

O comando não altera o estado da partida, não modifica DLLs e não abre menu.
É uma leitura de diagnóstico. Execute no menu principal e, se necessário,
repita em outras telas para descobrir quais tipos de UI ficam ativos.

## Limitações

- O bridge precisa estar carregado e o jogo precisa continuar aberto.
- A inspeção lista nomes das telas ativas, não os controles filhos nem todos
  os campos internos dessas telas.
- O resultado depende dos tipos que a build instalada do jogo expõe em runtime.
- Se a resposta vier como `error`, preserve `netplay-error.txt` e
  `game-response.json` para análise.

## Por que isso importa

A inspeção em runtime pode revelar os nomes concretos das telas e o estado
interno da UI sem exigir que o projeto tenha uma cópia local de
`Assembly-CSharp.dll`. Esses nomes servirão para decidir se o Community Panel
ou outro ponto de extensão existente pode receber os recursos sociais. Não
vamos criar clãs nem substituir telas do jogo sem evidência técnica.
