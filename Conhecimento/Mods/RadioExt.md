# RadioExt

- ID: `RadioExt`
- Versao fixa: 2.92; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Permite rádios personalizados,incluindo a estação doSpotOnRadio, em veículos e rádios físicos.

## Como usar

- Funciona automaticamente para estações instaladas; nenhuma loja ou painel especial é necessário para sintonizar.

## Dependencias

- [NightCoop + RED4ext 1.31.0](NightCoop.md) — 0.1.2-beta (`NightCoop`).
- [CET](CET.md) — v1.38.1 (`CET`).

Requisitos autorais registrados: CET atual eRED4ext>=1.30.0; pacote2.92 é para jogo2.5.

## Phantom Liberty e compatibilidade

Framework de rádio com19arquivos auditados,15Lua/2DLL/metadata/.keep, sem recursosEP1. Autor exigeCET eRED4ext>=1.30.0, nãoPL.

Nivel da evidencia: Generic radio framework and exact package/API audit. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Não remover enquantoSpotOnRadio depende dele.
- Estações adicionais/dados do usuário devem ser preservados ao desativar.
- Número interno2.4.0 refere-se àAPI nativaRadioExt, não aRED4ext2.4.0.

Coop: Instalados localmente29/10/2026; sem login/reproducao/sincronizacao musical testada pelo agente.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: RadioExt 2.92 4591 2.92 2026-10-25T20-46Z YfTif77t6.zip; SHA256 `af2d7de462bdf735863716c2a6686b9ad741ee2f76cf9b8bbaaa7e0967bd020b`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/init.lua` — 3752 bytes; SHA256 `e78d5750b9ec14c5e98c78689fd32102ac9ef3c1d258c2c1a9f4995d0525be41`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/metadata.json` — 438 bytes; SHA256 `14b110212305a6a5ff53d5bf8ae2185acff286f31b950a4b0748fc00635345e0`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/physical/observersP.lua` — 5131 bytes; SHA256 `33cb498f018bc60e7cacc2a92083718c70ab1062e7c34c66da5e0d2cad651e76`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/physical/radioManagerP.lua` — 1981 bytes; SHA256 `564a015f38af5700aa0bfe5b3a2280a5a2ca47012bfb6060c4ff27697706be91`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/physical/radioObject.lua` — 853 bytes; SHA256 `6eacf260a0c9ddad68401645f57825380365f5ed46bd679544a619c45bf89d39`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/radioManager.lua` — 7432 bytes; SHA256 `a4555ad39881c757ff7782ae5052dd49ab666299b58f29b3b97a0cc66f630f4c`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/radioStation.lua` — 6914 bytes; SHA256 `b2814e703ef7f1a9c17962e7a3759542f3117a25ba528eebfbff440943f77052`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/utils/audioEngine.lua` — 1383 bytes; SHA256 `cc149c764e3780ed0b5e524ae2f78b0ebcafe0eb4d2cffb9b209faa723b3b957`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/utils/config.lua` — 1288 bytes; SHA256 `3add1d983a62f2720f40b00921971dbf5b034dc855e4d94a052fc723d81a9183`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/utils/Cron.lua` — 4509 bytes; SHA256 `9ac6ee34d6af5a50705ead3fa5fac4a3f6dd53c61b2f86f2730e2fad22e3e16a`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/utils/GameSettings.lua` — 8354 bytes; SHA256 `335f6ed309e76c959715e89198e0b08bc21ab20f73145e3521de4a38cdd0116e`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/utils/GameUI.lua` — 45433 bytes; SHA256 `7a36452db6138dd20922070ae9694951641f8c3d74d220c147d1ffa677747774`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/utils/logger.lua` — 723 bytes; SHA256 `01ae8ce52e44bf337355635e651ec16865a1e29d921975f9c73cad77115a3325`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/utils/utils.lua` — 2112 bytes; SHA256 `25b9a5a582cf6721556b404b409b34603aeb8d79df82a153698ab841ffd9539b`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/vehicle/observersV.lua` — 16151 bytes; SHA256 `5365cfdb87ae9404656cfc4d23ffd6b5f0b0ba1b2c78954363d39a76192ede9e`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/modules/vehicle/radioManagerV.lua` — 3294 bytes; SHA256 `ed6f05f0f4bbab4ec6f4fc371186f49cae44fbad8ff09b3fc3b4c7f5d7571eb6`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/radios/.keep` — 0 bytes; SHA256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- `red4ext/plugins/RadioExt/fmod.dll` — 1876480 bytes; SHA256 `b15b807f37d38ba95ce037195990a1dc11952f24c55963d3994d20385a4b98fb`.
- `red4ext/plugins/RadioExt/RadioExt.dll` — 143872 bytes; SHA256 `853d86b4acb627c179f4a420729ac2daabf312c4f64494634785fe24a882902a`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/4591
- https://github.com/justarandomguyintheinternet/CP77_radioExt

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.