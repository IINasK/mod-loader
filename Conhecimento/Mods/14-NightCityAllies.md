# Night City Allies

- ID: `14-NightCityAllies`
- Versao fixa: 1.6.11; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: unverified.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Companheiros e mercenários recrutáveis, combate, progressão, contratos, veículos e convivência em apartamentos.

## Como usar

- Recrutar mercenários com ícone amarelo, por exemplo noAfterlife; personagens conhecidos mandam mensagens após progresso na história.
- Gerenciar no siteNight City Allies em um computador do jogo;F interage eR abre equipamento, conforme atalhos configurados.

## Dependencias

- [CET](CET.md) — v1.38.1 (`CET`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).
- [ArchiveXL](ArchiveXL.md) — v1.28.2 (`ArchiveXL`).
- [TweakXL](TweakXL.md) — v1.12.0 (`TweakXL`).
- [Codeware](Codeware.md) — v1.21.1 (`Codeware`).
- [InputLoader](InputLoader.md) — v0.2.6 (`InputLoader`).
- [ModSettings](ModSettings.md) — v0.2.27 (`ModSettings`).
- [Audioware](Audioware.md) — v1.10.1 (`Audioware`).
- [Utilities2077](Utilities2077.md) — 0.7.0 (`Utilities2077`).
- [Native Settings UI](NativeSettings.md) — commit 4065b51 (`NativeSettings`).

Requisitos autorais registrados: FrameworksCET/redscript/XL/Codeware e demais dependências do catálogo;AIO inclui módulos opcionais.

## Phantom Liberty e compatibilidade

Autor diz que oDLC não deveria ser necessário, mas a receitaAIO contém registro ativoCharacter.wst_ep1_09_ambush_leader. Metadata.ReadRecord trata registro ausente, porém não foi comprovado que toda a cadeia de spawn/personagens opcionais evita assetsEP1 no pacote semDLC. Não converter essa expectativa em garantia.

