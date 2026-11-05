# Correção da identificação do ensaio de 22:33 — 01/11/2026

Sessão completa 20261101-223341-Serie-Cyberpunk-f0b97454/02-Cyberpunk, ID1; roll25/50 íntegros, comandos1,500623s e1,500443s. Pasta posterior22:35:19 contém só verticalID2 semcomplete, não é a série de rotação.

Inicialmente tratada como possível teste Center400 após orientação anterior. Pergunta explícita sobre valores em execução recebeu resposta: "te mandei a foto de como tava era aquilo lá". A foto codex-clipboard-45e8abf1-b18c-4af2-a5c5-a9c5729c1fe2.png mostra Roll Center200, Expo0,40, Max660, e pitch/yaw também200/0,40/660. Logo classificar ESTE ensaio como200/0,40/660, não como400. Foto não mostra Feedforward: seu valor vivo não foi confirmado nessa resposta. NASKsalvo também200/0,40/660 eFeedforward0, mas não inferir automaticamente esse último como vivo.

A estimativa anterior rotulada Center400 (~32graus em25%,~142graus em50%) não é evidência de resposta de Center400. A baixa coerência do método continua impedindo ajuste fino numérico. Não concluir que400 piorou, falhou ou não mudou a resposta física. Dados não sustentam subir para500/600. Não extrapolar esta confirmação para todos os ensaios antigos; manter seu grau de certeza/assunção registrado individualmente.

Patch limite600 foi realmente aplicado22:21:30 e hashes dos dois Lua conferidos novamente depois. Jogo iniciou22:26:14, após o patch. Ele apenas expande a faixa aceita e não seleciona valores experimentais nem alteraNASKautomaticamente. Confirmar o menu comRoll400/Expo0/Max660 antes de gravar novo ensaio, preservando pitch/yaw e física. Para persistir, usarOverwriteProfile e conferirNASK. Nenhum valor do jogo foi alterado nesta análise.
