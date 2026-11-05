# FPV — revisão do experimento Roll Center 250, 01/11/2026 21:30

Sessão completa 20261101-213054-Serie-Cyberpunk-5c34e961/02-Cyberpunk, calibrador 0.1.4, ID12: 13 clipes, 1.585 frames, zero erros. Um frame duplicado reportado somente na vertical; os clipes de roll não têm duplicatas. Terminou 21:32:43. Originais preservados.

Ensaio segue orientação de alterar apenas Roll Center Sensitivity 200 -> 250, mantendo Expo 0,40, Max 660, Feedforward 100 e Tune Tightness 100, pitch/yaw 200. Esses valores em memória são inferidos da continuidade do pedido/"gravei", não lidos como telemetria; perfil em disco ainda registra todos Center 200 e Feedforward 0. Não contradizer usuário com SavedOnly; não declarar confirmação explícita de 250 que não houve. Vídeo não registra o menu de taxas.

Comparações visuais e estimativa de orientação da câmera em roll 25/50. Método auxiliar: AVI JPEG reduzido a 320x180; gradientes fortes abaixo da faixa de anotação; média de orientação com simetria de 90 graus (quarto harmônico), continuidade temporal, diferença entre frames 55 e 100. Ângulos aparentes do cenário, não telemetria do corpo. Autochecagem matemática da orientação/continuidade em 11 posições sintéticas 0..240 graus passou; isto não valida a precisão absoluta em cenário 3D. Perspectiva, deriva, orientação inicial e mudança de composição causam incerteza. Coerência do início do C250 é menor (~0,19/0,23); arredondar e usar como estimativas, nunca apresentar décimos ou taxas corporais exatas.

Pulso roll25 ID12 começou clock 12561,580825; captura iniciou 12559,7023021 (t1,879). Roll50 começou 12570,313142; captura 12568,429366 (t1,884). A janela f55..100 cobre aproximadamente o pulso de 1,5 s, ligeiramente antes do fim; condições temporais próximas das referências, mas não exatamente alinhadas ao mesmo frame de onset. Não usar os números como medição precisa de ganho.

Estimativas de giro acumulado nessa janela:

| Stick | Cyberpunk anterior Center200 ID10 | Novo experimento Center250 ID12 | Uncrashed ID1 |
|---|---:|---:|---:|
|25%|~35–40 graus|~45–50 graus|~85–90 graus|
|50%|~130–135 graus|~145–150 graus|~220–225 graus|

A direção de melhora é consistente com a alteração proposta; ainda há déficit claro contra a referência. Não afirmar que os 25% do controle representam toda a região de 5–10% do centro. Detalhe de parada roll50 não mostra rebote repetido evidente no curto trecho; cenário próximo de geometria limita diagnóstico de frenagem livre. Não comparar perspectiva/tamanho de prédios como altitude.

Conclusão e próximo experimento: Center250 ainda não é calibração final. Para reduzir rodadas de incrementos pequenos, sugerir SOMENTE Roll Center400, mantendo Expo0,40/Max660/Feedforward100/Tightness100 e todos os demais parâmetros. É passo empírico reversível, não valor final calculado que garante igualdade. A resposta calculada pela curva aumenta perto do centro e no meio, mantendo o endpoint660. Avaliar primeiro roll25/50, sem solicitar nova série completa. Não alterar pitch/yaw, gravidade, motor, massa ou executar instalação como efeito colateral. Nenhum arquivo do jogo ou executável alterado nesta revisão.

## Correção posterior — limite do painel

Douglas informou que Center Sensitivity para em 250. Conferido no settings.lua: os três eixos usam intervalo 10..250 em passos de 10. A recomendação acima de Roll Center400 foi um erro e fica CANCELADA; não pedir esse valor no painel nem atribuir sua ausência a falha do usuário. Próximo ensaio dentro dos limites: manter Roll Center250/Max660/Feedforward100/Tightness100 e testar somente Roll Expo0,00. Pela fórmula local, reduzindo Expo0,40->0,00 o setpoint em 25% sobe de77,915 para88,125 graus/s e em50% de189,0625 para227,5, sem mudar a inclinação no centro exato ou o máximo. Isso é experimento de faixa baixa/intermediária; não resolve por si só uma eventual diferença de dinâmica/PID e não garante resposta igual à referência. Sem alteração de arquivo do jogo.
