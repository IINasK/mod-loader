# ArchiveXL

- ID: `ArchiveXL`
- Versao fixa: v1.28.2; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Expande recursos, aparências, roupas, cabelo e customizações por arquivosXL sem substituir toda a base.

## Como usar

- É infraestrutura: funciona automaticamente para mods dependentes; conferir red4ext/logs em caso de recurso não registrado.

## Dependencias

- [NightCoop + RED4ext 1.31.0](NightCoop.md) — 0.1.2-beta (`NightCoop`).

Requisitos autorais registrados: RED4ext e versão do jogo compatíveis. A base1.31.0 atende a revisão já auditada.

## Phantom Liberty e compatibilidade

README oficial requer jogo2.5 e RED4ext, sem expansão. BundlePhotoModeScope inclui entradas opcionaisEP1, mas são alvos de extensão de recursos, não uma condição de possuir DLC para carregar o framework.

Nivel da evidencia: Official framework requirements and static role. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Código/SDK compatível não garante que um mod de roupa que o usa não exija assetsPhantomLiberty. Auditar cada consumidor.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: ArchiveXL-1.28.2.zip; SHA256 `840ffabb7642930403faa14ac249353cb34ca4ce3097fea5d819d5fb28c9519f`.
- `r6/config/redsUserHints/ArchiveXL.toml` — 0 bytes; SHA256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- `red4ext/plugins/ArchiveXL/ArchiveXL.dll` — 1951744 bytes; SHA256 `ca32a4285cceb70b238b7b9f1d1785b836455bbfa359bc6c819478d7e8b69955`.
- `red4ext/plugins/ArchiveXL/ArchiveXL.yaml` — 26 bytes; SHA256 `89c39c2d5dea5f591a351069d3020d19de8faeb5867d619897f93cc2ae39f942`.
- `red4ext/plugins/ArchiveXL/LICENSE` — 1090 bytes; SHA256 `dcd5cfc3e620a938b674b5b74a18bba24ac1d8c1353271dac8599b0620238629`.
- `red4ext/plugins/ArchiveXL/THIRD_PARTY_LICENSES` — 13077 bytes; SHA256 `ed196638202f571f23e55df2a51b160273f10235fe5ac2573920a1f234c6a2cf`.
- `red4ext/plugins/ArchiveXL/Bundle/ArchiveXL.archive` — 389120 bytes; SHA256 `5b84333de16a536b36523090a6fbfcef4f866554cc93094df74ca6e37932c96f`.
- `red4ext/plugins/ArchiveXL/Bundle/Migration.xl` — 5926 bytes; SHA256 `830883ea3d5e50746ea698429dc51f5971e079a588c48e8fe097e90c6c25ca68`.
- `red4ext/plugins/ArchiveXL/Bundle/PhotoModeScope.xl` — 4848 bytes; SHA256 `77c10d76a8ae8ca696b63e3930fd1d4b3b14fa410ce90e4f95ab665826a82264`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerBaseScope.xl` — 1352 bytes; SHA256 `fac81f0618a074b06472b5574aeefa4dde16bc935abe0416ac23121ec7a3ee2f`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationBeardFix.xl` — 21911 bytes; SHA256 `c3145023b30c0d0d2533af4c9d48c306aa4d868016e3de54ece46509ba40ff39`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationBeardScope.xl` — 7845 bytes; SHA256 `e65cfcdc09ce84768fc59a0646b6f90df5cb2b07637903bb57b834159494e94e`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationBrowsFix.xl` — 5621 bytes; SHA256 `458fc4337ff99efe58d3bd69ab5fd0aed7a35006b8ff65bff97c948cba11c856`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationBrowsPatch.xl` — 1900 bytes; SHA256 `482a99c243bc49196e356615f3eb9bcc4f29458496940e600891e03f75337595`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationBrowsScope.xl` — 9628 bytes; SHA256 `afd084003d5bfcf147026cfc6f115d41153be7d982642372859ce19cd951d2c5`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationEyesFix.xl` — 4797 bytes; SHA256 `f2fc3657f8620f81fe58049bc2dbca13534c8a8a6da299bda70914b070854c39`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationEyesPatch.xl` — 1209 bytes; SHA256 `2b1e3736764b4de5aea6d55120703fcf5bc0249441851fc8dbee489df013c527`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationEyesScope.xl` — 1427 bytes; SHA256 `58d06e94151cb49de1004bee265f3dbf7e189a3a15de495a01129740bc06dbd6`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationHairFix.xl` — 1077556 bytes; SHA256 `844060af19f21e97a23320df3e0b21c8299c66bc147da397b357a97d1a0ff95c`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationHairPatch.xl` — 177 bytes; SHA256 `fd7807f3f244be977aac3d4296108619743e04ef80652da75702810e5a7377b9`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationHairScope.xl` — 50466 bytes; SHA256 `f31eec0b6676dc64466034d11c5386ea4616e75b79da92ff72c7330a4f50ea83`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationLashesFix.xl` — 3272 bytes; SHA256 `6d2b14f747e1f99544ae9de68dcb16424ae046399a71fc810a2725891cab7e70`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationLashesPatch.xl` — 594 bytes; SHA256 `f762902023eebbb44005525ae6d290d6a28af92f784192c04dad97c373bbb79b`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationLashesScope.xl` — 1228 bytes; SHA256 `c299cc8843fb8adefd471e202c53452c3f6e9087f521f27c7f9153c66928e990`.
- `red4ext/plugins/ArchiveXL/Bundle/PlayerCustomizationScope.xl` — 0 bytes; SHA256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- `red4ext/plugins/ArchiveXL/Bundle/QuestBaseScope.xl` — 255 bytes; SHA256 `f56ceacafdf303062913e5cff589d27098a67cdf0c874d11269da4887ae23789`.
- `red4ext/plugins/ArchiveXL/Bundle/VisualTags.xl` — 3576 bytes; SHA256 `8f1d9628212514a5080b3f42c068a1092942346bf20416d51472f068e5b75020`.
- `red4ext/plugins/ArchiveXL/Scripts/ArchiveXL.DynamicAppearance.reds` — 1926 bytes; SHA256 `43acc08335e541d81d92c235706fda692070c2e2fb5c60b96952e0a9e6ed7a83`.
- `red4ext/plugins/ArchiveXL/Scripts/ArchiveXL.Global.reds` — 369 bytes; SHA256 `b76ab91e5d405cae0d65f1905d0c78df3e9a3a8d964add5d794befa7cc18c533`.
- `red4ext/plugins/ArchiveXL/Scripts/ArchiveXL.reds` — 39 bytes; SHA256 `650ec0ceb419b30cd23a77ab7b058725268572f87e15b5775b41cf1d89847bc8`.

## Fontes

- https://github.com/psiberx/cp2077-archive-xl

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.