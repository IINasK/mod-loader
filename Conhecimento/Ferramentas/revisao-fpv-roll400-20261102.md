# FPV — Roll400 / Expo0,50 — 02/11/2026

Após orientação para mudar somente Roll Center200→400, Douglas respondeu “feito”. Detectada nova série20261102-001034-Serie-Cyberpunk-d346e101/02-Cyberpunk/ID5, concluída00:12:34(-03). NASKsalvo00:10:27 antes da gravação, confirmado pela leitura posterior e pelo snapshot SavedOnly emsession.json. Não é telemetria contínua de parâmetros em memória.

## Valores confirmados no arquivo

Roll400/0,50/660; Pitch400/0/660; Yaw400/0/660. Feedforward100, TuneTightness100. Gravidade11,3400001526; massa400g; referência de motor8,407243. Câmera20graus/FOV125. Contra snapshot23:28, a alteração de rates é somente Center doRoll200→400. Física e demais rates preservados.

13clipes,1555quadros,0duplicados/erros registrados. Inspeção visual nesta análise: quatro clipes Roll25/50/75/100, em folhas de tempos iguais às análises anteriores; não declarar inspeção visual dos outros nove. Gerador reutilizado, sem algoritmo novo. Integridade da captura não garante voo livre/ausência de colisões.

## Resultado e próximo passo

Roll25/50 apresenta maior rotação visual acumulada que na sessão23:28 comCenter200 e se aproxima visualmente da referência Uncrashed20261031-040122/d9182a41/01-Uncrashed/ID1. Não declarar igualdade, graus por segundo exatos, porcentagens medidas ou Feedforward como causa isolada. Ambientes e poses mudam.

O perfil salvo registra câmera20graus, embora o padrão de comparação combinado anteriormente fosse0. Inclinação da câmera muda a projeção da rotação do corpo; não é mudança da curva do controle. A leitura do arquivo não prova ângulo vivo durante todos os clipes, e o ângulo exato da referência Uncrashed não foi medido por telemetria. Evitar apresentar0graus da referência como dado instrumental confirmado.

Recomendação: manter Roll400/0,50/660, Feedforward100 e Tightness100 como base experimental. Padronizar câmera em0graus e salvar porOverwrite antes de outra comparação fina. Não subir Center ou mudar Expo simultaneamente por aparência de um vídeo com câmera diferente. Foco emRoll25/50, sem exigir outra série Uncrashed/vertical; os demais eixos ainda não estão declarados calibrados. Não recomendar novas mudanças de massa/gravidade/motor. Se a intenção passar a medição exata da resposta, será necessária telemetria de entrada recebida/setpoint/bodyRates, ainda não implementada.

## Evidências e preservação

Originais emDocuments/FPVCalibrador, folhas emC:/Users/Douglas/Desktop/Mods/Analise-Cyberpunk-20261102-001034, helperinspect-cyberpunk-001034.ps1. Snapshot privado e metadados emAuditoria/Comparacao-FPV-20261102/00-10-Roll400; sem duplicar vídeos, enviar perfil pessoal aoGit ou modificar arquivos do jogo. Nenhuma instalação, perfil do jogo, Lua, save, RED4ext ou executável foi alterado pelo agente nesta análise.