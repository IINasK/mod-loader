# Immersive Third Person

- ID: `19-ThirdPerson`
- Versao fixa: 2.0.1; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Câmera em terceira pessoa com animações próprias e troca contextual entreFPP/TPP.

## Como usar

- Após instalar e carregar save, aguardar a mensagem inicial. ConfigurarSwitch perspective emConfigurações>Mods>Immersive Third Person>Keys ou na janelaCET.

## Dependencias

- [CET](CET.md) — v1.38.1 (`CET`).
- [Native Settings UI](NativeSettings.md) — commit 4065b51 (`NativeSettings`).
- [ArchiveXL](ArchiveXL.md) — v1.28.2 (`ArchiveXL`).
- [TweakXL](TweakXL.md) — v1.12.0 (`TweakXL`).
- [Codeware](Codeware.md) — v1.21.1 (`Codeware`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).

Requisitos autorais registrados: PhantomLiberty,CET,RED4ext,redscript,ArchiveXL,TweakXL,Codeware;NativeSettingsUI opcional.

## Phantom Liberty e compatibilidade

Descrição oficial lista expressamente Cyberpunk2077+PhantomLibertyDLC nos requisitos. Não há autorização para remover a exigência ou substituir a câmera por outra versão.

Nivel da evidencia: Explicit author requirements. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Primeiro carregamento do save pode falhar segundo autor; recomenda nova partida e informa possíveis conflitos com outros mods.
- Primeira e terceira pessoa usam animações separadas.
- SemDLC não oferecer este item como compatível só porque scripts compilam.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: Immersive Third Person - Best Of Both Worlds V2 32203 2.0.1 2026-10-26T07-30Z PYMIYqqOA.zip; SHA256 `3dd8b87f679b196f3ca7e7668804a442126dcfb7d51068c711e175b7a30bd143`.
- `archive/pc/mod/immersive_third_person_v_lipsync_en.archive` — 320413696 bytes; SHA256 `ad63f349beffead854c7a49c6d0e6336388f11ece26de02efe61f7dd077007b2`.
- `archive/pc/mod/immersive_third_person.archive` — 940470272 bytes; SHA256 `5e437864e22e74255b28d4f9236d9ca909c24809c340a2dcbd1048480c8a789b`.
- `archive/pc/mod/immersive_third_person.archive.xl` — 165 bytes; SHA256 `1774d6aa316a0e87a619358519ae40c23cb468bc0e3ce3a6158f16798d34793c`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/immersive_third_person/api.lua` — 97665 bytes; SHA256 `dd68e4f57422347f73635075b8ece2b3697da346adaff6e64dbf3f5d47ced4ad`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/immersive_third_person/camera_motion.lua` — 13075 bytes; SHA256 `dd63e2ab325309310e8deb6260a362dd270e2de4afd218b1bd7c2de9cb48f711`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/immersive_third_person/init.lua` — 567892 bytes; SHA256 `2a47855f977de1168b67fa9dec47bf96f2881c722963260d3f4a3750d61b67e5`.
- `r6/scripts/immersive_third_person/tpp_camera_api.reds` — 143155 bytes; SHA256 `64e627d47912b21027d23ba3671d2f9bd021caed63e367786c6c0d5ed634a4cc`.
- `r6/scripts/immersive_third_person/tpp_camera_bridge.reds` — 95942 bytes; SHA256 `e52d2ead99363dcce7f34e0d80b998a9334d00f69d295e5990d51cfb36142d55`.
- `r6/scripts/immersive_third_person/tpp_camera_dash.reds` — 12492 bytes; SHA256 `b83423c775b16947d296a1548c42c69ed520be9d46e3a522137eb5dd4c324a13`.
- `r6/scripts/immersive_third_person/tpp_camera_ground_level.reds` — 5761 bytes; SHA256 `68eb859bb749f5affe4bddd0b171dcfdcc6f25e727119308e1c6709c6355b367`.
- `r6/scripts/immersive_third_person/tpp_camera_headlook.reds` — 37405 bytes; SHA256 `8a4087b01d015d2ef2a6755204f747c2caefea942e15491abad90f86298a81d7`.
- `r6/scripts/immersive_third_person/tpp_camera_holocall_eyes.reds` — 1663 bytes; SHA256 `b131f16e9ead70d985cdf9be9e1458a3ba355d6208deec6ff67cc47d4a7db320`.
- `r6/scripts/immersive_third_person/tpp_camera_install_notice.reds` — 8011 bytes; SHA256 `e8659ec919efcf29396b5245ea7348b0a6222f9d2e7e13f5971fced7d4613fc5`.
- `r6/scripts/immersive_third_person/tpp_camera_item_plane.reds` — 4181 bytes; SHA256 `041ec8af9365ea2cba6058919b9b094c1501bd29808fb07820b6117490623794`.
- `r6/scripts/immersive_third_person/tpp_camera_male_climb.reds` — 8782 bytes; SHA256 `77f09ab0b026a06edbbcdd8ba92f382ffdbb6138d9a27be6064644e67ddfd3e6`.
- `r6/scripts/immersive_third_person/tpp_camera_male_dash.reds` — 5291 bytes; SHA256 `67a3117a7c52658713120b29bcae06fb9c94c89d30092f8be67d3a4b5daf0c2a`.
- `r6/scripts/immersive_third_person/tpp_camera_male_knockdown.reds` — 91914 bytes; SHA256 `578d69fe67e7d3f0a3874468678bd37b4d7ec0eeefb8dc7e178a31ea3ea726d3`.
- `r6/scripts/immersive_third_person/tpp_camera_male_landing.reds` — 888 bytes; SHA256 `0c2fa6e367fa53999ae43ce1d46d0e76189ac10cc03527762adad736859ccf2e`.
- `r6/scripts/immersive_third_person/tpp_camera_move360.reds` — 54102 bytes; SHA256 `a00ec681c2785610b8087c240e1101d1ec1772863e4e4b3db2e5ad9212ca8cd8`.
- `r6/scripts/immersive_third_person/tpp_camera_notify.reds` — 11934 bytes; SHA256 `4a94681ddf43fa2f782a35c66b5c046a7ef8a9110651710c7e07f6fcc4f85be1`.
- `r6/scripts/immersive_third_person/tpp_camera_settings.reds` — 17578 bytes; SHA256 `666deb592856a4c2901600af30f2d3e21ad482d8ef53144b8d338916826f4b06`.
- `r6/scripts/immersive_third_person/tpp_camera_sprint_stop.reds` — 29614 bytes; SHA256 `4eade21c3651c82bb0b91a486a72c6d14d06fdff1b7320786036ab8440b2da7e`.
- `r6/tweaks/immersive_third_person/api_level.yaml` — 58 bytes; SHA256 `62dedc52f9aaaa93235441fbfed9a1392d5df69adde2b6e7c1eba1b3c4d9d838`.
- `r6/tweaks/immersive_third_person/consumable_timing.yaml` — 0 bytes; SHA256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- `r6/tweaks/immersive_third_person/install_notice.yaml` — 166 bytes; SHA256 `e6a145e5308e0e6b90931f96ada43517ca4449ca4c6c0834cf6f4d0e93a2fc2e`.
- `r6/tweaks/immersive_third_person/tpp_head.yaml` — 137 bytes; SHA256 `8416b31506f8ba3f357ba401cd28c085741c724dccfbc9332823e701eb9fa796`.
- `red4ext/plugins/ImmersiveThirdPerson/ImmersiveThirdPerson.dll` — 240128 bytes; SHA256 `cbd47d32eb9cb5656921b4243737ae47105f0c717e741951f492f9a8cc4b6864`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/32203

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.