Nivel da evidencia: Unconfirmed exact AIO without expansion. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Um companheiro doDLC pode não existir sem assetsPhantomLiberty mesmo que o núcleo do mod funcione.
- Personagens/quest desbloqueados variam com o progresso do save.
- Statusunknown é só desta auditoria semDLC, não revoga relato anterior de coop comDLC.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: Night City Allies - AIO 27625 1.6.11 2026-10-25T17-01Z IieOiggb1.zip; SHA256 `9d3b895c255b68a338c291ae21c9ebaf31fd793d1313964b2a8824057429ff0e`.
- `archive/pc/mod/Night City Allies - Immersive Companion System.archive` — 2670592 bytes; SHA256 `d6c3e299942987f505828ca3689e8ba356b02f1018ebe7931261edd6ae39934d`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/InkGUI/init.lua` — 15179 bytes; SHA256 `5e6955d071f193761900175bae03edf72aabec7f03bccccc7a36363a6cc3c23e`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/help.txt` — 541 bytes; SHA256 `e637f566edd63e31812f820f1b3641ca2eba8036d301944373b5f007f3ebc95a`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/init.lua` — 30432 bytes; SHA256 `2402539d4df37e7a5ca891f22fa7546b733c2fbb697ceda28c2fb48f99051067`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/Application/app.lua` — 2015 bytes; SHA256 `36f5a0a76fbc9d32ac66cc15d473b38d8747c0d352e1a010b2bf3980ee3bccf6`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/Application/IO/characterLoader.lua` — 2979 bytes; SHA256 `7d02cabdf5c7a5932b2f455560d14c49e988fc12062c38a3adeac7f5addf2b05`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/Application/IO/moduleLoader.lua` — 1466 bytes; SHA256 `db471c911894e8f39cbf3281f49ed5f752094c2068a1b1cd115d77bc19a8ad71`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/Application/Lib/debug.lua` — 3325 bytes; SHA256 `229ee27a42da8cd8cb7167bb8a811b4e16a8640c51066dbfd99a51948bd1aa1c`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/Characters/README.txt` — 43 bytes; SHA256 `505c3cf0023fbf22f86bc4a62d257c50f9b236f51da67a9eb51372fc7da2a3b7`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/Interactions/greet_example.lua.txt` — 1243 bytes; SHA256 `98bea0ae2862cb48b0746d2db7357a3774d344405cbd66d8a39b62cb11dcf8da`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/NightCityAllies/Modules/README.txt` — 0 bytes; SHA256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- `r6/scripts/InkGUI/BrowserExtension.reds` — 3567 bytes; SHA256 `0312bc501a188747091bbef08c60dbacff770236fc7acb322ece5434145c9ec1`.
- `r6/scripts/InkGUI/InkGUI.reds` — 11419 bytes; SHA256 `d282ce8f5d493119dbb73e5175e6bdc28d0f6dcb48eda7054f83f65077d34235`.
- `r6/scripts/InkGUI/Lua.reds` — 11096 bytes; SHA256 `46513d01e744de3ee4f2b6f77c31a5f7145d5f9994fdcec69333444ffd70ae4f`.
- `r6/scripts/InkGUI/Notification.reds` — 12381 bytes; SHA256 `01dc5b7a5fe0ee750acf8870e8914552f10f69cb70dc98626520323d2d21009c`.
- `r6/scripts/InkGUI/Panel.reds` — 64891 bytes; SHA256 `9e3fedd9694e14e053a8f539b0faf5b60019c1019249bbfb18f1cca1948e4b74`.
- `r6/scripts/InkGUI/StatRow.reds` — 3911 bytes; SHA256 `18916d2a4d8e1c8e2ab6aa8017812e1941fee50de64786531d0ab0b2dda5cc91`.
- `r6/scripts/InkGUI/Website.reds` — 10919 bytes; SHA256 `ae4774a929b80dbc4f9a4396178fcb60228f72a5550db5e3436a467d7b82c1b3`.
- `r6/scripts/NightCityAllies/LICENSE.md` — 1306 bytes; SHA256 `82ef0def11f36b9cb4c059ee134f0e74c88528a19428fe15d44044dcec130bb6`.
- `r6/scripts/NightCityAllies/NCA.reds` — 5753 bytes; SHA256 `759b51c06a7e1125d4f14b55a034d28b9e96e68f099471a1593a7a0142b70e26`.
- `r6/scripts/NightCityAllies/NCAConstants.reds` — 15938 bytes; SHA256 `f3949cd936d335a7b8f6a18b83339f31d8ee389b1828ecbcd9c26ebedda007cb`.
- `r6/scripts/NightCityAllies/Animation/AnimationHandle.reds` — 9952 bytes; SHA256 `9fc9cd72469872a5f2f0edc2fe056daf8febfbe49ff23b9ec38f38339d574a22`.
- `r6/scripts/NightCityAllies/Animation/AnimationSet.reds` — 4093 bytes; SHA256 `5db2cee492594ff2359961079c294e53d29290b9b27c6bf56c0c29e79d809915`.
- `r6/scripts/NightCityAllies/Animation/AnimationSystem.reds` — 13120 bytes; SHA256 `d89ce8bd5f74e47623a67fb6f309ded8e39592dd140b2ca8cd1193cb95899493`.
- `r6/scripts/NightCityAllies/Animation/Routine.reds` — 12121 bytes; SHA256 `11294b7b1e5bfe026fa51b2df1ed779cb2b6c52271720411f60953e868e560a2`.
- `r6/scripts/NightCityAllies/Contract/ContractSystem.reds` — 20571 bytes; SHA256 `b5fb9cfc77d05095ef107964e6b18c7f167558dbf0964e9a99469d0d520752ed`.
- `r6/scripts/NightCityAllies/Contract/ContractTemplate.reds` — 1894 bytes; SHA256 `c5374b071e6f64e292325be0a2c0cd4366b16c270bb84fa5847f7a89bc0374c0`.
- `r6/scripts/NightCityAllies/Effect/EffectSystem.reds` — 8438 bytes; SHA256 `24b9a5e4b7270bf0a91ae0c85236305d52d5c7478b6439f1eea25864e118624c`.
- `r6/scripts/NightCityAllies/Equipment/EquipmentRecords.reds` — 3466 bytes; SHA256 `3dd1a8b3183d621219f65748f42c93bbcdaef9a3de0406897e4e31e7879817f6`.
- `r6/scripts/NightCityAllies/Equipment/EquipmentSystem.reds` — 33337 bytes; SHA256 `dde0949b4a3ff5b330bbc7694953ce9108a1edc65df7fa52cd52d93db70d4f30`.
- `r6/scripts/NightCityAllies/Event/EventBus.reds` — 13800 bytes; SHA256 `08f7519222bdd65635e6aa27de3432f8cf2ac1d03b6b133d34ffc9878966baa0`.
- `r6/scripts/NightCityAllies/Factor/FactorSystem.reds` — 5394 bytes; SHA256 `cb1859865a4257b6b0d06ecc9144e090c76fe9235810c3ffdf408b40ec73f07c`.
- `r6/scripts/NightCityAllies/Localization/NCALabels.reds` — 10666 bytes; SHA256 `0586961cf2224328b18e48a5a27bf9d223fa227aa1e24f2aa31b7c516faaae5d`.
- `r6/scripts/NightCityAllies/Location/LocationSystem.reds` — 27148 bytes; SHA256 `747d962a9d7b7b2754ee2d9d4d25f1d5f3c2176965224e2358562efaa071918e`.
- `r6/scripts/NightCityAllies/Location/Entity/Path.reds` — 1320 bytes; SHA256 `906cfed6967b0ac72fa8978618e8700fefca92ed9127403cc238ea4f4c46763e`.
- `r6/scripts/NightCityAllies/Location/Entity/Prop.reds` — 5633 bytes; SHA256 `166f1579760705ebf5da38a1cb3a7244c4f32b313ba9c5883f204aa62131b3b4`.
- `r6/scripts/NightCityAllies/Location/Entity/Spawn.reds` — 609 bytes; SHA256 `8a0c5165f3fc0f4010100461f917d810acc450cc9a5f419cc42bdeb967f859e4`.
- `r6/scripts/NightCityAllies/Location/Trigger/LocationTrigger.reds` — 3195 bytes; SHA256 `99f49d0d75e3de8edb9f95dc514a9adb3c50eaa3a000bca7bb25e035d3716304`.
- `r6/scripts/NightCityAllies/Metadata/CompanionMetadata.reds` — 4090 bytes; SHA256 `e1a34ac816e51ad3e887643c704341ea0b6857e88aae5922effa60b1491c29d7`.
- `r6/scripts/NightCityAllies/Metadata/NCAMetadataSystem.reds` — 2942 bytes; SHA256 `f7126bb7ba5f329c07c6b1cb213830a003217b9436ebb25c726d025e9d639fff`.
- `r6/scripts/NightCityAllies/Migration/Migrations.reds` — 6313 bytes; SHA256 `7d4b3ab7a1b436398df979bc43835c04d5f93d7aded15e57ea9760bf33501905`.
- `r6/scripts/NightCityAllies/Modules/NCAModule.reds` — 2959 bytes; SHA256 `f0184f970e5b40bd8e7bfc88bd3dda341a24e20410294e3a463009dd3fe5daa5`.
- `r6/scripts/NightCityAllies/Modules/NCAModuleSystem.reds` — 9775 bytes; SHA256 `ec00ea8f818869162aeecaf400d2ec76bdd2232e6ceceff5a595b843a2f9bd08`.
- `r6/scripts/NightCityAllies/Modules/NCARegistration.reds` — 25192 bytes; SHA256 `fc8e1497780ac98074bb02acea6112cc3c767066ffc1a9d4fb87cfffc5d26355`.
- `r6/scripts/NightCityAllies/Npc/DamageTracker.reds` — 3286 bytes; SHA256 `811ff5e2549e35de556874a69ec91392341e788242be46a273ecaf34953ede49`.
- `r6/scripts/NightCityAllies/Npc/EntityResolver.reds` — 4027 bytes; SHA256 `a46bdee351f0417a62538fb2215667cad1b7ddafebd9efe15c6348cb88005472`.
- `r6/scripts/NightCityAllies/Npc/ExpLogic.reds` — 1440 bytes; SHA256 `dc545428cb3d715a24102083010327ad1e0066ae0852c93d5ca632035ed44184`.
- `r6/scripts/NightCityAllies/Npc/NpcHandle.reds` — 48230 bytes; SHA256 `9f234fbe688d6074c8744f9336e80441a368cb4c792899a719318ed4db80a0f2`.
- `r6/scripts/NightCityAllies/Npc/NpcManager.reds` — 28677 bytes; SHA256 `2d005b2069bdf221640f51f9238f604b519ccec99cebbf26898fc41fd4995426`.
- `r6/scripts/NightCityAllies/Npc/Behavior/AcquirableBehavior.reds` — 1075 bytes; SHA256 `8c8ebe2ace6d12fb44bcda0faf488134562027d9e13bed849d6dd8da583e091a`.
- `r6/scripts/NightCityAllies/Npc/Behavior/AcquirableRoamBehavior.reds` — 728 bytes; SHA256 `b43f6b1dbef7d65bd14cf40c52c3b1d5ea3e5786948ee02050443b7dc71799d4`.
- `r6/scripts/NightCityAllies/Npc/Behavior/Approach.reds` — 2441 bytes; SHA256 `06ad7d2aecfe47982d42d4910f5c5e7b640c913ffeb5519b90195ca86ef6fbee`.
- `r6/scripts/NightCityAllies/Npc/Behavior/CatchUpToPlayerBehavior.reds` — 2889 bytes; SHA256 `1423e7f0b7581ea6c4681852a9a18cea5952287f3cb7c07b8502559a961ebae5`.
- `r6/scripts/NightCityAllies/Npc/Behavior/ChauffeurBehavior.reds` — 6843 bytes; SHA256 `e588ba2e52d6282d5ad63e0379aeeffd987738c105d829dc692c705d4c5dca35`.
- `r6/scripts/NightCityAllies/Npc/Behavior/CombatBehavior.reds` — 1760 bytes; SHA256 `0e6e69cea6947a7205e7a1a5cfb51dffe820d3b7cf03d3a5fc69f07153cf58df`.
- `r6/scripts/NightCityAllies/Npc/Behavior/DriverBehavior.reds` — 7364 bytes; SHA256 `60fcc73912daa36ee6c1cd8a55d8a4a2af6419a7c89c21dd661897aaaf3d7753`.
- `r6/scripts/NightCityAllies/Npc/Behavior/ElevatorBehavior.reds` — 1854 bytes; SHA256 `bcdba35d350388d396a4c15644905d4880b4c58268b399dc43e0d62b67e5a1b6`.
- `r6/scripts/NightCityAllies/Npc/Behavior/ExploreLocationBehavior.reds` — 20681 bytes; SHA256 `08bd210c45da65252f4bca83d22ca39ff47cd7ecb7b49b25549c70c440ba51ad`.
- `r6/scripts/NightCityAllies/Npc/Behavior/FollowPlayerBehavior.reds` — 2208 bytes; SHA256 `5eae0147816069774995e45a20f4d6b05bfa05b5415b245247d910050537c48a`.
- `r6/scripts/NightCityAllies/Npc/Behavior/FollowTargetBehavior.reds` — 1250 bytes; SHA256 `e7254d74dab214dc6d20a1e8c407db6cc20420d3b4e19057e6bc08bc9f445aac`.
- `r6/scripts/NightCityAllies/Npc/Behavior/HoldPositionBehavior.reds` — 845 bytes; SHA256 `6bde1a8a4744091ccd06810ffffb8ed7d55ddb2430ea3a95eab71e8eb434d5f7`.
- `r6/scripts/NightCityAllies/Npc/Behavior/MoveToPositionBehavior.reds` — 1513 bytes; SHA256 `c0a280db50b01592cd9371ac0a0aad7a64a83f0cbe099f4380b4a20d8849ffcc`.
- `r6/scripts/NightCityAllies/Npc/Behavior/NCABehavior.reds` — 28735 bytes; SHA256 `11031b89828b473bdbf5b8cd645a5a4d7f597587ba42bee7f070e36c6ade992c`.
- `r6/scripts/NightCityAllies/Npc/Behavior/PassengerBehavior.reds` — 2455 bytes; SHA256 `7af76b7eed713f3098f0b096105aee7089b1e00de36cdfe2a7470c9a6ea031c5`.
- `r6/scripts/NightCityAllies/Npc/Behavior/PreviewBehavior.reds` — 4276 bytes; SHA256 `8e631d120165ffaaaf05bf0078bd58594a7be615cd159dc190a9d1c1b43058cc`.
- `r6/scripts/NightCityAllies/Npc/Behavior/RoamBehavior.reds` — 5332 bytes; SHA256 `1dedb493fd43be515f46e4977cf4427c5774da2f54a740b2d0730382633613c1`.
- `r6/scripts/NightCityAllies/Npc/Behavior/WalkAheadBehavior.reds` — 5792 bytes; SHA256 `c1a89475f2781afcf794656668aee49a29c4822449feec569e30b0984a0ab408`.
- `r6/scripts/NightCityAllies/Npc/Hooks/ExpHooks.reds` — 548 bytes; SHA256 `4a1001ed30edcab8b59ba3aee3fd48b27012e05c83a96b423b9c733aa795ee9a`.
- `r6/scripts/NightCityAllies/Npc/Hooks/NpcHooks.reds` — 4055 bytes; SHA256 `8cbdcddc67f811d08f9a4ac1e923dd7ffd538182248e9c19bd825d64f0411b07`.
- `r6/scripts/NightCityAllies/Npc/Hooks/ProximityHooks.reds` — 66 bytes; SHA256 `bca691035b9cb345bb5e1a9d468193a4af5deac290d6e079c50e3b5181db745e`.
- `r6/scripts/NightCityAllies/Persistence/ContextSystem.reds` — 960 bytes; SHA256 `43304ef421d2e859af9e31979a7966fe1d511536caa0e0575cfd5323eb894aea`.
- `r6/scripts/NightCityAllies/Persistence/PersistenceSystem.reds` — 72415 bytes; SHA256 `c938f52807b6c870fd33d97ea7da9da9eadb7c8d660da924f3433827b209a326`.
- `r6/scripts/NightCityAllies/Persistence/Hooks/BlackboardListeners.reds` — 7441 bytes; SHA256 `3da3c335caf84d0c09c2120e50e0093b3166ec99d35954134f58987162815fda`.
- `r6/scripts/NightCityAllies/Persistence/Hooks/ContextHooks.reds` — 6660 bytes; SHA256 `3a0f410f75df66c28d1957a7753cb1dbd6dac13d770624e7e58b9461fce8443e`.
- `r6/scripts/NightCityAllies/Persistence/Hooks/TimeListeners.reds` — 4566 bytes; SHA256 `d981c212c2671a9b9e47b72fc9a218e8d34a54f002160a80fb28c26f8b257a4e`.
- `r6/scripts/NightCityAllies/Phone/ConversationBuilder.reds` — 1297 bytes; SHA256 `558f7a82b98717801224454bebb641fca26bc9baf81255a213ee8928355e6079`.
- `r6/scripts/NightCityAllies/Phone/NCAPhoneSystem.reds` — 24688 bytes; SHA256 `7d3d94b3cf6a7302288e161ae07bef9fcc22ff1e2d358097c5f88d1c2d12209f`.
- `r6/scripts/NightCityAllies/Phone/Hooks/NativeHooks.reds` — 10778 bytes; SHA256 `cd318b4f2b084cae5c23b1a37c0e25bea59c8e87eee4238dd52168c29f9cfcd4`.
- `r6/scripts/NightCityAllies/Phone/View/ConversationPhoneView.reds` — 6314 bytes; SHA256 `79417225f85cdb38128958dc5330b2ec42e0c0965d7d11d796e989483aa73dbe`.
- `r6/scripts/NightCityAllies/Phone/View/CustomConversationView.reds` — 3786 bytes; SHA256 `38b1bbd39867971193b62e12a76ffcb245b9abad4ec81e1eb367964416debae1`.
- `r6/scripts/NightCityAllies/Phone/View/MercListPhoneView.reds` — 6054 bytes; SHA256 `644613e82b2b3bcc665a99954285c52d2df19744d49eb613d9d474cca832e910`.
- `r6/scripts/NightCityAllies/Phone/View/NoMercsPhoneView.reds` — 2216 bytes; SHA256 `cea4e1ade837a44e32d42d7f7230b44a94a6c86a5013981b066f31bec40a19c7`.
- `r6/scripts/NightCityAllies/Phone/View/PhoneView.reds` — 1936 bytes; SHA256 `4f4daadfde24c0b5e0f181d98ac55ab78a94eb53dc3ea4544afa6c619d7b099b`.
- `r6/scripts/NightCityAllies/Settings/NCASettings.reds` — 17914 bytes; SHA256 `5c707c1704586be647a5395eefbbb1f53792a485c7e4bc66773a58002466f953`.
- `r6/scripts/NightCityAllies/Spawn/SpawnSystem.reds` — 11423 bytes; SHA256 `a891dc74da06461ca1c86d5478ead94a491b59ddfddbd5b41a50d10700f1db42`.
- `r6/scripts/NightCityAllies/Timer/NCATimerSystem.reds` — 2861 bytes; SHA256 `7fe960b6ca372589eb5f7882eb665bb8f5f159a392e9cc787182eabc84dea0fc`.
- `r6/scripts/NightCityAllies/UI/CompanionStatsView.reds` — 8037 bytes; SHA256 `757e839b957c3c84993590381023dd52c895a10f074715574fef75684624bd6c`.
- `r6/scripts/NightCityAllies/UI/DealerWebsite.reds` — 6397 bytes; SHA256 `b620d95fafb7889592cf413dfcce92d65401f58252e6652ed16a0213686d39d2`.
- `r6/scripts/NightCityAllies/UI/DebugLog.reds` — 2562 bytes; SHA256 `ebf0380010e54ec7a59b4602bfa340028632b5bde49383235771c0890bc2db0e`.
- `r6/scripts/NightCityAllies/UI/EquipmentAction.reds` — 4456 bytes; SHA256 `e04ab56badb87d4b7408994ce4bef629a3aad6c2a8ef02725ae60e0abfa0c84e`.
- `r6/scripts/NightCityAllies/UI/EquipmentActionsController.reds` — 6645 bytes; SHA256 `de09140d7b618ed607dc7295c2c383adb38b097cfad2c1bad9f877822318daec`.
- `r6/scripts/NightCityAllies/UI/EquipmentPanelController.reds` — 35674 bytes; SHA256 `f6d44b6f5486d22d88b745b5dd8cfd5c15903ab44db531ecd42cec626350689e`.
- `r6/scripts/NightCityAllies/UI/HealthBarVisibility.reds` — 2878 bytes; SHA256 `ef9ee669e95fba1831448d1b3789df828ac1bde8e8ecd82c3d4a021775abfd43`.
- `r6/scripts/NightCityAllies/UI/InteractionMenu.reds` — 35793 bytes; SHA256 `782d3db44c60828c4afd55c812d266e019ac8f365a0013a66df9abe1abd311fc`.
- `r6/scripts/NightCityAllies/UI/OutfitsPanel.reds` — 16523 bytes; SHA256 `5febae8095820c5e80cb8b779fb98dff65cd929803eed5b74fef0aec6a76a003`.
- `r6/scripts/NightCityAllies/UI/SettingsDropdownRow.reds` — 2524 bytes; SHA256 `842789d0ce6479b0f019c75a166bbb1e8a31e4a63cdfa4bdb14fd1a78ac6e1ba`.
- `r6/scripts/NightCityAllies/UI/SquadContainerController.reds` — 6072 bytes; SHA256 `721e70b80933c8f51c6a45294da993c1de09711d1000d4e128884855e9b9e49a`.
- `r6/scripts/NightCityAllies/UI/SquadMemberController.reds` — 8964 bytes; SHA256 `a52a1eaf5aa4f2fc66908c828a41ea453f9a60f1804abf3c524cad4d1b3ca8da`.
- `r6/scripts/NightCityAllies/UI/UISystem.reds` — 8927 bytes; SHA256 `633d2c089c23182d28fe111078da2a40ee705c030d3b14c5c106f7a594f11d83`.
- `r6/scripts/NightCityAllies/UI/WeaponActionsPanel.reds` — 3175 bytes; SHA256 `2035e4af0ded2b9b30eb7c48b5a8b1d35259c8cf031f9c1e9ae1524ce2f7a3cd`.
- `r6/scripts/NightCityAllies/UI/Website.reds` — 59368 bytes; SHA256 `934980b66b43614566da075c4e69a160ef23097422de94c59e6295765ccc7767`.
- `r6/scripts/NightCityAllies/UI/Hooks/InteractionMenuHooks.reds` — 2953 bytes; SHA256 `4f9225171014704e957c77d0c72c8629446ccd93bb842521f13db9ad9fe93143`.
- `r6/scripts/NightCityAllies/UI/Hooks/ItemDisplayHooks.reds` — 720 bytes; SHA256 `aca683ff8b74d1a823b43bdf44c7bba4edc5fa408a7ad67f66ecbdac508270a8`.
- `r6/scripts/NightCityAllies/UI/Hooks/LookAtListeners.reds` — 1917 bytes; SHA256 `b35fa12263cdc17b09fd18b863a40f145cf1ff64f254606c17e4e48383a5e831`.
- `r6/scripts/NightCityAllies/UI/Hooks/NotificationListeners.reds` — 437 bytes; SHA256 `d82d76e0ccf55a708a83b074416e6b66b376fa87254110dad724234188bed792`.
- `r6/scripts/NightCityAllies/UI/Interactions/EquipmentPanelEntry.reds` — 2006 bytes; SHA256 `524ea9285d841757fb3918bfa533ee52d69cb78263f508227444ceb2407e5921`.
- `r6/scripts/NightCityAllies/UI/Interactions/FollowEntry.reds` — 719 bytes; SHA256 `9594d2e954d14f741eb0ea6cb5dfb076bb10b77df0c322895d34b674fd46499d`.
- `r6/scripts/NightCityAllies/UI/Interactions/GreetEntry.reds` — 909 bytes; SHA256 `feaa5df516341948413d7a1fea9caa3f5bf1b7275fdd27b1702e73ab31dc4792`.
- `r6/scripts/NightCityAllies/UI/Interactions/HireEntry.reds` — 1382 bytes; SHA256 `7fd87a80b7d1f075abdeea8302c1c3d61a2fe94cd60a6d20aff83b3c0e512d57`.
- `r6/scripts/NightCityAllies/UI/Interactions/HoldPositionEntry.reds` — 788 bytes; SHA256 `5d1026aa44454f8d57f7818c1163164e8613e0eba36b98b0024ebfe622173c64`.
- `r6/scripts/NightCityAllies/UI/Interactions/InteractionEntry.reds` — 820 bytes; SHA256 `68cf35838d045d35b95d577ab192f908359b2bdeeb4e8d0bc763309a7ff053ad`.
- `r6/scripts/NightCityAllies/UI/Interactions/JoinSquadEntry.reds` — 700 bytes; SHA256 `e6d8123980fda90ec17a911cddb59ed65ac909cd597ce14d0663ccf094459837`.
- `r6/scripts/NightCityAllies/UI/Interactions/LuaEntry.reds` — 1221 bytes; SHA256 `5b74a0ce2b6aa23dc8ddf20d31698a53c617e7d6a2eb8456c39c7da49ce7857d`.
- `r6/scripts/NightCityAllies/UI/Interactions/RoutineEntry.reds` — 2795 bytes; SHA256 `de35657535eea9340e3e51b1a597eb6c8d4444f568245887dd93b86bfbb4ebf6`.
- `r6/scripts/NightCityAllies/UI/Interactions/SendAwayEntry.reds` — 871 bytes; SHA256 `780cca53aa12c0ee4914b4325102d0a36ab316952deb16f26a0abd4408b5ca22`.
- `r6/scripts/NightCityAllies/UI/Interactions/StayHereEntry.reds` — 832 bytes; SHA256 `f674295f92077772a7f5c843d9489764fd229921a62e1fa2c1f84aed22a4da0c`.
- `r6/scripts/NightCityAllies/Util/NCAUtil.reds` — 20303 bytes; SHA256 `2b7e447056d8fc2ea25523b0cd4578bf13e59d33eaf6c238aa39ce4390eae6d6`.
- `r6/scripts/NightCityAllies/Vehicle/Vehicle.reds` — 3173 bytes; SHA256 `ca41ceb3724b36b2dd678335759a228f5a231e4509985b5d5f7594ce5045ac94`.
- `r6/scripts/NightCityAllies/Vehicle/VehicleHandle.reds` — 10062 bytes; SHA256 `484c1850f6160d03b627c42d52a8743577f315bbbf544bafecda2b339124ef08`.
- `r6/scripts/NightCityAllies/Vehicle/VehicleSystem.reds` — 16389 bytes; SHA256 `491a5c94ac94aa4c0fa0331c19ae4d1d2a7170ff08d464b91ca5b3438c08a650`.
- `r6/scripts/NightCityAlliesModules/Animations/ArcadeAnimationsModule.reds` — 3620 bytes; SHA256 `9ff19e876e0c2fd81143ff487f6be0bb9c05314dd6f483ed1a926c98ff99b4d5`.
- `r6/scripts/NightCityAlliesModules/Animations/BarkeeperAnimationsModule.reds` — 20414 bytes; SHA256 `143cd2c93419a53f978b88d948b3460d753e96456a2a58c67ede3cf24908e81f`.
- `r6/scripts/NightCityAlliesModules/Animations/BathroomAnimationsModule.reds` — 2916 bytes; SHA256 `f36db30d9c4f5a0f40e07b7510a7b5a69ecaedb5efda7d8a253387cc828ce9c3`.
- `r6/scripts/NightCityAlliesModules/Animations/DanceAnimationsModule.reds` — 11965 bytes; SHA256 `2376d36d53b78dc3927ef8a4ff3a2f617fc0baae1257b3fb0ed361b3de213fd4`.
- `r6/scripts/NightCityAlliesModules/Animations/LyingAnimationsModule.reds` — 6312 bytes; SHA256 `0fb06623195c3a023e7002f4cc1a550629fa76d5b2cb894d06482fa82dc7232b`.
- `r6/scripts/NightCityAlliesModules/Animations/SittingAnimationsModule.reds` — 48566 bytes; SHA256 `86939ccf721f23fda3d58cd9d2f2a2e1b6fb1eae6dec791c6e95a711bbc7ddd9`.
- `r6/scripts/NightCityAlliesModules/Animations/StandingAnimationsModule.reds` — 21050 bytes; SHA256 `418efd07c69d0380ca97c12769ce1c9d383b46ac6c65032a51a3ae183a35128b`.
- `r6/scripts/NightCityAlliesModules/Animations/SwimmingAnimationsModule.reds` — 924 bytes; SHA256 `5a81f5f9d22c6459c39be771568c74c7aad4f81df5d09b313208ea6756da0944`.
- `r6/scripts/NightCityAlliesModules/Animations/SyncedSittingAnimationsModule.reds` — 1967 bytes; SHA256 `1982f69e2a2a4efda6e193aff22e07f60f1eae2fb06e2dfd2610126cb5c15b05`.
- `r6/scripts/NightCityAlliesModules/Animations/SyncedStandingAnimationsModule.reds` — 4027 bytes; SHA256 `2e7e950f40e44de75d29b44e669204b3bf0c17a566a19c80c99f495aa9f77b46`.
- `r6/scripts/NightCityAlliesModules/Characters/GoroModule.reds` — 2996 bytes; SHA256 `a8ff3c07e95098f4b7b2ec7cfaecff99e6c0bfcf10dc84da1b3c0ab9993f3356`.
- `r6/scripts/NightCityAlliesModules/Characters/JackieModule.reds` — 2201 bytes; SHA256 `3c39a36e46bbc797eb1c91df09df6c1ff272b5fb5ec84304c63d236e0ad0387d`.
- `r6/scripts/NightCityAlliesModules/Characters/JudyModule.reds` — 3260 bytes; SHA256 `07b3a8f7d9cd532ac8c8e22b2b80679809ca6dd36c5f031a0db6c7a79a8bba55`.
- `r6/scripts/NightCityAlliesModules/Characters/KerryModule.reds` — 4242 bytes; SHA256 `f5d33c95167d2d0428d833cd47bce4af07fe41248b17e8f88c10593d834dbd00`.
- `r6/scripts/NightCityAlliesModules/Characters/MistyModule.reds` — 2046 bytes; SHA256 `a200b09b3cde3bec980a26390e2e73d3d596f6a3f7805fead14418abf05652a8`.
- `r6/scripts/NightCityAlliesModules/Characters/PanamModule.reds` — 3351 bytes; SHA256 `b2c7f66a9d5b37031d5dadc541fc4dafce7e5920bd10f78256674e62f824fef6`.
- `r6/scripts/NightCityAlliesModules/Characters/RiverModule.reds` — 3261 bytes; SHA256 `7416644ad624df24e0ba6cc80ae47b165c31ab6ce75e12e1b032d26f3b0d408c`.
- `r6/scripts/NightCityAlliesModules/Characters/RogueModule.reds` — 4163 bytes; SHA256 `f9abd7ac694634bc2b9636093ecc5d8352925c6bf3946461c58ace658a9a7ea5`.
- `r6/scripts/NightCityAlliesModules/Core/ContractsModule.reds` — 9315 bytes; SHA256 `7599a83d1c32fadfa3aaf131fafbdb5dd56df4ec7e21f6bd7331f0886bf0b5cf`.
- `r6/scripts/NightCityAlliesModules/Core/EffectsModule.reds` — 2215 bytes; SHA256 `6fec85fc367e5b0de1e95586504cf1b65803b018a8a54947b29be83c57d5dec8`.
- `r6/scripts/NightCityAlliesModules/Core/EquipmentActionsModule.reds` — 2801 bytes; SHA256 `c2df7546ec04adb163408cbae133ec66d361d95649f3fa810edde6964587f167`.
- `r6/scripts/NightCityAlliesModules/Core/FactorsModule.reds` — 1170 bytes; SHA256 `c73d1a8f7c3a68942e2401d94ea67830d0575acf29ac82a94a10203b7dabbd1d`.
- `r6/scripts/NightCityAlliesModules/Core/InteractionMenuModule.reds` — 1115 bytes; SHA256 `073d35fb6c60a0cc58c988e047d761542c22026def2705e826959b0c50ee3c62`.
- `r6/scripts/NightCityAlliesModules/Core/VehicleDealerModule.reds` — 3304 bytes; SHA256 `1380df25f6854f440339dd1aa52573da85e082503b6edf608d6bc744ee283cef`.
- `r6/scripts/NightCityAlliesModules/Core/WebsiteModule.reds` — 402 bytes; SHA256 `78cf619e6c4f361eb5f4159ccccbe7547ac51589f0a2c556c81c857cb6d5809f`.
- `r6/scripts/NightCityAlliesModules/Locations/AfterlifeModule.reds` — 7261 bytes; SHA256 `89b64409bbb9931a6614e0ababac662ed6fdaf5d53d299b4c798254c0b99d1dc`.
- `r6/scripts/NightCityAlliesModules/Locations/CorpoPlazaApartmentModule.reds` — 3702 bytes; SHA256 `cc9df94f19e482ec139710cbc5f1ddd1590b3c881fea92cf85668caa3b89a4f1`.
- `r6/scripts/NightCityAlliesModules/Locations/GlenApartmentModule.reds` — 4565 bytes; SHA256 `f06a87d5a0412ea8929bf45e602fe23084886f9fcf3c89a43b1865ca7c25754d`.
- `r6/scripts/NightCityAlliesModules/Locations/GlenModule.reds` — 871 bytes; SHA256 `344b87cc0ccb6275fca52b068dcc1f15856bd6b6448448c916e35f0fbf570a84`.
- `r6/scripts/NightCityAlliesModules/Locations/H10ApartmentModule.reds` — 3296 bytes; SHA256 `a91ee9f3ecab51b37127778d5aa8381c487d93dc555dd6255ae58e403b1b98d0`.
- `r6/scripts/NightCityAlliesModules/Locations/JapanTownApartmentModule.reds` — 3275 bytes; SHA256 `8d85013a64af2d1bacecd0b5e52526c9fe059e93aa44e1a85fa84ef530cd9b0e`.
- `r6/scripts/NightCityAlliesModules/Locations/KabukiModule.reds` — 978 bytes; SHA256 `efad3f3343f21c38509e55e5bf6c41b84f55f597dd148773a227c81199d7f1fb`.
- `r6/scripts/NightCityAlliesModules/Locations/LittleChinaModule.reds` — 949 bytes; SHA256 `66fb7a9c26cc9db8704c65ebb75b82e0ab9e91eb72c6d46e2a04e3f42e9c17f5`.
- `r6/scripts/NightCityAlliesModules/Locations/LizziesModule.reds` — 8269 bytes; SHA256 `678a1e5d90c6b2aca285cb30659babfda6856d929752ec17a12a5f156a03e47f`.
- `r6/scripts/NightCityAlliesModules/Locations/NorthsideApartmentModule.reds` — 2203 bytes; SHA256 `b4c5a3b26ce426bca492db3794e06f86a2a016072ba4c1ab3b88140b9e2e9c0e`.
- `r6/scripts/NightCityAlliesModules/Locations/NorthsideModule.reds` — 1009 bytes; SHA256 `ae39bc0595ff898c878e752a1745d58a1b3d0e59c10de32d85288c2e04339f4a`.
- `r6/scripts/NightCityAlliesModules/Locations/RedDirtModule.reds` — 1555 bytes; SHA256 `b81886d47dff7843c3553ecdafba3801e0e2c78ee068d4fa3fb20ed386ad7b36`.
- `r6/scripts/NightCityAlliesModules/Locations/TotentazModule.reds` — 6082 bytes; SHA256 `e0e0ed9e0a525513b8041343852ea64cd15ffd129f5cb5dbab57e3fc4580d7f8`.
- `r6/scripts/NightCityAlliesModules/Mercs/AfterlifeMercsModule.reds` — 5271 bytes; SHA256 `51cec5f05c1d3d868d01cc2dce7f9f91b559e256c5bab55568a1574842588d84`.
- `r6/scripts/NightCityAlliesModules/Mercs/MaelstromMercsModule.reds` — 22562 bytes; SHA256 `0c765fdbd3b26196591cbfd7a75b76e901fe76335b4c373db61d10931c6539ba`.
- `r6/scripts/NightCityAlliesModules/Mercs/MoxMercsModule.reds` — 1658 bytes; SHA256 `43019d8ecd5f8458c680698da90bc8ff8a8efd623c7b2583aae9aff3913ca0c1`.
- `r6/scripts/NightCityAlliesModules/Mercs/RoyceAndBrickModule.reds` — 1243 bytes; SHA256 `692e0ce17529c17ffac52d46bb343cf707b2fd98525f4d26d58daf062ba7ddb4`.
- `r6/scripts/NightCityAlliesModules/Mercs/TygerClawsMercsModule.reds` — 2851 bytes; SHA256 `eede59a8b034183419a34178309f36039329261dd9004e13d366ff495ee95200`.
- `r6/scripts/NightCityAlliesModules/Mercs/ValentinosMercsModule.reds` — 2119 bytes; SHA256 `8e20873b15884cbae306560bd6e1abdad4c64c1d70a4793e56bc53a6c7e40f4b`.
- `r6/tweaks/NightCityAllies/nca_equipment.yaml` — 1592 bytes; SHA256 `8dbc40323ff6b3354c83c8a203809d8c8f8b5d3cd5e8e8af79bcf93f71b462fb`.
- `r6/tweaks/NightCityAllies/nca_icons.yaml` — 1572 bytes; SHA256 `a0124a615bd5b35c9c85fa520a59616bbdff7ed293b8dcb642efa56168cfaf77`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/27625

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.