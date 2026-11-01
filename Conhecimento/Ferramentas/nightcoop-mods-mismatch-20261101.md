# Aviso de mods divergentes com Coelho — 01/11/2026

Douglas pediu deixar a calibraçãoFPV emstandby e investigar a imagem doNightCoop. Não continuar refinamentoFPV sem nova solicitação. Auditoria somente leitura no jogo; não houve limpeza, reinstalação, atualização ou alteração deconfigurações.

## Evidência de execução

red4ext/logs/nightcoop-2026-11-01-19-36-15.log,19:37:30: local189mods, peer186; o peer não tem três nomeslocais, local não falta nenhumnome dopeer; duas entradasdivergem porhash. Os nomes completos são REDmod:NIGHT CITY ALIVE, tweak:bmw_m4_csl, tweak:yamaha_r1; divergentes CET:immersive_third_person e tweak:NightCityAlive. O mesmo resultado apareceu19:35:52,19:21:45 e18:51:48. Embora o usuário tenha se referido ao jogo do amigo, esse mesmo aviso está no log local e é orientado local→peerCoelho; não inverter a direção.

## Três extras são pastas vazias, não payloads faltantes

Conferência recursiva incluindoHidden: mods/NIGHT CITY ALIVE existe com0arquivos e18subpastas também vazias; r6/tweaks/bmw_m4_csl e yamaha_r1 existem com0arquivos/0subpastas. Nenhum archive correspondente foi encontrado emarchive/pc/mod. OmanifestNightCoop registra cadauma comsize0/hash4fd0bfc1/files0. Portanto a detecção asanuncia apesar de não haver conteúdo. Não mandar o amigo instalarREDmod ou osdois veículos para resolveresses trêsnomes. BMW/Yamaha continuam retidos no catálogo por falhas previamente reproduzidas2.5; não reabilitar.

Correção preparada conceitualmente: comjogo fechado, remover ou mover para fora da árvore dojogo somente essas trêspastasvazias após revalidar zeroarquivos, preservando oNightCityAlive semREDmod emr6/scripts er6/tweaks. Não executarremoção recursiva de mods ou r6/tweaks inteiro. Nesta auditoria nenhuma pasta foi movida ou apagada. Reabrir jogo faznovascan; não editarNightCoop.dll nematualizarRED4ext1.31.0.

## Night City Alive

Local: presença do perfil antigo personalizado de densidade baixa: r6/tweaks/NightCityAlive/aigeneralsettings/zz-coop-low-density.yml, conteúdo aggressiveCiviliansLimit:1, SHA87db72bf73fde677459bfca0c87e77bbef7be7de62a8775ea35715aec555fe62. Os dois arquivoslow-to-std.yml da receitaMédia estão ausentes. Essa estrutura é reconhecida comoLegacy peloOnlineDensity.cs; não substituir silenciosamente. Os outros arquivos conhecidos da receita conferem. Pasta tem20arquivos/30159bytes e fingerprintlocalfc7851e6. Opeer tem a entradaNightCityAlive, pois ela consta em“versão diferente”, não comoausente. Densidade diferente, patchlocal ausente nopeer ou arquivosdiferentes são hipóteses; só uma comparação dopeer pode cravar aorigem. Alinhar a mesma variante/configuração de encontro preservando a decisão de baixa densidade; não instalarREDmod.

## Immersive Third Person

Os arquivos da receita2.0.1 conferem comcatálogo local. ManifestCET registra4arquivos/684577bytes, hash75776c64. Os quatroLua atuais somam exatamente684577bytes: api97665, camera_motion13075, init567892 e settings5945. Osettings.lua é pessoal e foi modificado01/11 às18:36:47 (SHAaebf87c4c9e0262b1bfddac8ec6527da5e549f118711f449489cf802ed1bc978). Assim, “outra versão” é uma divergência do conteúdo comparado, não prova de versão numérica/compatibilidade. Settings diferentes são uma hipótese forte, mas falta a cópia dopeer para determinar qual arquivo. Não sobrescrever câmera/presets pessoais para eliminarum aviso sem conferir.

## Limites e próximo passo

Acesso somente aoPCDouglas; nenhum arquivo do amigofoi lido e nenhuma mensagem externa foi enviada. Para concluir diferençasporarquivo, obter nightcoop-mods.txt do amigo e seuCET/immersive_third_person/settings.lua; paraNCA, inventário de hashes dos arquivos r6/tweaks/NightCityAlive ou pasta correspondente. Omanifestsozinho identifica grupos, não todos osarquivos internos. Não dizer que opróximo ajuste é reinstalarpacoteinteiro. O log mostra notificaçãoinfo com15s, não comprova bloqueio/conexão ou causa decrash.

Evidências recortadas locais emAuditoria/NightCoop-Mods-Mismatch-20261101. Não publicar logscompletos, dados desessão, desafios, IDs deinstalação, nomes pessoais ou configs noGit. Esta ficha guarda somente achadostécnicos. Arquivos doFPV, saves eRED4ext preservados.