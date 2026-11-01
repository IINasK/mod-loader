# Cyberpunk Coop 1.0.25 — Night City Alive e limpeza do aviso

Solicitação: identificar a versão instalada do Night City Alive e retirar as pastas que apareciam como extras no aviso do coop. Não atualizar RED4ext nem reinstalar mods como efeito colateral.

O instalador identifica a versão por correspondência dos arquivos conhecidos com os hashes das receitas, incluindo a variante antiga Baixa personalizada. A versão reconhecida aparece no resultado da varredura, no seletor de densidade e nos detalhes junto da variante. Arquivos alterados, ausentes ou parciais não recebem uma versão instalada presumida do catálogo. A versão fixa do pacote continua separada da versão reconhecida; não há consulta ao Nexus para esta conferência local.

Inspeção somente leitura do jogo real: Night City Alive 2.3, Baixa personalizada, 24 arquivos corretos, zero ausentes/diferentes. Preservado aggressiveCiviliansLimit: 1. A detecção identifica o pacote instalado em disco; não é leitura do processo do jogo e requer reiniciar o jogo após uma troca de variante.

Com o jogo fechado e após autorização, foram removidas somente mods/NIGHT CITY ALIVE, r6/tweaks/bmw_m4_csl e r6/tweaks/yamaha_r1, todas novamente verificadas sem arquivos, incluindo ocultos e sem links de diretório. A primeira continha 18 subpastas vazias. Exclusão de baixo para cima sem remoção recursiva de conteúdo. As três pastas estão ausentes; o Night City Alive ativo em r6 permanece. Não há ZIP faltante a baixar para corrigir esses três nomes. BMW e Yamaha permanecem retidos pelos motivos anteriores; não reabilitar. Os dois grupos de hashes divergentes com o amigo (ITP/NCA) ainda exigem comparar o outro PC; limpar os extras não comprova resolução dessa divergência.

RED4ext preservado: 1.31.0, SHA256 58ddfe43766aa91249d11671b55a8323d5ec017f816d847affe4b6201dcd0709. Nenhum save, corpo, preset pessoal, Spotify ou parâmetro de voo alterado.

Compilação concluída. 906 verificações registradas em 24 relatórios passaram em jogos fictícios em D:/t25b. Incluem variantes, versão por hashes, ausência/parcial/alterado sem falsa versão, detalhes, fila e monitor, importação e troca de densidade. Prévia da tela de densidade examinada. O primeiro ensaio no contexto restrito falhou na proteção de dados do Windows; o segundo com perfil normal e caminho longo falhou no limite de 260 caracteres; um ensaio intermediário encontrou o seletor do teste ainda usando o texto antigo do cabeçalho, corrigido em OnlineDensityWindowTests. Ensaio final com caminho curto e perfil normal passou. Não enfraquecer criptografia para contornar o contexto de teste.

Executável único: Cyberpunk Coop 1.0.25.exe, 1.465.344 bytes, SHA256 d1853306e34d0b8fc4ada7591f38cc458652bafd6a30cc4c8ca04de097c1ddfa. Catálogo embutido inalterado, SHA256 738527fc15e0de06fcdcb33f4b8075bec369b3e1982f71460e8df8d98b0a7f4d. Não inclui mods, saves ou credenciais. Não houve teste de nova sessão coop para conferir o desaparecimento do aviso após a limpeza.
