# Dark Future

- ID: `17-DarkFuture`
- Versao fixa: 3.0.0-for-2.5; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Sobrevivência urbana: fome,sede,energia,Nerve,dependência de substâncias,ansiedade e ajustes de gameplay.

## Como usar

- Ler tutoriais do mod ao carregar uma partida e configurar recursos emModSettings.
- Restaurar necessidades com alimentos,bebidas,descanso e interações; quase todos sistemas podem ser ajustados individualmente.

## Dependencias

- [ArchiveXL](ArchiveXL.md) — v1.28.2 (`ArchiveXL`).
- [TweakXL](TweakXL.md) — v1.12.0 (`TweakXL`).
- [Codeware](Codeware.md) — v1.21.1 (`Codeware`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).
- [ModSettings](ModSettings.md) — v0.2.27 (`ModSettings`).
- [InputLoader](InputLoader.md) — v0.2.6 (`InputLoader`).
- [Audioware](Audioware.md) — v1.10.1 (`Audioware`).
- [Native Settings UI](NativeSettings.md) — commit 4065b51 (`NativeSettings`).
- [CET](CET.md) — v1.38.1 (`CET`).

Requisitos autorais registrados: Frameworks fixados na receita3.0.0-for-2.5.

## Phantom Liberty e compatibilidade

Documentação éPhantomLiberty-aware, nãoDLC-only. DFGameStateService possui rotas alternativas de progresso do jogo baseq001/q101 ou introduçãoPL; aPL não é condição obrigatória noGetGameState.

