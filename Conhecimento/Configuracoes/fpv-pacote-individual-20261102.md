# Pacote individual FPV NASK — 2026-11-02

Douglas aprovou a sensação após o teste de propwash e solicitou compartilhar somente FPV/dependências com outro piloto, sem coop/roupas. Confirmou jogo 2.5 no destinatário. Depois esclareceu preferência por ZIP de pastas prontas, sem instalador próprio; a preparação de novo EXE foi abandonada antes de compilar. Nenhuma alteração no jogo nesta exportação.

## Entrega

- Arquivo local: `C:\Users\Douglas\Desktop\Mods\Cyberpunk FPV NASK.zip`.
- 47.769.930 bytes, aproximadamente 45,6 MiB; 80.866.462 bytes extraídos.
- SHA256: `01970dc6f4b6d2d333796f63717f7251096f975c0b9afe725fb22feccaaddfc7`.
- 113 arquivos de instalação + LEIA PRIMEIRO.txt, NASK-ALTERACOES.txt e MANIFESTO.json.
- Não guardar o payload em Git nem enviar automaticamente a terceiros. ZIP mantido fora da pasta do projeto para não repetir o acúmulo de mods já limpo.

O destinatário extrai e copia archive/bin/engine/r6/red4ext para a raiz do jogo. Guia cobre VC++ x64 oficial, primeiro início do CET, tecla Toggle, perfil NASK, seleção/calibração do próprio rádio e ARM, salvamento por Overwrite Profile, cuidados com dependências já existentes e inventário para desinstalação manual.

## Composição

Night City FPV 1.31.4; RED4ext 1.31.0 baixado diretamente da distribuição oficial separada de NightCoop; CET 1.38.1; redscript 0.5.32; TweakXL 1.12.0; Codeware 1.21.1; Audioware 1.10.1; Native Settings UI no commit 4065b51456f5e3df8f29e1dc3c07edcc294e68e9.

Original FPV já disponível em Downloads. Dependências baixadas dos links oficiais fixos e conferidas por SHA256 do catálogo; RED4ext conferido com o SHA publicado em sua release oficial. Extração usa lista explícita de caminhos/entradas e hashes do catálogo, não cópia de pastas inteiras do jogo. Sem ArchiveXL ou KillTrigger, opcionais na descrição oficial; sem coop, roupas, veículos ou Spotify.

Incluídos somente os três Lua alterados aprovados, com hash de origem/resultado conferido: settings.lua (Center até600), profile.lua (leitura até600), physics_model.lua (pico0.15/peso de corte1.00). O resto vem das distribuições fixas. Perfil NASK real exportado com seleção inicial própria. Perfis de fábrica vêm do ZIP original, não da cópia local personalizada.

Captura NASK: Roll400/.5/660, Pitch500/0/660, Yaw500/0/660, Feedforward120, gravidade11.34, massa400g, referência força8.407243, câmera30, FOV135, aspecto16:9. Conferir JSON para precisão completa. Ajustes pessoais de bindings CET, logs, contas, saves, caminho/GUID/calibração do controle não incluídos. Calibrador, ViGEm e emulação Xbox não são dependências de voo com um rádio USB reconhecido.

## Verificações e limites

Todos os downloads conferidos antes da extração; cada arquivo original conferido após extração; arquivos customizados comparados com hashes aprovados; todos os113 arquivos finais novamente lidos dentro do ZIP e comparados com o manifesto. Conferência de exclusão de dados pessoais/mods fora do escopo. Nenhuma DLL ou executável de terceiros executado e nenhuma escrita no jogo. Não houve início em instalação isolada contendo exclusivamente estes oito componentes, nem teste separado sem Phantom Liberty. A aprovação do voo é a do usuário em sua instalação completa.

Licenças e avisos fornecidos nas distribuições foram preservados. FPV inclui GPL-3.0-or-later e THIRD_PARTY_NOTICES para o programa Lua; alterações estão identificadas em NASK-ALTERACOES.txt e o código Lua está incluído. A seção de permissões do Nexus restringe upload do pacote em outros sites, apesar da licença dos scripts; não interpretar como autorização de publicação dos assets completos. Não foi publicado/reupado o ZIP em serviço externo; guia informa que não é distribuição oficial nem deve ser publicado como reupload.

Fontes: https://www.nexusmods.com/cyberpunk2077/mods/31642?tab=description ; https://github.com/WopsS/RED4ext/releases/tag/v1.31.0 ; https://wiki.redmodding.org/cyber-engine-tweaks/getting-started/installing

Geradores e evidências locais: `C:\Users\Douglas\Desktop\Mods\preparar-fpv-individual.ps1`, `montar-zip-fpv-nask.ps1`, e `FPV-Individual\resultado-zip.json`. Não executar novamente sem escolher destino novo ou conferir explicitamente a preparação existente.

## English package

Requested by Douglas for an American recipient. Deliverable: C:\Users\Douglas\Desktop\Mods\Cyberpunk FPV NASK English.zip (47,769,659 bytes). SHA256: e5ec4b60bb91e2b37ab4d36165d8389930f66037070fef3eccb3f4f4c42dbe10.

READ FIRST.txt and NASK-CHANGES.txt replace the Portuguese guides; MANIFEST.json replaces MANIFESTO.json. All 113 mod/profile files were verified byte-for-byte against the original manifest after ZIP creation. No game files changed. English preparation does not add a new runtime validation. Source helper: C:\Users\Douglas\Desktop\Mods\FPV-Individual\translate-pack.ps1. The original Portuguese ZIP remains available separately.

English guide correction: connect USB first; configure Toggle in CET Bindings and calibrate/assign ARM in FPV Drone Controller Setup within the same CET overlay, then close CET and load the NASK profile under Esc > Mods. Corrected the misleading order after Douglas reported it. Rebuilt the same English ZIP and verified all 113 mod/profile hashes unchanged. Current SHA256: 77890c2674825c4e40757d0863b5ca01cfc9ac28a319d3d5a9125625a94c0e1e; 47,769,763 bytes. Previous English ZIP retained in FPV-Individual/English-before-guide-fix.zip. No game files changed.
