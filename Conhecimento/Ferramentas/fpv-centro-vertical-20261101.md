# FPV — centro do stick e comparação vertical, 01/11/2026

Pedido: avaliar resposta perto do centro e inércia de subida, pico e começo da queda, comparando Cyberpunk com Uncrashed. Usuário relata que Uncrashed parece mais arisco/esperto para virar. Isso é compatível com os contatos de pulsos 25%, mas não prova uma causa física específica.

Fontes: Uncrashed d9182a41/01-Uncrashed/ID1 e Cyberpunk 2d1871ad/02-Cyberpunk/ID10. Comparados contatos roll/pitch/yaw 25%, verticais, comandos enviados e rastreamento de referências do cenário. A região de 25% ainda produz menos giro acumulado no Cyberpunk nos três eixos, visivelmente em roll e pitch. Não chamar o centro de calibrado; os testes não mediram especificamente 5–10% nem a inclinação infinitesimal no centro. O máximo próximo de 660 não implica curva inteira equivalente. A proposta de Center 250 somente no roll permanece experimento, não valor final demonstrado; nunca atribuir o teste ID10 a 250, pois taxas salvas eram 200/0,40/660 e a mudança só foi sugerida depois.

## Sincronização da vertical

Uncrashed: captura iniciou clock 192,6129685; comando de subida 193,611954..193,985654, pulso 0,373700 s. Em tempo do vídeo, começou t~0,999 s e cortou t~1,373 s.

Cyberpunk: captura iniciou clock 11205,6868359; comando de subida 11206,682770..11207,056575, pulso 0,373805 s. Em tempo do vídeo, começou t~0,996 s e cortou t~1,370 s. Os pulsos efetivamente enviados são praticamente iguais, embora o campo nominal seja 0,35 s. Esses registros não são telemetria de motor/empuxo recebido pelo jogo.

## Pico e descida — estimativas de imagem

Uncrashed: pico visual t~2,6–2,7 s, início perceptível da descida t~2,8–2,9 s. Cyberpunk: patamar do ponto próximo t~2,5–2,9 s, descida claramente identificável t~3,0 s. Centro do intervalo de pico em torno de 2,7 s em ambos; aproximadamente 1,3 s continuando a subir depois do corte. Reportar como proximidade temporal, com incerteza de décimos de segundo, não medição exata de gravidade.

Retorno/contato aparente: Uncrashed ~4,7–4,9 s do vídeo (mudança/embaçamento e tombo), Cyberpunk ~4,5–4,7 s (mudança abrupta de enquadramento e acomodação). Cyberpunk parece retornar um pouco antes, cerca de 0,2–0,3 s; não dar isso como tempo de impacto medido por sensor. Não usar momento de F10 como impacto. Não converter a diferença de tempo diretamente em porcentagem de gravidade ou massa.

## Método e limites

Tracker C# reutiliza o leitor dos JPEGs AVI existente; imagens reduzidas para 320x180, amostras a cada 3 frames (0,1 s), resolução de deslocamento de 3 pixels originais. Faz correlação normalizada e auto-checagem de que o quadro de referência retorna deslocamento zero e correlação ~1. CP ponto próximo com correlação ~0,93–0,96 no pico e ponto distante ~0,91–0,95; ponto próximo desceu na imagem até ~66 pixels (t2,5..2,9), referência distante ~39 pixels. A diferença mostra por que não se deve interpretar todo deslocamento como altitude: há movimento de câmera/paralaxe. A subtração é apenas uma verificação de sensibilidade, não reconstrução de altitude 3D.

Primeiro tracker da caixa UC, referenciado em t1, perdeu correspondência perto do pico por sair da janela e baixa correlação; foi descartado para o pico. Segunda passagem referencia caixa em t2,5, confirmando máximo t2,6–2,7 e correlação ~0,90–0,94. Descartar trechos de baixa correlação, não suavizar falhas para produzir resultado. Evidências em Auditoria/Comparacao-FPV-20261101/Centro-Vertical, incluindo CSVs e imagens; originais preservados. Campo CameraAngle 20 é SavedOnly e não prova ângulo em execução. Não comparar pixels de subida entre jogos como metros ou porcentagem de potência, pois distância, perspectiva, FOV, inclinação e deriva diferem.

Conclusão prática: timing do conjunto de subida/coasting/pico é razoavelmente próximo, mas altura/velocidade vertical e cada parâmetro individual não estão calibrados por estes dados. Não há evidência suficiente para alterar gravidade 11,34, massa 400 g ou referência motor 8,407243 agora. Em um salto curto, tempo ao pico restringe uma relação entre impulso e desaceleração; diferentes combinações de empuxo, massa, gravidade, arrasto e giro residual podem gerar tempos parecidos. Queda livre ideal não determina massa. O maior desvio observado permanece na resposta angular a pequenos e médios comandos. Nenhuma alteração no jogo ou EXE feita nesta auditoria.
