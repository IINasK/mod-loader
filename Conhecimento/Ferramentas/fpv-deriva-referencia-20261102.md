# FPV — deriva na referência Uncrashed — 02/11/2026

Douglas observou que o Uncrashed pende para um lado e questionou se isso contamina a comparação. Reinspecionada a folha de subida de20261031-040122-Serie-Uncrashed-Cyberpunk-d9182a41/01-Uncrashed/vertical_100_rep01_run0001_id1. Há mudança de posição/projeção e pequena inclinação visual durante a subida/queda; as grandes mudanças de orientação depois do retorno ao solo não podem ser usadas como queda livre ou resposta de rates. Não foram medidos ângulos exatos.

Commands.csv contém711linhas para o clipe vertical e0linhas com sent_roll/sent_pitch/sent_yaw diferentes de0. Isso comprova comandos enviados neutros, NÃO leitura real dos eixos recebidos pelo Uncrashed, nivelamento inicial, ausência de vento ou velocidade lateral inicial. Não atribuir causa a drift doDualSense, macro, vento ou simulador sem conferir sinais/estado.

Distinção necessária: deslocamento lateral desloca marcos de cenário e confunde medição pela imagem; inclinação inicial altera a direção do empuxo durante a subida e pode alterar trajetória/apogeu; rotação espontânea do corpo se soma ao pulso de teste. Impacto/rolagem no solo não devem ser classificados como rotação comandada. O segundo de potência zero não garante que o drone esteja nivelado e sem movimento.

Consequência: os vídeos continuam úteis para comparação grosseira e sensação de pilotagem; não sustentam ajuste fino exato de curves, gravidade/massa/motor. As bases500/0/660 emPitch e400/0,50/660 provisória emRoll foram aproximações experimentais, não equivalência instrumental. Registro atual continuaRoll500/0,Pitch500/0,Yaw400/0,conforme foto e sessão00:33; não substituir estado atual pela proposta anterior.

Próximo controle de qualidade: observar eixo recebido no próprio simulador com macro neutro; resetar posição antes do trecho, conferir horizonte nivelado e padronizar vento se houver. Não mudar gravidade/potência para compensar deriva lateral e não exigir que Douglas refaça todos os vídeos. Reaproveitar trechos válidos; registrar entrada e rotação real no Cyberpunk antes de alegar medição precisa. Nenhum logger foi implementado ou arquivo do jogo alterado nesta conferência.
