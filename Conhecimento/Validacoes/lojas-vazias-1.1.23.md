# Lojas vazias — Mod Loader 1.1.23

Removidas somente quatro lojas individuais: Nola Follow Your Passion (12642), Veegee Shop4 (30021), Veegee Jewelry Atelier (8688) e Kwek Sartorial Omnibus (6779). Oito arquivos de registro/apresentação movidos para backup com hashes conferidos. Nenhuma roupa ou acessório foi removido. Catálogo144: todas as 144 receitas restantes são idênticas às anteriores.

Auditoria local: scripts de registro AddStore cruzados com registros Items dos YAMLs instalados e dependências do catálogo. As quatro lojas não tiveram correspondências nem pacotes dependentes. A lista de dependências sozinha não foi usada para classificar loja vazia: Nola x Veegee, loja de acessórios Nola e Veegee Shop1 possuem itens encontrados mesmo sem dependência explícita de roupa no catálogo. Não houve consulta runtime ao TweakDB; a auditoria estática não cobre qualquer registro criado dinamicamente ou recursos internos não inspecionados de archives.

A instrução posterior de manter packs de lojas prevaleceu: o pacote raem (14210), com seis lojas, permaneceu integralmente, incluindo as duas sem suas coleções instaladas. Seus 12 arquivos foram reconferidos após a remoção. Base NightCoop/RED4ext, CET e RadioExt também foram preservados e conferidos.

Catálogo e duas listas persistidas/presets atualizados. Retirada conhecida impede o retorno automático de IDs/ZIPs antigos; inclusão custom explícita futura permanece possível. Quatro recibos concluídos foram validados contra o catálogo anterior e preservados no histórico, com backup, para não bloquear operações posteriores. Registros incompletos ou desconhecidos não foram ignorados.

Testes específicos de retirada/migração e catálogo144 aprovados; pacote raem completo verificado. Compilação e Setup concluídos; Mod Loader instalado1.1.23 confere com o executável preparado por SHA256. Suíte geral não repetida: falha preexistente de Deactivate documentada na revisão1.1.21. Sem lançamento do jogo ou confirmação visual das lojas nesta revisão. Saves, calibragem FPV e dependências compartilhadas não alterados. Sem publicação remota.
