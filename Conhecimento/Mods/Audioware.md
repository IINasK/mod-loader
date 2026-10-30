# Audioware

- ID: `Audioware`
- Versao fixa: v1.10.1; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Carrega áudio personalizado e fornece APIs de som a mods de fala/imersão.

## Como usar

- Infraestrutura automática; os mods consumidores expõem seus controles.

## Dependencias

- [NightCoop + RED4ext 1.31.0](NightCoop.md) — 0.1.2-beta (`NightCoop`).

Requisitos autorais registrados: RED4ext e patch suportados pelo assetWindows.

## Phantom Liberty e compatibilidade

Ferramenta de áudio genérica;18scripts conferidos não impõem recursosEP1. Cada banco de voz consumidor é auditado separadamente.

Nivel da evidencia: Official audio-framework role and package scripts. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- ImmersiveVDialogue exigeAudioware>=1.5.0;1.10.1 fixado atende.
- Não é dependência direta do Spot On Radio.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: Audioware-windows-v1.10.1.zip; SHA256 `7b0d42ca4d706780aa9b44530c007c17310adbfb822b056e098391b65662fb93`.
- `r6/scripts/Audioware/Callback.reds` — 542 bytes; SHA256 `35cd46b079acc078a11e882dbe1fe7a59fab30c745847ed6cf6ca55594e401f9`.
- `r6/scripts/Audioware/Codeware.reds` — 3033 bytes; SHA256 `d596e0e944948c9e225d4f5f7446c3b72f53b5b4927bf0c1e60079c121b5d71f`.
- `r6/scripts/Audioware/Config.reds` — 1324 bytes; SHA256 `364a7e721c650c0be6ba2bf806a1cddf3c08470d51f04234ddd548db2edca5ab`.
- `r6/scripts/Audioware/Ext.reds` — 7148 bytes; SHA256 `fb7c049af06a6d95e772aae45526cd9edd6871c3961a479792cb7755cec2df9c`.
- `r6/scripts/Audioware/Hooks.reds` — 1905 bytes; SHA256 `749713f1abc5be4656d835224f93e39bd023ab08d4163cdc41e73514626d6c05`.
- `r6/scripts/Audioware/Listener.reds` — 4959 bytes; SHA256 `03f914fa44199c8dbe34f9d679f999c0167419dfb34928ee4aaa4b26949810c7`.
- `r6/scripts/Audioware/Manager.reds` — 3366 bytes; SHA256 `5082003d06e570c5eb512826d455211b9b089f9d2e52c3ef9fd27daec65c3f90`.
- `r6/scripts/Audioware/Natives.reds` — 1679 bytes; SHA256 `3cb00d99b59e060eecd19f7807695c6024ac6f9029d372230d27d5ff5a36edc0`.
- `r6/scripts/Audioware/PreGame.reds` — 723 bytes; SHA256 `a9bb7170d0c86eaebf81d48eefdf290e820b78712957b6abf165098a0117ef3a`.
- `r6/scripts/Audioware/Preset.reds` — 649 bytes; SHA256 `28c8ac2189cadf2e3f9a70d8bc5301e43cd44500370f00f43b1e6b6b7699d34a`.
- `r6/scripts/Audioware/Service.reds` — 2326 bytes; SHA256 `81deba2666122b1c721504dba0f4aeed315a66e67f21e3b22450e51b11cbcb9c`.
- `r6/scripts/Audioware/Settings.reds` — 978 bytes; SHA256 `4e030b858947d47d76585db9caf546e950bcfea5c819c1c8756b60e6063a492c`.
- `r6/scripts/Audioware/System.reds` — 8079 bytes; SHA256 `332ace9d477598f6363cbc3e9d736ea0f12917802564c0748b3495087b7c300c`.
- `r6/scripts/Audioware/Trigger.reds` — 474 bytes; SHA256 `134fd91d992a1d2803ab22854c418f5b9c06e2d56dacc57645b13a18d59f9ef6`.
- `r6/scripts/Audioware/Tween.reds` — 1499 bytes; SHA256 `1d85a84884d72b1e6412e35a65ccf5444f60b585e643fb7a51449e4b93aead43`.
- `r6/scripts/Audioware/Utils.reds` — 2318 bytes; SHA256 `75a088e0fd80e84b02adbdd5cebce187765830dabeb5d33c776bbac067d3b710`.
- `r6/scripts/Audioware/Event/Callback.reds` — 8178 bytes; SHA256 `135e461dcf7abd880d6077cb769d9fd258abc3bb50f3cba8e2ed64e92358cef3`.
- `r6/scripts/Audioware/Event/Dynamic.reds` — 2347 bytes; SHA256 `f1ea8d1a929cc04148bdf3960ad88853981db2f5d43fc29fafda83cc66a0a93b`.
- `red4ext/plugins/audioware/audioware.dll` — 3763712 bytes; SHA256 `2be1681538acc013820c16f07deeedf2f16d477e8ecc92889a317a9a7278f7e2`.

## Fontes

- https://github.com/cyb3rpsych0s1s/audioware

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.