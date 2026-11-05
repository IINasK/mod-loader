# FPV: perfil salvo e ensaio de 23:28 — 01/11/2026

Douglas confirmou “salvei (override) e gravei novamente”. Série 20261101-232828-Serie-Cyberpunk-a6213f8d/02-Cyberpunk, ID4, concluída às23:30:22(-03). São13clipes,1593quadros,0duplicados e0erros registrados. Não confundir integridade de captura com ausência de colisão ou precisão da medição.

## Configuração conferida

NASK.json foi gravado pelo usuário às23:27:43, antes da série. session.json registra os mesmos rates salvos. A leitura posterior confirmou:

| Eixo | Center | Expo | Max |
| --- | ---: | ---: | ---: |
| Roll | 200 | 0,50 | 660 |
| Pitch | 400 | 0,00 | 660 |
| Yaw | 400 | 0,00 | 660 |

Feedforward100, TuneTightness100. Gravidade11,3400001526; massa400g; motor/referenceThrustToWeightRatio8,407243. Câmera salva20graus/FOV125. Não houve escrita no jogo pelo agente. O programa registra SavedOnly: estas informações não são telemetria contínua da memória do jogo.

O ensaio anterior23:21(f834797f/ID3) foi confirmado pela foto codex-clipboard-1c9b2672-2484-485e-983c-a2320936f03e.png: Roll200/0,50/660, Pitch/Yaw400/0/660; Feedforward0 confirmado pelo usuário. NÃO rotular como Roll0,55 nem como três eixos200. A diferença declarada entre estes dois ensaios é Feedforward0→100; cenários/poses também mudaram, impedindo isolar quantitativamente seu efeito.

## Comparação e limites

Foram inspecionadas sequências visuais dos seis clipes25/50%(roll,pitch,yaw), com tempos0;1,4;1,85;1,95…3,65s. Roll25/50 foi confrontado com a referência Uncrashed20261031-040122/d9182a41/01-Uncrashed/ID1 e com23:21. O deslocamento visual acumulado de Roll continua menor que na referência. Não se estabeleceu melhora clara atribuível apenas ao Feedforward100. Não produzir graus/segundo ou ganho percentual medidos a partir destas folhas: perspectiva, translação, câmera inclinada e ausência de telemetria tornam esse ajuste fino incerto.

Pitch/Yaw usam uma curva diferente da do Roll e não são controles para concluir efeito de Expo ou Center do Roll. Não foram classificados como calibrados nem receberam ajuste nesta análise. Metadados de todos13clipes conferidos; inspeção visual focada em seis25/50%, sem afirmar análise quadro a quadro dos demais.

## Próximo ensaio proposto, não aplicado

Alterar SOMENTE Roll Center200→400, mantendo Roll Expo0,50/Max660, Feedforward100 e TuneTightness100. Manter Pitch/Yaw e física. É um teste de compensação da resposta observada, não conversão matemática exata da curva KISS. O aumento de Center reforça baixo/médio stick e conserva o endpoint660 enquanto Center≤Max. Salvar por Overwrite antes de gravar. Focar avaliação em Roll25/50; não exigir nova referência Uncrashed nem nova bateria vertical.

O valor400 é experimental; não afirmar calibração concluída. Para separar atraso de controle, entrada recebida e resposta física com precisão, será necessária telemetria de stick/setpoint/bodyRates. Nenhum logger novo foi implementado.

## Persistência e evidências

O mod só grava o perfil explicitamente via FPV Profiles & Setup → Overwrite Profile (botão Overwrite). settings.lua/Save sem allowOverwrite=true é no-op deliberado. CET Bindings/Save é outro sistema. O salvamento atual funcionou; não era falha de permissão nem prova de reinstalação.

Auditoria/Comparacao-FPV-20261101/23-28-Overwrite contém snapshot privado NASK, metadados e comandos, sem duplicar vídeos. Folhas locais em C:/Users/Douglas/Desktop/Mods/Analise-Cyberpunk-20261101-232828. Helper reutiliza FpvVideoSheets existente; tentativa no PowerShell7 falhou na referência System.Drawing/List, repetição explícita WindowsPowerShell5.1 concluiu. Nenhum mod, save, executável ou RED4ext foi alterado. Não enviar NASK pessoal/auditoria aoGitHub.