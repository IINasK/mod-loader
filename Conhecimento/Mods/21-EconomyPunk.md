# EconomyPunk Standard

- ID: `21-EconomyPunk`
- Versao fixa: 4.7.2; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: unverified.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Rebalanceia preços,saques,vendedores,recompensas,custos de serviço e valores dos veículos.

## Como usar

- Age automaticamente na economia; versãoStandard é fixada. Não é uma loja virtual para veículos adicionados.

## Dependencias

- [ArchiveXL](ArchiveXL.md) — v1.28.2 (`ArchiveXL`).
- [TweakXL](TweakXL.md) — v1.12.0 (`TweakXL`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).

Requisitos autorais registrados: ArchiveXL,TweakXL e dependências fixadas. A descrição atual o chama deScriptFree; o catálogo ainda inclui redscript por política/base.

## Phantom Liberty e compatibilidade

PacoteStandard contém dezenas de referênciasEP1 emvendors/recompensas/veículos. Sem prova de que oTweakDB/recursos e essas alterações são seguros semPL, não liberar comofalse pela mera ausência deDLC na descrição.

Nivel da evidencia: Exact Standard recipe has unguarded EP1 vendor/quest records. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Pode sobrepor preços/estoques de outros rebalanceamentos.
- Mudança de valores pode ser comportamento esperado, não bug.
- Não foi criada variante modificada removendo osYAML deDLC.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: EconomyPunk - Standard 16952 4.7.2 2026-10-06T12-37Z 2w0lw1T19.7z; SHA256 `1c883ae9f201166b683eb2eb80f5463ef265a62bfd246a9d4128de71d8837939`.
- `archive/pc/mod/EconomyPunk.archive` — 8192 bytes; SHA256 `390f4bf99566601d08411c5961f21d08f96165929dfeefddcb5a9e2bfacdba28`.
- `r6/tweaks/EconomyPunk/A_Records_EconomyPunk.yaml` — 21469 bytes; SHA256 `c5957c9044b7b5550166f60e1f125c5980a04ca2f523d6b7605c80e607664a20`.
- `r6/tweaks/EconomyPunk/B_Fixes_EconomyPunk.yaml` — 35850 bytes; SHA256 `a5fc3991030571442b159debfa750088023583fef84450eec85e623316e44445`.
- `r6/tweaks/EconomyPunk/C_Clothing_EconomyPunk.yaml` — 11866 bytes; SHA256 `ecf9f79cffd93bbda998f38cb5937610b06cbbbe6fd5db6f73ecf0193854ca97`.
- `r6/tweaks/EconomyPunk/D_Vendors_EconomyPunk.yaml` — 18009 bytes; SHA256 `6d3bd322042059541170c6e9b840cb5238c4f60d629dbe4d250507aca64371f8`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Clothing.yaml` — 2701 bytes; SHA256 `517e53d13259e549ecc0d3c6eb142a1ee6ca397caa0d1c0c85abc5a44cae6f18`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Consumables.yaml` — 2330 bytes; SHA256 `d292be3190436ac44b4e2562d0dc61445a78d83b46d4431bea2976662ec91ecc`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Craft_Upgrade.yaml` — 5784 bytes; SHA256 `b9756045e81d4159f867c64f391e193bcfd3ed6856489c66422c5208b1eb8332`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Cyberware.yaml` — 386 bytes; SHA256 `233e6408a2dfa152664c2f543fb7c2d8caf82128e608fd861734fd721d223dbb`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Loot.yaml` — 20705 bytes; SHA256 `4d141ca749a12dfaa26e11b06b0df6bb1eeedff9c10644747b21463f21dc5043`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Misc.yaml` — 5880 bytes; SHA256 `d05d243bb12d4e5ceca7cc0b6d5d5d2ad5cfbdbd87578e61d8145d7ba80b366a`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_QuestRewards.yaml` — 61935 bytes; SHA256 `2788a0226b73edda1c7dde3bed4b7edb786be24ef7ba5af78a8c1f98e4133a25`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Vehicles.yaml` — 7612 bytes; SHA256 `a7f1fd12a32d34fdd148fdb7e09a7ed7c324faa545605e58f4a231d6dc78ff8b`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Vendors.yaml` — 2488 bytes; SHA256 `ca0272a4304499ccc26a61fa4dc52a8701b72750417851784cb9d609ec9fc31d`.
- `r6/tweaks/EconomyPunk/MODIFY_EconomyPunk_Weapons.yaml` — 4087 bytes; SHA256 `dfaefac8c670dae55ab1a07373863a6f015bf0434e904315571c2f8a32a0c5e7`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/16952

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.