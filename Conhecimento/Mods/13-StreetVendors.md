# Vanilla Style Street Vendors

- ID: `13-StreetVendors`
- Versao fixa: 1.0.1; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Aproximar-se de vendedores de rua mostra diálogo nativo; V fala antes de abrir a troca.

## Como usar

- Aproximar-se de um vendedor visível fora de combate/veículo e escolher a fala de negociação. Ajustar distâncias/frequência na página do mod.

## Dependencias

- [ArchiveXL](ArchiveXL.md) — v1.28.2 (`ArchiveXL`).
- [TweakXL](TweakXL.md) — v1.12.0 (`TweakXL`).
- [Codeware](Codeware.md) — v1.21.1 (`Codeware`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).
- [Audioware](Audioware.md) — v1.10.1 (`Audioware`).
- [CET](CET.md) — v1.38.1 (`CET`).
- [Native Settings UI](NativeSettings.md) — commit 4065b51 (`NativeSettings`).

Requisitos autorais registrados: Frameworks da receita fixada. BibliotecasModSettings/NativeSettings têm papéis diferentes.

## Phantom Liberty e compatibilidade

Autor informa teste comPL, mas funcionamento esperado sem ela.46arquivos textuais fixados não apontam recursosEP1 obrigatórios; falas vêm do jogo base.

