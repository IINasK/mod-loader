# Slice of Life

- ID: `18-SliceOfLife`
- Versao fixa: 2; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Permite colocar pontos de interação para tarefas domésticas,lazer e pequenos rituais deRP.

## Como usar

- Configurações>Mods>Slice of Life: escolher categoria eSet here para marcar a posição deV.
- Aproximar-se e olhar para o ponto criado; gerenciar/remover pontos pelo menu do mod.

## Dependencias

- [CET](CET.md) — v1.38.1 (`CET`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).
- [Native Settings UI](NativeSettings.md) — commit 4065b51 (`NativeSettings`).

Requisitos autorais registrados: CET,redscript;NativeSettingsUI opcional segundo autor.

## Phantom Liberty e compatibilidade

Pontos são colocados pelo jogador e usamfade,banner e sons nativos;10scripts fixos sem recursosEP1 obrigatórios. Não precisa de apartamentoDogtown para funcionar.

Nivel da evidencia: Generic base-game interaction APIs and reviewed scripts. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Não adiciona animações complexas; usa transição para preto.
- NativeSettingsUI é opcional para a página nativa;CET ainda pode configurar o mod.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: SliceOfLife V1 2 31319 2 2026-07-10T09-37Z UQrhQSt8J.zip; SHA256 `495a14896521847bf8c0a547af6ec2749f7a009637c3c58d7d4663bbeb9daac1`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SliceOfLife/actions.lua` — 5180 bytes; SHA256 `fa445e5acbd3e65c6a8489288c0cd4d9576aec64046f945d2c0bf058e8d625c3`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SliceOfLife/config.lua` — 1658 bytes; SHA256 `4839e29d5ffa0251f8c2f5ab16fb8f095eedd31a2a8271b42eda9842d90c9980`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SliceOfLife/init.lua` — 13415 bytes; SHA256 `db6545a4f0790bdb9c6d79d0b85a550db8dd3f782a9be6519ff52ef575a6dab3`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SliceOfLife/modules/fade.lua` — 2934 bytes; SHA256 `efbe401177cf13cb6e7f61b4f65bcd6ece52ef9dd16141099633924872e9a65c`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SliceOfLife/modules/external/GameUI.lua` — 44157 bytes; SHA256 `a72647faf49b2cbc000a05fd2922060ceb07b26cd23e737161d16592185c737f`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SliceOfLife/modules/utils/interactionUI.lua` — 5355 bytes; SHA256 `f6fb956d0b84d448828a0866e963b4fd5cfac7f1229ec4ce761388f2ce4b7186`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SliceOfLife/modules/utils/worldInteraction.lua` — 3807 bytes; SHA256 `4847c2463515444a8c21c400423868ddfa8526bdab8c6204f860ab178cec6819`.
- `r6/scripts/SliceOfLife/_Bridge.reds` — 1839 bytes; SHA256 `c87d1f2a0e8d715e01a33c933c72a15cb59e2d8344ff49181ab21fda2536cf4d`.
- `r6/scripts/SliceOfLife/_DarkFuture.reds` — 1151 bytes; SHA256 `c58001bd477d610a70c112b75f51ed6c4201f9783e7a2ce2a2c17889130f5648`.
- `r6/scripts/SliceOfLife/_SkillXP.reds` — 1965 bytes; SHA256 `9116aef440de07a7d5b38bfe98cf8f9d165ddb4ba6169aa7851e64875f81d2da`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/31319

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.