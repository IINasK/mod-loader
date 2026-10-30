# Immersive NCPD Hotline

- ID: `15-NCPDHotline`
- Versao fixa: 3.1.11; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Permite subornar a polícia para apoiar combate, encobrir crimes e evitar perseguição por falsa acusação.

## Como usar

- Segurar a teclaOpen Main Menu do mod, normalmenteH no layout inglês, ou usar a janelaCET/atalhos configurados.

## Dependencias

- [CET](CET.md) — v1.38.1 (`CET`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).

Requisitos autorais registrados: CET e redscript fixados.

## Phantom Liberty e compatibilidade

common_base_game_data.lua declara suporte ao jogoRelic/base ePhantomLiberty. Documentação descreve um problema específico somente se oDLC estiver instalado, confirmando seu caráter opcional.

Nivel da evidencia: Explicit source comment supporting base game and optional DLC note. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Se shards deDogtown sumirem da lista, autor recomenda desativarWelcomeShard nas configurações e reiniciar o jogo.
- Menu interativo por tecla não oferece o mesmo acesso por gamepad; há ações/atalhosCET.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: Immersive NCPD Hotline 8557 3.1.11 2026-10-11T13-14Z xNHtNXVaL.zip; SHA256 `d493b3db9c77fe5895f7eb6b697f70a02ec3dbb0db90b038181ed825c0a16658`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/common_base_game_data.lua` — 35148 bytes; SHA256 `24d353fc5a6ee9fbae4d72600bf351eb300aee98b402500416cada89898e90df`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/init.lua` — 302774 bytes; SHA256 `6dbbdd49c622cc8e0184f00f8824b660d193aa04f993d1e1e3fdb38529d93adc`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/interactionUI_ncpd_hotline.lua` — 12216 bytes; SHA256 `eb77362631f02955cc27bb758adb5cdad7967fb8612a6ef2bf186a5004dc1824`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/LICENSE` — 1069 bytes; SHA256 `ebfd5f05db59b0d8d233c10967a546a3ee7635ff339be7a6688154cbf6215855`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/Ref.lua` — 1278 bytes; SHA256 `6ae5a3c3e470ad970c0de72d0a82e39a644ce65fc257701a958cc781e2dd9627`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/sms.lua` — 7252 bytes; SHA256 `3740b242a322b73dddca9046383f66cc24ca443a2214b7ca056f573d8308c4fd`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/game_base_relic/language/lang_pl_text.json` — 31973 bytes; SHA256 `3af66efd28c2292990b6b47fbac5d6035d04b4af023c8f7dbf3f0e149ffca094`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/game_base_relic/language/Readme.txt` — 13090 bytes; SHA256 `fd6f8ddbceffe9f5118eaba397282847684d9b0625ca965d8b6fb6d5cfd84e61`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NPCD_Hotline/game_base_relic/language/template_lang_en_text.json` — 22163 bytes; SHA256 `7e5d25e46ed6ea7e0560019c6a2498c2827bba115c0ea3fa6fea42a61f4b595b`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/stopNCPD/init.lua` — 467 bytes; SHA256 `60d9071cf992e819827df2ff5a91e6886990ca119089d8972c4b467e739df7fb`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/stopNCPD/mod_user_settings.json` — 2 bytes; SHA256 `44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/stopNCPD/Stop NCPD End of Life.txt` — 453 bytes; SHA256 `a76e0a0c0d9f13b261ebff25e2db71709994c950bc5a31b7f5e161294b6050cc`.
- `r6/scripts/NCPD_Hotline/ncpd_hotline_mod_reds_helper.reds` — 2977 bytes; SHA256 `2aa8ed99cd85102802550ccbc67d3271b47edc815ca64f9d99b57f84b7b32fdd`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/8557

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.