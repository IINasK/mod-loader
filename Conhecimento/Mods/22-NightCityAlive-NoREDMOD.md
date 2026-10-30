# Night City Alive sem REDmod

- ID: `22-NightCityAlive-NoREDMOD`
- Versao fixa: 2.3; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Aumenta atividade de gangues no trânsito,motos,reatividade da polícia/civis e conflitos ambientais.

## Como usar

- Densidade deNPCs no instalador:Baixa recomendada,Média ouAlta;Aplicar e instalar troca a variante combackup. Reiniciar jogo depois.

## Dependencias

- [TweakXL](TweakXL.md) — v1.12.0 (`TweakXL`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).
- [CET](CET.md) — v1.38.1 (`CET`).
- [Codeware](Codeware.md) — v1.21.1 (`Codeware`).

Requisitos autorais registrados: PhantomLiberty declarado;TweakXL,redscript,CET,Codeware na receitaNoREDmod.

## Phantom Liberty e compatibilidade

Autor declara expressamente PhantomLiberty obrigatório. A varianteNoREDmod evitaREDmod, mas não demonstra dispensarPhantomLiberty; conservarPLtrue paraLow/Medium/High.

Nivel da evidencia: Explicit author requirement, conservative for exact NoREDmod variants. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Não confundirREDmodDLC gratuito comPhantomLibertyDLC pago.
- Não usar desativação deCyberpsycho/DarkFuture para reduzir somente conflitos entreNPCs.
- Baixa oficial não é idêntica ao antigo perfil local personalizado comaggressiveCiviliansLimit1.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: NCA Standard Density 2.3 10395 2.3 2026-10-04T08-35Z X32s3EYPc.zip; SHA256 `c3a2e869b9a6121d5276654dd85b7e2218ec221dc63432f6c9975941825e8137`.
- `r6/scripts/NightCityAlive/Logs.reds` — 2161 bytes; SHA256 `4cccd8a1e0de1b6a57a39c54baf162cdef3cab09e75790044bb222389edb5dd2`.
- `r6/scripts/NightCityAlive/NCTO.reds` — 16550 bytes; SHA256 `c67ba6de22950098f78306a93302173c27fe553038b2d4578ac1870f017a4e00`.
- `r6/scripts/NightCityAlive/Reactions.reds` — 11704 bytes; SHA256 `698870488631f7d7093e3cfa046c69244876619442f0144d4539496e20cf1bb3`.
- `r6/scripts/NightCityAlive/Traffic.reds` — 6501 bytes; SHA256 `7040d96b43faa6a66da14c0960c9932737e8b7929d10c7879d74ded7d2c95f9c`.
- `r6/tweaks/NightCityAlive/attitude.yml` — 3291 bytes; SHA256 `61d8eedb41442967f601ce6bd227c10bae6572eeca7ea3750ddaaee55b24ed53`.
- `r6/tweaks/NightCityAlive/stimpresets.yml` — 2164 bytes; SHA256 `80c9d6fdaa1f3c8ba93b3b69eae129480eec0dcbdde11fbc9e11cdd1821c90f8`.
- `r6/tweaks/NightCityAlive/aigeneralsettings/cdpr-to-low.yml` — 0 bytes; SHA256 `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855`.
- `r6/tweaks/NightCityAlive/aigeneralsettings/low-to-std.yml` — 60 bytes; SHA256 `c48d4259156a0f80ac2f99f7a7d70ed02d395974045513531f72a36d83ee7b09`.
- `r6/tweaks/NightCityAlive/passengers/cdpr-to-low.yml` — 19902 bytes; SHA256 `5662bb264b988641c69f7428f02b34f9e0d450ce84f814f2370185b670f62f95`.
- `r6/tweaks/NightCityAlive/passengers/low-to-std.yml` — 18848 bytes; SHA256 `dea1f4f2ee45c7846bddd2fdacb22c7fcd01f716bd408aaaab15e224f5473a0c`.
- `r6/tweaks/NightCityAlive/vehicles/alvarado.yml` — 500 bytes; SHA256 `b36f0bc593a30291275b18bfd1cf43e71f51e5c0a3fe70618b2f32343d976b83`.
- `r6/tweaks/NightCityAlive/vehicles/apollo.yml` — 169 bytes; SHA256 `47df9a0c542520da51f1e3bcca5bce38508d6d375b8ae33d7a92b1fd467b66ee`.
- `r6/tweaks/NightCityAlive/vehicles/columbus.yml` — 261 bytes; SHA256 `096b90d70a5c6f605ec654f290a35eb65fc293c4908e0414772dcce8d4bb4aac`.
- `r6/tweaks/NightCityAlive/vehicles/cortes.yml` — 347 bytes; SHA256 `7779b98064664cca5459871e9654f12baabf9b0cf3c2c32867af6a5e07383e39`.
- `r6/tweaks/NightCityAlive/vehicles/deleon.yml` — 507 bytes; SHA256 `f21f57d8c9e8bdd5ae044249b3bac9b1dd62fd887adc6fada60a31a45a5d608a`.
- `r6/tweaks/NightCityAlive/vehicles/emperor.yml` — 348 bytes; SHA256 `ff2b7c4252a9be573b8b75d7801e1b4d19cc37cdd24ae9348dd855de3c6cc2c2`.
- `r6/tweaks/NightCityAlive/vehicles/hozuki.yml` — 253 bytes; SHA256 `7fc6aebb50bda1b8174f85ea4217ca160fe1214238f791e247871b05e59f8ce3`.
- `r6/tweaks/NightCityAlive/vehicles/kusanagi.yml` — 253 bytes; SHA256 `373bc5335a4deaaba4c63981894e6f005ea72a94c20f77b1ff9074a95e0a99bb`.
- `r6/tweaks/NightCityAlive/vehicles/mackinaw.yml` — 367 bytes; SHA256 `366283cd8c5fffb3da920fc470ac4bb19537b76c9253a383ced633a76cb03711`.
- `r6/tweaks/NightCityAlive/vehicles/merrimac.yml` — 521 bytes; SHA256 `e07533a9c733c87b2199f68a5b960dbc46950b60df72f56c27b128795c6fab64`.
- `r6/tweaks/NightCityAlive/vehicles/nemesis.yml` — 199 bytes; SHA256 `46831a4e8d5ccca9ed1821f0aba93c7a57efef0de7723c626029f682b92bffe5`.
- `r6/tweaks/NightCityAlive/vehicles/r7.yml` — 250 bytes; SHA256 `c07e7ce6705b5b01aa3efa83beb68cfb792942c393bbeb5561f1f198fdfa39d0`.
- `r6/tweaks/NightCityAlive/vehicles/shion.yml` — 248 bytes; SHA256 `c912c7fe3b786cb46712ddc59d8e064b2148e63eb36da73fa32d4372affa3c59`.
- `r6/tweaks/NightCityAlive/vehicles/supron.yml` — 194 bytes; SHA256 `d324f2cc91582c876b70032b61f3cb0fd62cd4d38382b1a285446f61df593553`.
- `r6/tweaks/NightCityAlive/vehicles/type66.yml` — 256 bytes; SHA256 `06c84fe2ead0bc514c42a6fecb1f68d52e8cfca10fbf6a3ea116476282101ab0`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/10395

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.