Nivel da evidencia: Base-game and PL alternate initialization in reviewed scripts. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Por padrão pode restringir viagem rápida,crafting ou convocação de carros; isso é configuração, não instalação corrompida.
- Emboscadas ao dormir em veículo são próprias doDarkFuture e não devem ser removidas ao reduzir brigas entreNPCs.
- Mods que reescrevem os mesmos sistemas podem exigir ajustes de compatibilidade.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: Dark Future 16300 3.0.0-for-2.5 2026-10-25T15-19Z psSjsvvls.zip; SHA256 `4aeecfa9d5649cb8ae3281e91cb85b1f08ec720665f4cc02080f23d01df8deba`.
- `archive/pc/mod/Dark Future Localization.archive` — 737280 bytes; SHA256 `d2f7df3c0be476b401182b6790e5a65ef7d51ff905ec437bd4262711a8a9b973`.
- `archive/pc/mod/Dark Future Module - Relax Action Localization.archive` — 73728 bytes; SHA256 `e4e6263d8af1d4d37704a72df6296cef354d68f0b9db6da6aafae238f97a7a04`.
- `archive/pc/mod/Dark Future.archive` — 98324480 bytes; SHA256 `6ef0d7d8f584f665d8fed3b515524b01be7ed9efcbe1966b0d28de875ca8c06b`.
- `archive/pc/mod/darkfuture_localization.archive.xl` — 2893 bytes; SHA256 `69ddf6a863288f7a35cc5bbb04562850ee71ef6db449fca85d5a304c81d5d4a3`.
- `archive/pc/mod/darkfuture_module_consumable_anims.archive.xl` — 382 bytes; SHA256 `54e867aa8528e41c8193673375354b63d8d01128b7612b4f998d95791a57b470`.
- `archive/pc/mod/darkfuture_module_consumableanims_localization.archive` — 53248 bytes; SHA256 `31335694454d807f4a7d9005cf2a36b9a51e37c0f0deb0fb2feb813a9d2ac84a`.
- `archive/pc/mod/darkfuture_module_consumableanims_localization.archive.xl` — 2193 bytes; SHA256 `f72d9dc217e3273167162f6ff2dfad73ae03b0e6bfce1a78bf355b9e6d0383aa`.
- `archive/pc/mod/darkfuture_module_relaxaction_localization.archive.xl` — 4337 bytes; SHA256 `172895e2f0db863f6b4842a29460fda559d5e26abdcb5439b55de08db6a747ca`.
- `archive/pc/mod/darkfuture_worldbuilder.xl` — 71 bytes; SHA256 `8eec6eb2b3fae684a1afc28e2b054c9257c2413195354c79f88c116315e3fdef`.
- `archive/pc/mod/darkfuture.archive.xl` — 3660 bytes; SHA256 `c26c12946d79b2a6defe6b21712db443922dcefcd2b8fc85d0339d3613b43d45`.
- `r6/input/Dark Future.xml` — 2530 bytes; SHA256 `512e432cd8001e0c0764d338b5f203fa5d49337819e5c303d83f81c07d9b60c9`.
- `r6/scripts/Dark Future/Addictions/DFAddictionSystemAlcohol.reds` — 15489 bytes; SHA256 `4cba225c58b9ac0bd8b7cbeeb91a55b2b2cd5d6eae76c00a6ceb1b2eeeb79892`.
- `r6/scripts/Dark Future/Addictions/DFAddictionSystemBase.reds` — 46103 bytes; SHA256 `ba07b583bc36c6091d20fb28d90b0dc5131bcd8f9ecdc717be4e56fe0eb1bbf0`.
- `r6/scripts/Dark Future/Addictions/DFAddictionSystemNarcotic.reds` — 15368 bytes; SHA256 `018b766a029d8046e44d5dcf7ff200948c746de7338a288457792414ae625c3f`.
- `r6/scripts/Dark Future/Addictions/DFAddictionSystemNicotine.reds` — 16429 bytes; SHA256 `8be5d42737ea699dcf03fa6432d3257286ba27f8fc1f1f6f733ee2fb8bc72f92`.
- `r6/scripts/Dark Future/Conditions/DFConditionSystemBase.reds` — 23454 bytes; SHA256 `657feb0011eee3a87838dee5298870b0ddd8ff21850e9a9be31ac36df8be5411`.
- `r6/scripts/Dark Future/Conditions/DFConditionSystemBiocorruption.reds` — 69315 bytes; SHA256 `eb917d9e69e74adbe6300f345a4ee1bff6c14b96cf7f26de0e0f008a3e594dd6`.
- `r6/scripts/Dark Future/Conditions/DFConditionSystemHumanityLoss.reds` — 103708 bytes; SHA256 `f36b96a1cae20721a9eb903fe40229c0b74edbc073103eb5c567eafbaa025002`.
- `r6/scripts/Dark Future/Conditions/DFConditionSystemInjury.reds` — 11528 bytes; SHA256 `dfbbf436404cad9b70de35a08796717b58cd12ee05b243378fffde647c8253bd`.
- `r6/scripts/Dark Future/DelayHelper/DFDelayHelper.reds` — 2113 bytes; SHA256 `58ae7fd2192b4a120fe7d753a4906e42c36c81ebeda37c4373ef4bbc7724a167`.
- `r6/scripts/Dark Future/Gameplay/DFImpairedDrivingSystem.reds` — 25407 bytes; SHA256 `8f39ebdd98c7a0c04164de553850909ae121777f34acfe6bc2f6daeba6e5424c`.
- `r6/scripts/Dark Future/Gameplay/DFInteractionSystem.reds` — 110030 bytes; SHA256 `bde4b8b25fe2d5a2cfd1ad59d46e20eb2ef55bbbb76360a33ef400fd64b3715f`.
- `r6/scripts/Dark Future/Gameplay/DFModCompatSystem.reds` — 17837 bytes; SHA256 `91b9decd89b6f60dae887f751ecd21c5aeed2fa948882808a00b69daaf706768`.
- `r6/scripts/Dark Future/Gameplay/DFRandomEncounterSystem.reds` — 26745 bytes; SHA256 `1a962918dd5c8077c6fbb52cf3e2c12d56214bf077ee962e4fdf2dad5eca908a`.
- `r6/scripts/Dark Future/Gameplay/DFRelaxActionSystem.reds` — 58560 bytes; SHA256 `3c6739db3471c429352e45f8dadbc7038650d4abb6834b2b48377efcef029570`.
- `r6/scripts/Dark Future/Gameplay/DFStashCraftingSystem.reds` — 10580 bytes; SHA256 `63f12b35d05747f824b96df881d6cd4692769984d2b9190e716e5d6d1026dfd1`.
- `r6/scripts/Dark Future/Gameplay/DFVehicleSleepSystem.reds` — 33753 bytes; SHA256 `84553120b6128088e46e308f3a0998101a079b2cb5da88a54371c46008e4af2a`.
- `r6/scripts/Dark Future/Gameplay/DFVehicleSummonSystem.reds` — 31717 bytes; SHA256 `b9793f1eacb40f756f45e21311b30e40b6568b2a5ad7c7af112a16a66a1eb26f`.
- `r6/scripts/Dark Future/Logging/DFLogging.reds` — 3033 bytes; SHA256 `2bb48abd4220a60b2e8e7dc8c0967b4a96273d168b10d27fcbd34eccd9728130`.
- `r6/scripts/Dark Future/Main/DFMainSystem.reds` — 45303 bytes; SHA256 `87566b8649dae24919ea760d2d3774216bded704701d71ba2d2f38fa691270f2`.
- `r6/scripts/Dark Future/Needs/DFNeedConsumables.reds` — 30529 bytes; SHA256 `6488045e6953ae967d87732885caaf6482c1b8426cb4ce1a53786a715ab96b65`.
- `r6/scripts/Dark Future/Needs/DFNeedSystemBase.reds` — 62562 bytes; SHA256 `f6ba6deead6390ccc2519a19cbe78243912087a37bf22e27d7eee840b68318f5`.
- `r6/scripts/Dark Future/Needs/DFNeedSystemEnergy.reds` — 34997 bytes; SHA256 `686a0c1d1ec639cc14483660aa8b5cbaac749b1fb18ee9476bbd3ff163df4538`.
- `r6/scripts/Dark Future/Needs/DFNeedSystemHydration.reds` — 12209 bytes; SHA256 `54c230d0b15ddc151b9770a40d1c3aa81e5f81a7d24f03fd61bbb4666fdba05b`.
- `r6/scripts/Dark Future/Needs/DFNeedSystemNerve.reds` — 59587 bytes; SHA256 `eaaaa8dc7f5554b07f7e9ee0b5db1de80475cddccfbfaebb0a9252f1fef65fcd`.
- `r6/scripts/Dark Future/Needs/DFNeedSystemNutrition.reds` — 13231 bytes; SHA256 `7c223b2f3be6aeeb700d9ec062e76239df171b7387caaf62c10d03e5dbe694f8`.
- `r6/scripts/Dark Future/Services/DFAnimationService.reds` — 181591 bytes; SHA256 `630d4bd8e743756f3920108e00bd2ef5ee002e2623ccb2c9719f32bb417ef952`.
- `r6/scripts/Dark Future/Services/DFCyberwareService.reds` — 11843 bytes; SHA256 `44888008748cdaeb32dd9d41885612eb24882aee27144657f5de12ad2a6df7ff`.
- `r6/scripts/Dark Future/Services/DFGameStateService.reds` — 22358 bytes; SHA256 `8403cb3b084bf849943bede25f024f03e67a85ac43179e4045183785d740d50a`.
- `r6/scripts/Dark Future/Services/DFNotificationService.reds` — 37829 bytes; SHA256 `44e8ee234c1121785af558a51c72dfea27f4dd536ed2d9ded4f2ef989c534d0a`.
- `r6/scripts/Dark Future/Services/DFPlayerStateService.reds` — 140688 bytes; SHA256 `2e747c7c27d4552d15c1da9f63b77763bf60c8f4ff928da9a4e6b834737eb789`.
- `r6/scripts/Dark Future/Settings/DFSettings.reds` — 212705 bytes; SHA256 `b08bba41096af48c35dbbd538f6fd5aca06b2467af794d1ca10af2af7aedd4c0`.
- `r6/scripts/Dark Future/System/DFSystem.reds` — 11009 bytes; SHA256 `ae2f00a9ea90d0780b411113dd1ba35c5a80a83d1e5edcd16b148078d48bed03`.
- `r6/scripts/Dark Future/UI/DFBackpackMenuUI.reds` — 20271 bytes; SHA256 `09c339efb72ad6a5bd882e2a9a84f6ce51bcc0ba7817916ecf749de3a71de777`.
- `r6/scripts/Dark Future/UI/DFBaseGameUIOverrides.reds` — 80023 bytes; SHA256 `cab2c488dddf3548d82d22227e4d29890a5a6798cb9d77eac37137926fa54998`.
- `r6/scripts/Dark Future/UI/DFConditionsMenu.reds` — 22153 bytes; SHA256 `b3b414629594d88d378dacddc47d8cc143e48f70b337ba3cffd6f4ab256fcfbb`.
- `r6/scripts/Dark Future/UI/DFHUDSegmentedIndicatorWidget.reds` — 27047 bytes; SHA256 `20b81e05543099f00c5b539632d27abfe5e9799e8e88c525fb5a5c3cea0b98c2`.
- `r6/scripts/Dark Future/UI/DFHUDSystem.reds` — 57668 bytes; SHA256 `1f3234f44eafc01a677b5c4dad56eb72eec9632992f75c7c08a7c1dc51592cb4`.
- `r6/scripts/Dark Future/UI/DFInventoryStatsBarOverrides.reds` — 39493 bytes; SHA256 `a927a91cb93fe2df309e8ac6146c5f1599b505ff8bd21bbcdbea215ec5d908c5`.
- `r6/scripts/Dark Future/UI/DFNeedsHUDBarWidget.reds` — 51334 bytes; SHA256 `a4cbe36e9618fd58c9b1bc438f76236390c42c7319cb76710f2ac29a529c459a`.
- `r6/scripts/Dark Future/UI/DFNeedsMenuBarWidget.reds` — 24768 bytes; SHA256 `3a27e90f27440583cbb858e0a88842cd7ebabf2197e2202c1fb829394f2cee2d`.
- `r6/scripts/Dark Future/UI/DFRevisedBackpackMenuEvents.reds` — 756 bytes; SHA256 `908c32f81a62205ad74726cf30f97531919995cd8e60814e8a2c94374562fe00`.
- `r6/scripts/Dark Future/UI/DFRevisedBackpackMenuUI.reds` — 24525 bytes; SHA256 `a7fe52aeeb84a59fee4c3ff5561ec622837df895d8d0e031c3c70c2ef38e87e8`.
- `r6/scripts/Dark Future/UI/DFTimeskipMenuUI.reds` — 25644 bytes; SHA256 `e26e416d11f5958de7b8e0f72801fd8303f342bb0efdf7760b60a97d8bdf43c9`.
- `r6/scripts/Dark Future/Utils/DFColor.reds` — 16020 bytes; SHA256 `7f65c0ba830c6959bb5e721c9e94be57510f4a9516903523973b449c0a3b9446`.
- `r6/scripts/Dark Future/Utils/DFCommon.reds` — 5250 bytes; SHA256 `a7bbae09dc52d4553b6435ed79d93286ee3b228210e1b38859d571bd9663c256`.
- `r6/scripts/Dark Future/Utils/DFEnum.reds` — 1249 bytes; SHA256 `5474f554902462128d63d6619facac07a2801ec2d0970ec6f6a15e63bbe861ff`.
- `r6/scripts/Dark Future/Utils/DFFactListenerUtils.reds` — 7038 bytes; SHA256 `bff550fd5429154996738e5dfa8174efa0dbb2b23c46547b166be02308c24baa`.
- `r6/scripts/Dark Future/Utils/DFResourceUtils.reds` — 23573 bytes; SHA256 `e9c8e62d49defd98c9a5246e93b80c943b9758140e61332b4e53b1801f065e97`.
- `r6/tweaks/Dark Future/ConsumableAnimations.ChoiceCaptionIcons.yaml` — 12566 bytes; SHA256 `2ea81db6b8f489665f8842422d397c655e86398b20caf99dbbdf19fb0301fada`.
- `r6/tweaks/Dark Future/DarkFuture.AddictionEffects_Alcohol.yaml` — 11976 bytes; SHA256 `383f07b624a38387ba429d7aeb1547c5df023ec2b3b4f2085da7a16e634200bd`.
- `r6/tweaks/Dark Future/DarkFuture.AddictionEffects_Narcotics.yaml` — 11515 bytes; SHA256 `3adaf515bfe327b46d83770d6d9a47f4b41219f1f4e47afe7c0fe18ff8fe6b3b`.
- `r6/tweaks/Dark Future/DarkFuture.AddictionEffects_Nicotine.yaml` — 11792 bytes; SHA256 `adbb8fb736bc4acd90259f38c7c005db3bbda8dc9b60dd46af8d65c19c479f53`.
- `r6/tweaks/Dark Future/DarkFuture.AvatarImages.yaml` — 167 bytes; SHA256 `7872619f057a9c0b269a71c33d317d11c8abea78c84fc6c3dcaaea57cf80b1d5`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_AlcoholData.yaml` — 32927 bytes; SHA256 `3fda88a1b0352e4b172a3c242b689cb7d5c65cc342f36fc4cb260870c2e2c5d8`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_AlwaysOnEffects.yaml` — 10795 bytes; SHA256 `08a32c991b003af0b712084b438c0c008f202a0115cebe7a34916de23501c099`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_AmmoChanges.yaml` — 3405 bytes; SHA256 `8d5d1d66bc74423b4f2283a6c36fa970de184b02ffecac1e60f1b3de76de2e94`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_BonusDurations.yaml` — 454 bytes; SHA256 `8a71349b29c5381f1b258d83fbd4f347443904b716457f2a9d322d4736b2a6b8`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_Boosters.yaml` — 7942 bytes; SHA256 `35c478e53f7ec0ffe0c1da28f09a2b698f233a7f374bd4709b05fa868aad9f87`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_BreathingHeavyAnim.yaml` — 370 bytes; SHA256 `82d161e60d0df61a12cd33622083f49a27e62a792fb537a0559eb6a72387f49c`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_ConsumableItemData.yaml` — 62618 bytes; SHA256 `1b083ddc2257cfefeab5ff9b2e78067eaf00110d698abb623f929aaabc2e208a`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_Cyberware.yaml` — 3823 bytes; SHA256 `3243211e0ea421aad5ad705ed13771d59a446b0369a791364c58b39a4f575367`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_Icons.yaml` — 587 bytes; SHA256 `5a0148f67f1af82561c9dcf5f9711511d9910fe0b6c7f8e2ab0c1e2f56fe485f`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_MiscIllegalItems.yaml` — 686 bytes; SHA256 `fa143fe10b2a6e618f2233350f1d2be733a93931366474ef6015352e1c7d799a`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_Perks.yaml` — 1351 bytes; SHA256 `972f72063d331352f0efe7cdb46eba7d9a3dc2dc3505892fce18db3d5145da96`.
- `r6/tweaks/Dark Future/DarkFuture.BaseGameUpdate_VendorQuantities.yaml` — 5421 bytes; SHA256 `111b04c651be7efde5c557f730b5d0b7459e89a247f2ecbf33c50b5266420f31`.
- `r6/tweaks/Dark Future/DarkFuture.ConditionEffects_Biocorruption.yaml` — 16481 bytes; SHA256 `137f7c70893e84225481ed09fe4b5f853bf52f7b4b87791f85482a094f9520dc`.
- `r6/tweaks/Dark Future/DarkFuture.ConditionEffects_HumanityLoss.yaml` — 13218 bytes; SHA256 `2c6991bd5dfe12e29d935a0608340cfb272fb033cadd33b4972b4fe684cffd7b`.
- `r6/tweaks/Dark Future/DarkFuture.ConditionEffects_Injury.yaml` — 12560 bytes; SHA256 `17c464449ad18d8fe5c473123ba12424c5f447c9d92c52eeb225b0f4b7572edd`.
- `r6/tweaks/Dark Future/DarkFuture.ConsumableAnimData.yaml` — 64105 bytes; SHA256 `c749b5eb4102b70edbe3d1c89dd6a52c8310b7ecf09c3fd4a103981a94f79002`.
- `r6/tweaks/Dark Future/DarkFuture.ConsumableCigarettes.yaml` — 23757 bytes; SHA256 `e210a99a4ae061aabc082e76b46c42f47c48b2c932bf95d65d93942ab7b231d5`.
- `r6/tweaks/Dark Future/DarkFuture.MetaWellnessEffects.yaml` — 2063 bytes; SHA256 `8882319c0da9c4de6154aaca0c124e8217b64fd4eaa95a86bef836bc7c9c1657`.
- `r6/tweaks/Dark Future/DarkFuture.MiscIcons.yaml` — 168 bytes; SHA256 `f0775a86be09e1d410fbf07d08bd4434639575935ca38264eb8d4b7a2179d085`.
- `r6/tweaks/Dark Future/DarkFuture.NarcoticData.yaml` — 33106 bytes; SHA256 `bebb65921e933d2260a2833c000803388a2409db92f6837ba73678b650c902f2`.
- `r6/tweaks/Dark Future/DarkFuture.NeedPenaltyEffects_Energy.yaml` — 12864 bytes; SHA256 `4e2f84d2c4f1473c81d65e1ed638a621f65dfab467db0487892de57985c78e40`.
- `r6/tweaks/Dark Future/DarkFuture.NeedPenaltyEffects_Hydration.yaml` — 8270 bytes; SHA256 `4d7d185cdaabcb0a2f9290352b5100cae2f18263bb31f7e4884a8a35c7df12b5`.
- `r6/tweaks/Dark Future/DarkFuture.NeedPenaltyEffects_Nerve.yaml` — 35885 bytes; SHA256 `1cdeda8c72accb9ff2b74e2bda3a882a54c177d93afff75cd3d8340c095b3e84`.
- `r6/tweaks/Dark Future/DarkFuture.NeedPenaltyEffects_Nutrition.yaml` — 11484 bytes; SHA256 `f236f57fcd3b4b6e7c71006eef93a8857fa46430de80a8097dad7d77f8ea26d5`.
- `r6/tweaks/Dark Future/DarkFuture.NewCyberware.yaml` — 58516 bytes; SHA256 `cfe6035c09bb70fa2ab01b31e93322a18dd3d69ec1a6f305afe6d26434181734`.
- `r6/tweaks/Dark Future/DarkFuture.NewDrugs.yaml` — 46652 bytes; SHA256 `7cdbea1f9d4ee8aa7b4b3aa2214ca6cd5135234a8fd7fce811180bbfe686c80e`.
- `r6/tweaks/Dark Future/DarkFuture.NewWorldMapFilterName.yaml` — 81 bytes; SHA256 `4211179b27312dbf0874fc4eb133555ae0a354a1961dacac412f7fe1e8f453d1`.
- `r6/tweaks/Dark Future/DarkFuture.PainToleranceEffect.yaml` — 2732 bytes; SHA256 `b76ca84219f736cc3f7e07e854f7c81d61c1888b4f72ff00e60d54f1566d8cab`.
- `r6/tweaks/Dark Future/DarkFuture.SceneOptionDetection.yaml` — 6158 bytes; SHA256 `6640ef7d7deef7337ccdfa80f821049b40c565b5b3c9a91dee72c74615c659a3`.
- `r6/tweaks/Dark Future/DarkFuture.SprintBlockEffect.yaml` — 1773 bytes; SHA256 `6aad7ce9996dcbce0f060e50712fda40a5e4707f33af5bb9884f4e5edb68e52e`.
- `r6/tweaks/Dark Future/DarkFuture.StashCraftingInteractions.yaml` — 309 bytes; SHA256 `f0f3fc3bea1170baa1d85f7778dfe6d3eea249e5575c62b0f500dac2e78cb296`.
- `r6/tweaks/Dark Future/DarkFuture.TutorialImages.yaml` — 167 bytes; SHA256 `af8667015da7c41f364fb660b9d29206a738e98ed7cedb80015c77eb57e14622`.
- `r6/tweaks/Dark Future/DarkFuture.VehicleSleepingEffects.yaml` — 1723 bytes; SHA256 `3550940caa88099e6f8b07292a10ebc1320545712dac65a0d85c80b18b4a8419`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/16300
- https://wiki.darkfuture2077.com/mod-settings

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.