Nivel da evidencia: Author says DLC should not be necessary plus script review. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- O módulo antigo deStreetVendors doVendorFilters deve estar desativado/atualizado para evitar prompts duplicados.
- Não fornece respostas faladas dos vendedores.
- Pode interagir com mods que alteramInteractionUIBase,PlayerPuppet ouReactionManagerComponent.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: VanillaStyleStreetVendors-28640-1-0-1-1775580521.zip; SHA256 `947f8b321d9f773de0d9ccd4e7c69b90685049f7cfcbec8a3d75665bb3994857`.
- `archive/pc/mod/VanillaStyleStreetVendors.archive` — 20480 bytes; SHA256 `ce4ccbc7cf09558d58cd7364bc0961b9512a4de9fe517688b2620dadbe81a9a2`.
- `archive/pc/mod/VanillaStyleStreetVendors.xl` — 1871 bytes; SHA256 `fab8469093182bb30101886b768c09f54594051ba66f9edb545601cb954077d5`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/init.lua` — 1266 bytes; SHA256 `91bedeb81b875e7f88fa3078b222b4ca551135f96e7b85c703bcbeba6ae6c796`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/RedConnector.lua` — 2013 bytes; SHA256 `2142c223ff092525033608d90dc54294cf0b2a4b3651980617cbebe6387b4bd2`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Tweaker.lua` — 13763 bytes; SHA256 `c07f433e0ad86039589637c24474c5d87cf33fd0da50c900bbeaac3a73db6162`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Configuration/Defaults.lua` — 518 bytes; SHA256 `6824928054219aceedffd148f23d2858194284b3153a0b8d7dc809b82fa231e7`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Configuration/Menu.lua` — 3130 bytes; SHA256 `212fb94d30fd5f02dc929e84f11749fac37a1065c1129bba335fbf40d9825d74`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Configuration/Settings.lua` — 3260 bytes; SHA256 `e6f32518f410e1ef5618a03d76a5ab809ee377f68e67dbe8bde42c3651285bd4`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Configuration/UIStrings.lua` — 2163 bytes; SHA256 `29b1aae7c16e8ee77b5275d28ac45a8a90cef175010c80edae5a34bc09c9ba2f`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Data/ValidVendors/Vanilla.list` — 534 bytes; SHA256 `aae9564701b1c7cb3e91d016e6b79858d1a9847733258ba4397cad1c22327ed7`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Helpers/GetLocalizedTextByName.lua` — 2571 bytes; SHA256 `de18c4ab237c1d1b159531fecbc427b680ae65fcfc7415ff7cca56eb35552beb`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Utils/CallAndLog.lua` — 630 bytes; SHA256 `a525dc8f5c992c1ed7907de7f1fa6dcf68a7bbb0a47b28e307b85198e7be6d7e`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Utils/DumbLog.lua` — 3484 bytes; SHA256 `0ffc5c811f75b13192c2c46b816f981ddeb344855a43ffdc2ff489eaac112aae`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Utils/File.lua` — 7570 bytes; SHA256 `f460bab259f0a934e370618eae402425768cac67629b5ca735b7b1560306ab0b`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Utils/ListManager.lua` — 2177 bytes; SHA256 `f6e7dcf36b1f447a5044a6b6f3325ef9eda6bad7a94ac75be8e87dad83f50092`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Utils/MenuConfigurator.lua` — 8132 bytes; SHA256 `bfe35ff8e07693317c990c918786829f4e98962a2612b7f17a820ae29905c92d`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/VanillaStyleStreetVendors/Utils/StringProvider.lua` — 6172 bytes; SHA256 `3eac4543bb9740a334b4ebbe12326a4521acb0b611404b86ba3db15885cb8e6a`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.core.yml` — 10939 bytes; SHA256 `1425dd725ba6f3b7c7e44b0f3f12ea0b9d5c15dec7c6ef109a5b0e1412b2dd90`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.de-de.yml` — 1282 bytes; SHA256 `81508416f07246e94d2de0e01c7d388783761cd3d7f84441b41c52cb5e2ec5a9`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.es-es.yml` — 1290 bytes; SHA256 `f1368cd8d92bc10f6bbbc9e6b0c5ac20d40fd382703e4129769cbba323edc0d1`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.fr-fr.yml` — 1352 bytes; SHA256 `3a70b6173e226faaeb7e5b9e4818a2cdd8fc51ae981106cbe31d3f3c2617e947`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.it-it.yml` — 1272 bytes; SHA256 `eeb80634fd9b12c2771675a8c5a5ff2642a233d56aa048e6bb24b3d9ac06d05d`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.jp-jp.yml` — 1324 bytes; SHA256 `f85e1d7fdff0804fb0f640363974d98f3bd830ab4afc9b929a9a77a1586808e7`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.kr-kr.yml` — 1308 bytes; SHA256 `400895dbebb9e22370733aeb95b6f8adb47132d875151f215d551080a1e5b98e`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.pl-pl.yml` — 1311 bytes; SHA256 `c01ae07b8fedf74940ef96399f4624cb0502e2129a79861cf660962cef062c1d`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.pt-br.yml` — 1286 bytes; SHA256 `6ac74cfef6bd529f94b0c561c86c920e215386968a43cf3211327bdebc5bf4c8`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.ru-ru.yml` — 1378 bytes; SHA256 `c02b6b91f5863e3371aa4f418361de465d89064f51c2cd8aab364b45b1f23663`.
- `r6/audioware/VanillaStyleStreetVendors/vanilla.zh-cn.yml` — 1318 bytes; SHA256 `733efdb74965a8711d925c17ad9fde630a9fca4bb5e5cc4414bb323525b86f27`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 144008 bytes; SHA256 `0192891584bc42143855309b249b0184dbee6ca1b20f4a0fcffdc689fbbdc274`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 148324 bytes; SHA256 `59b7901059e99cd49464aacf10c02c9d1f76e9870c8b119cfafad39dbda43c50`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 152534 bytes; SHA256 `951370fd9e592a336c6be1250985febe4d183b62c67f7ce2ec143eb9546ddd3b`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_japantown_f_1b682524d044d000.wem.wav` — 159154 bytes; SHA256 `4aae73c0dd18332d72548090b41fa2ebc4978ced10c13544240f262618af487a`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 150758 bytes; SHA256 `d6796e61b33116b2d53bf0dec7dacc1ae59059533295b21ad48c2b2680148e28`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_japantown_m_1b682524d044d000.wem.wav` — 163808 bytes; SHA256 `d6e4c1f5fca79274f6c5abf3c708bf02046d3af5c992b0a1b19c8c6fa139b316`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 169988 bytes; SHA256 `877dec990d1fc77f45e31877ebeee7a27084af750ece6970ab321f7b2dbd6489`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/de-de/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 177376 bytes; SHA256 `c6e7377132794a09a37996e7e917e35df1909abbbdbbc73a60096ca8149d853c`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 149318 bytes; SHA256 `6d7d96f719c75eee9a81d548ee89498f762f70062db6b58ffb20d74b030e8b6b`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 145326 bytes; SHA256 `3a8359c9ffabf606e511deb3d851bc610535e0d53bc234201888a444e8655ac5`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 133204 bytes; SHA256 `2ad7103e9fd76f27329caf9b7b7ac54ec1db8c9bb2d442e0fc9d4e61e90745ad`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_japantown_f_1b682524d044d000.wem.wav` — 159810 bytes; SHA256 `fc3107f03af1e9798107a8c770fa57ea87aad86dc725f0d6726baa538996610d`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 134078 bytes; SHA256 `b190c19d9baa6ab9db0adcab0f8e8ef3c4d7d811e13bb9b4fc27f7db0b2d1422`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_japantown_m_1b682524d044d000.wem.wav` — 174962 bytes; SHA256 `55a126363ffc344b58441d58d6065cb6b48836c1519e54db2b07c0d12bc34b67`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 202716 bytes; SHA256 `a701da270d5ba58381d1131eb0ce20e7aeec1731654018697de7ae00be1282ac`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/en-us/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 202030 bytes; SHA256 `9b38aa108c3a0fe5b3590e5ac855563e2655a68add0ce8f4c42bd8904d851d9b`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 156226 bytes; SHA256 `dd485a153233eed1c6dc5d1e22655979443248781972c7e48c1ad7ef09735933`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 149746 bytes; SHA256 `3b18188428c9f50bb8620f5783770f28d526e99a498a839d7da9d165e8cb0e52`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 163064 bytes; SHA256 `b45c151be5d6fb96ea4c22accdb492e1eefef4681b0563b3f53b9167951b0bae`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_japantown_f_1b682524d044d000.wem.wav` — 157764 bytes; SHA256 `ee36885ee9c3967a12797f5beefd2a75bf0c1af2aa8a8cf735b69d85324bb079`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 168690 bytes; SHA256 `f70204086d1b9210971df61dbe78d51013d27e2891ea92b3ce4cedd1cc6b0121`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_japantown_m_1b682524d044d000.wem.wav` — 156914 bytes; SHA256 `3cba5b371f86c7587686932c564475c0e43074db772359e3782bd92e10fac4d3`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 189822 bytes; SHA256 `3c65e09ebf84a6611a79ee92801416a09e77c4c0f5221b1fb5be7056e9e9b8b5`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/es-es/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 194900 bytes; SHA256 `d9ca2bd1b8041ae5c0b00252a98992693368da959ffea71b605d2b5d5cd7bb28`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 142424 bytes; SHA256 `e6fb5558326ca89f8e7cd34b9c67b531e208315e23da434ffca8033f758263b7`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 146806 bytes; SHA256 `ae92075c3b0b5024ee140dd29d8a688c74c71966378e93a80716429ac9be5f31`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 132012 bytes; SHA256 `fd5e41a7235ea6d0939705262d00993fc4cc42976de5a47f73c29bfeff0d46d5`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_japantown_f_1b682524d044d000.wem.wav` — 161468 bytes; SHA256 `a699d98388df4c2107d783745ad81265a2303e812091521c86ddb7a415739ca8`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 133386 bytes; SHA256 `1596ff4df40676e6af079993db39e08666d349b24679fb2a72e1a0a3970fac4e`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_japantown_m_1b682524d044d000.wem.wav` — 165376 bytes; SHA256 `cc357925427682de7e30475b55a68873e4b54554835863b791960dcf4f5f2f52`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 195576 bytes; SHA256 `9557e570a9eb9b042262fab67064df7e8ffd031b812889032832bdce2eceda6c`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/fr-fr/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 195576 bytes; SHA256 `dbeb039c7c9528abf503fc4a413e141a6acbc3c2b8fb47f878473155e85052e4`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 150818 bytes; SHA256 `bbc8e31eeb27fddd3897fc5216e1b99374fad83b95ae2ac58a962b89385ce451`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 150818 bytes; SHA256 `d4a037d10ad5215cf083f406596de5195ae5a4975b0449336f23b0482b3ba5b8`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 130448 bytes; SHA256 `2ef18cd25d0201c895f210e250c13511cc4f97c9432703a301537b58ffeed031`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_japantown_f_1b682524d044d000.wem.wav` — 151808 bytes; SHA256 `5215c0aadf6f9d10e2e998e8f4382033a007198919163a8e7c02cc7b9453454f`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 130448 bytes; SHA256 `5bf576e44e4125e7d847d6b01d87969d0bfb84d9dd42b9ca57524c0105cbdfb5`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_japantown_m_1b682524d044d000.wem.wav` — 151808 bytes; SHA256 `5930ad6ee2eb6d379afed7630c47a385889a9a420b09aa9d774c9408fcf8efd9`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 210600 bytes; SHA256 `1c412b63cfe33103b9827772874a2eb5e256a734cd2fe4676c6453449ca3d5f5`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/it-it/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 210600 bytes; SHA256 `1966907d864709b09af529378de6c21fc81e5697bbe5e221f3d3a6fb2a1e16ff`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 159612 bytes; SHA256 `e74f610278f48540497779bc6e9cbf932843665f3c87a4c793cbe18d5a407341`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 159612 bytes; SHA256 `ad2984e16932671bfb234d3bd1819ebb1d3b77a1b156763574c8cc9251769674`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 148656 bytes; SHA256 `3d36803d592974712c3b7ef2bf76ecc09c96fb2ff11b2e84b52dd123d6ab4533`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_japantown_f_1b682524d044d000.wem.wav` — 180382 bytes; SHA256 `f139ce25437a8eb3d7c43100efcf4d8a703575605fcf4d284f29baf880a4896c`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 148656 bytes; SHA256 `62b7910668bd7857d43cc9b304731c9ad668d229b728d915e997d66a58197ba7`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_japantown_m_1b682524d044d000.wem.wav` — 180382 bytes; SHA256 `d51c6d02676c37c5151ce08eb6e5147a59e284c26624b0ca2fe51500c92b21d6`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 216816 bytes; SHA256 `9744b7826844977b0f76b84cf251801fde5f77feaa9336a8a853e12baafeb842`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/jp-jp/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 216816 bytes; SHA256 `90ed89b86a865e63c92107ba3a1d8fdba4ceeacce200e93774bad513d1a1aa16`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 148546 bytes; SHA256 `dc100e56742bfca4d5e2c2e2cda812b5b9de735a552a4258bea9f7063615a5b0`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 140724 bytes; SHA256 `69ce810e47dd8214da489b915ebc1fbf555d163f23538062b7dfd8b0c0e17490`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 136252 bytes; SHA256 `b8addd4fe9206b879fef9f0156a8ec365707283defb3e538ad56a1e713e73c31`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_japantown_f_1b682524d044d000.wem.wav` — 156594 bytes; SHA256 `abd843c85e9ab928a833b9e4d5ea54dccda17df3b72a43bb35669d5d2a6d3e53`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 135526 bytes; SHA256 `f2f33559f58643a6f39d84d8bbf753fc1259dbe0a1469dfa952e1036fe94715f`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_japantown_m_1b682524d044d000.wem.wav` — 167024 bytes; SHA256 `0046b1f23c04bdbd3d8fde2b133cbf037a6dd39d0da9a4c3cc8868bf072c00da`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 203658 bytes; SHA256 `4f53ac18be0a1f7c32568bf04083d885b818cd1dea02924f4b9d13e1c0a5a397`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/kr-kr/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 208660 bytes; SHA256 `ffd987cb18a054c95c53c20bc59951bb584c3eb41fc8e7cb481873ee123c8b91`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 160300 bytes; SHA256 `4ed25576ca1e7271905e8a12c5042ab775d1f630925f3de5631702166b8f0aef`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 160300 bytes; SHA256 `4196c8a731f44c7aa2ec43b5e8c814355527a498968e6c9a03465180c1b204d6`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 197932 bytes; SHA256 `b025a82470c62fd0cfbc75e47e14656039b7abe1b88d72633ea1e1ad3c510fb3`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_japantown_f_1b682524d044d000.wem.wav` — 163500 bytes; SHA256 `f0450f5a4f3a554a682b2f8f027a42a62188a772e8bfa0908824d9ffc0bb2f76`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 197932 bytes; SHA256 `ad572db35e6939cea753f458a853b007097456c02fceecf8fc1442687b692d82`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_japantown_m_1b682524d044d000.wem.wav` — 163500 bytes; SHA256 `4cba08abee6c6ed3f0447b2601ef0376c1a9f5b5075052a7dcf1c5bf6959f7c6`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 221108 bytes; SHA256 `0727e1ad8f82521367d4105890ff43ade44b659b114e46576d955f9c732209e6`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pl-pl/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 221108 bytes; SHA256 `5cfa9f3c0da51208aa05238c921cfde148d6088381247090d27ce44d69d4025b`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 166546 bytes; SHA256 `d9b27020d96de20575a42a64a169bb018e1d526ad729616c1e50c5b9eac6c04a`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 160462 bytes; SHA256 `a906e49106cda2e776865fba9a8e4dfb1d81687efd12337e4be7ae4e49e0de55`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 157788 bytes; SHA256 `d28829f66affba3938b257370ea7bb3564d79d9df2170288064b24c356e0d026`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_japantown_f_1b682524d044d000.wem.wav` — 162398 bytes; SHA256 `14c65eb2844a6256a321071078e1922726ffd5145a5889ed7da51d0b387d7080`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 144952 bytes; SHA256 `d2ffcf2489003aec17863fd6d82540ca1d3458db33307db79488638e174ece1e`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_japantown_m_1b682524d044d000.wem.wav` — 166414 bytes; SHA256 `bce92162d53d8d4b7f805825ecc5ea0975a6690fd717ab4b50e5ecd1f59400a4`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 208370 bytes; SHA256 `96487a569fe9ac83f828f71de8963da735610ca0b906005c095c292d8e6fa5d4`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/pt-br/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 202664 bytes; SHA256 `3ca54e13443510ae5a5aefe4a784b637c520ef777b499458d21d16751dd289fa`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 145326 bytes; SHA256 `98c19cbcca9c3c5787c90b7198a5d0a146830c72f803782b74a430a3803ad06c`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 145326 bytes; SHA256 `2c8bb6b1c93dd2f184e308a0308707bb5c031f29a257a405fcfb474fb4b96671`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 146210 bytes; SHA256 `f368703a1dd189759c7e0985893d832e55499e8f432af1080495e3230dee0cd7`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_japantown_f_1b682524d044d000.wem.wav` — 190336 bytes; SHA256 `2a524bcb3aeb4e7887312c32024aa1f64de502522f3eb10ac5a0ebf0a4574707`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 146210 bytes; SHA256 `d68e0a642423da94267d16f6e4294cfc954149addf4b7b3db1bbfa870f43ebd7`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_japantown_m_1b682524d044d000.wem.wav` — 190336 bytes; SHA256 `c732345e26d645571a6d855cf754d185ef7de2fca95281c2797c3fcc21e20124`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 227972 bytes; SHA256 `0e27d46d55f83404130a14a12ac6208ea3a09169ef158235cffe46e0813f2dbc`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/ru-ru/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 227972 bytes; SHA256 `b1615dd91a2103ec3b9786a2e41041438681883747300ea89cbd21dba97257eb`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_bls_ina_se1_foodshop_01_f_1adcce560246b004.wem.wav` — 131388 bytes; SHA256 `b9bf8014af2a0fd36575876ac8c8823def7f684a8c16459dd038b6f33852f35a`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_bls_ina_se1_foodshop_01_m_1adcce560246b004.wem.wav` — 131148 bytes; SHA256 `680371d18bdf74fafedf931815112ae45f5edbb5a6b8b8df918fa13440d62a5d`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_japantown_f_1a28c88f83464004.wem.wav` — 148356 bytes; SHA256 `8cf9f833f262a7db525a258f831a5c404020892ef14103499ad9d8c5b7d8494a`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_japantown_f_1b682524d044d000.wem.wav` — 162092 bytes; SHA256 `c67b53626e6bf5b1abcff6ca1665fcc1100691e017240ef151bfb22024a56964`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_japantown_m_1a28c88f83464004.wem.wav` — 139612 bytes; SHA256 `63219c499c635c5db287172f5a713387a4163c74714784b887528beca792ee92`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_japantown_m_1b682524d044d000.wem.wav` — 153644 bytes; SHA256 `72685f6272208276ab7184aa8c93c976be749824597bf1f067314e5a4a119d9d`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_wat_kab_clothingshop_01_f_1a90f361f95c5000.wem.wav` — 212284 bytes; SHA256 `0f435dc91a784070140367a912861d25049555a8d3d5a22e5128a611c4348aa7`.
- `r6/audioware/VanillaStyleStreetVendors/Vanilla/zh-cn/vo/v_wat_kab_clothingshop_01_m_1a90f361f95c5000.wem.wav` — 202284 bytes; SHA256 `0afdb783c642afd227a55597134e2ea40b7f25b61a6c2940cb5056a15492f847`.
- `r6/scripts/VanillaStyleStreetVendors/InputListener.reds` — 3649 bytes; SHA256 `70b8e56e9448cf40efdcd49f1316d859629f9651a6b4378fea34a6cf7029d074`.
- `r6/scripts/VanillaStyleStreetVendors/InteractionSystem.reds` — 8583 bytes; SHA256 `b94675499993490e55919efb2c1f03d1418e61d911e06cc4a00ccdf8b99a66d4`.
- `r6/scripts/VanillaStyleStreetVendors/Logger.reds` — 1247 bytes; SHA256 `44609a82fd9367dbdd3ad792527606e14157cce2604947e6819107db5b38656b`.
- `r6/scripts/VanillaStyleStreetVendors/SettingsSystem.reds` — 751 bytes; SHA256 `1cd6d3f31d7779f1c2760fd2e8312f71d88765fe824ba791bbc44cb46cc28ccb`.
- `r6/scripts/VanillaStyleStreetVendors/TargetSearchCallback.reds` — 1390 bytes; SHA256 `16e8f6e198ff94bbfde2c5ef124bcd789493158d2323aecbaf58d6a7468c4907`.
- `r6/scripts/VanillaStyleStreetVendors/VendorMenuCallback.reds` — 702 bytes; SHA256 `cbdca43be1bd3465b39622f3c9f5813870d7ea6b85aad2f2cdcec681753676da`.
- `r6/scripts/VanillaStyleStreetVendors/VendorSearchSystem.reds` — 9082 bytes; SHA256 `3a019176f57fd573cc1bbbd1bf4eb164e1997d65d25c710311a818803b3a750a`.
- `r6/scripts/VanillaStyleStreetVendors/Overrides/InteractionUIBase.reds` — 2577 bytes; SHA256 `1f6de8944fae01986c397a50c88fdf090443ae0c52f64c4243279087a7039baa`.
- `r6/scripts/VanillaStyleStreetVendors/Overrides/PlayerPuppet.reds` — 859 bytes; SHA256 `0971f7d5fcc9db4b04fd1e3e478229a92b84299c287b4c4cc205729abd3b7273`.
- `r6/scripts/VanillaStyleStreetVendors/Overrides/ReactionManagerComponent.reds` — 374 bytes; SHA256 `e691651b74a4e82b41f8f43708030fa8e78559771611baf2e7930eb7b618f69f`.
- `r6/tweaks/VanillaStyleStreetVendors/BaseVoiceLine.yaml` — 177 bytes; SHA256 `259d72ff1cfd0564b64a7a2f49f5f2f216fdc387c8611a4e90dd25e36744751e`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorType.Clothes.yaml` — 96 bytes; SHA256 `63fe842b0baffe302da688cf73d3b554fe8cbb43df64b67c1c668016dc5c1206`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorType.Clothing.yaml` — 97 bytes; SHA256 `2a37d2074c9e8ac2fc9dc168594efc3d35d949b64dead428bafe6dbeca6c526c`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorType.Drinks.yaml` — 87 bytes; SHA256 `2b0fa236cea45eca231e8cd806a95381b27a0f1ea339edab5c55d5af3bed7b09`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorType.Food.yaml` — 85 bytes; SHA256 `9855a19a5723dd069b4128b12741f4293bb7f33a1415ba45e0a1699c393bf7fc`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorType.Generic.yaml` — 200 bytes; SHA256 `cabbbab2e96441a927394ce4c6193eab6b051ffdfb10a2e487abbe2ddd927ebc`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorType.GrilledFood.yaml` — 92 bytes; SHA256 `8ac79d17477d26408e58994aca3545707e3a3538f91a246704097cf869a33bee`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorType.PackedFood.yaml` — 91 bytes; SHA256 `0f56a3510968046b8c6ffab19660e823ba596cec812efef2aeab17df8990251d`.
- `r6/tweaks/VanillaStyleStreetVendors/VendorTypeIcons.yaml` — 1841 bytes; SHA256 `838092c699fe75d71fee4bbc7e1a3e9d479911d81f05ea6a92eef6b03da8f7eb`.
- `r6/tweaks/VanillaStyleStreetVendors/VoiceLines.yaml` — 947 bytes; SHA256 `9baeca4d01d9ffd2cf7f54c4f7cfad832d916abf85130bde5a7ed811991dfffd`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/28640

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.