# FPV — primeiro teste isolado de aceleração0.1.5 — 02/11/2026

Douglas informou “gravado” após receberCalibrador0.1.5. Séries completas usadas:Uncrashed20261102-154950-Serie-Uncrashed-4153296b/01-Uncrashed/ID2, concluída15:50:14;Cyberpunk20261102-155319-Serie-Cyberpunk-1caca867/02-Cyberpunk/ID3, concluída15:53:39(-03). Cada série contém throttle025/050/075, pulso configurado1s, uma repetição. SérieUncrashed15:49:23/c11155a2 tem somente dois clipes semcomplete e não foi misturada com a série completa.

## Integridade e configuração

6clipes completos:588quadrosUncrashed+512Cyberpunk=1100;0duplicados/erros registrados. Os comandos ativos têm somente o nível esperado25/50/75% e eixosRoll/Pitch/Yaw0. Não houve impulso100% anterior aos pulsos. Isso valida o fluxo real usado pelo usuário; não comprova os eixos recebidos internamente pelo jogo nem mede altitude/empuxo.

Intervalos entre primeira amostra positiva e primeira amostra zero posterior noCSV:Uncrashed25=1,021444s,50=1,047728s,75=1,026257s;Cyberpunk25=1,026775s,50=1,024702s,75=1,025488s. São intervalos amostrados, não duração exata medida de cada atualizaçãoXInput. Não afirmar pulsos perfeitamente idênticos ou precisão de1ms. As diferenças de amostragem e agendamento limitam conclusões de poucos porcento.

Snapshot salvo doCyberpunk:ThrottleCurve0,Feedforward100,Tightness100;Roll/Pitch/Yaw500/0/660;gravidade11,3400001526,massa400g,motor/referênciaTWR8,407243,câmera0/FOV125. Não confundir os rates de giro com throttle. SavedOnly não confirma parâmetros continuamente emmemória. Nenhuma alteração feita pelo agente.

## Inspeção dos vídeos

Foram vistos os seis clipes por folhas de quadros (0;0,9;1,1;1,5;1,95;2,1;2,5;3;3,5;4;5;6s) e folhas adicionais de queda50/75%(2–6,5s emintervalos0,4s). Para tempos após o fim do clipe, o helper repete o último quadro com o timestamp real desse quadro; não interpretar como frames novos ou congelamento do jogo. Capturasoriginais não foram editadas.

25%:não há decolagem significativa inequívoca nas imagens; deslocamentos pequenos/rotação lateral impedem quantificar a subida e não provam falha deARM.50%/75%:há subida e retorno evidentes nos dois jogos. NoUncrashed a inclinação muda durante o voo, especialmente75%, deslocando/suprimindo o marco no recorte central; não inferir altura nem pico exato de pixels sem corrigir orientação. Não usar otempo deF10/fimdoAVI como impacto. Não atribuir essa inclinação à posição inicial:Douglas confirmou100%nivelado, e a hipótese degeometria do drone não foi comprovada.

Os retornos no teste50% aparecem em faixas semelhantes do clipe (aproximadamente4–5s), mas não são medidas instrumentais de contato e não sustentam igualdade degravidade ou massa. A amplitude visual entre mapas/FOVs não é escala física comparável. No75%Uncrashed o marco fica fora do recorte; não quantificar diferença de pico ou empuxo. Não afirmar que a curva está calibrada ou que o motor está exatamente igual.

## Conclusão prática

ManterThrottleCurve0 como base provisória, sem nova recomendação numérica por estas imagens. A resposta nos níveis intermediários foi exercitada, porém não apareceu evidência suficiente para escolher uma curva de compensação específica. Preservar massa400g/gravidade11,34/TWR8,407243 e rates atuais. Não pedir nova bateria idêntica nem ajustar física para compensar a deriva. Para afinar além da sensação de pilotagem, será preciso observar os eixos realmente recebidos e/ou instrumentar posição/velocidade/atitude noCyberpunk; essa telemetria ainda não foi implementada. Não apresentar coeficiente inventado a partir do tempo do clipe.

## Evidências e limites

HelpersC:/Users/Douglas/Desktop/Mods/inspect-throttle-20261102.ps1 e inspect-throttle-queda-20261102.ps1 reutilizamFpvVideoSheets. Houve erro na geração do segundo conjunto:interpolaçãoPowerShell impediu renomear a folha dequeda, seguida de tentativa de abrir nome ausente. Corrigido o nome porpatch e regenerados ambos os conjuntos; apenas folhas derivadas locais foram substituídas, nenhumAVIoriginal. SaídaAnalise-Aceleracao-20261102/pulsos.json e folhas normais/-queda. Auditoria/Comparacao-FPV-20261102/Aceleracao-1549-1553 preserva metadados/comandos/resumo, sem copiar vídeos. Não enviar perfis pessoais/logs aoGit.
