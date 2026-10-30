# TweakXL

- ID: `TweakXL`
- Versao fixa: v1.12.0; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Aplica alteraçõesTweakDB descritas porYAML/RED, usadas por veículos, roupas e gameplay.

## Como usar

- Não tem menu. Atua ao carregar o jogo; consultarTweakXL.log se houver registros ou tipos rejeitados.

## Dependencias

- [NightCoop + RED4ext 1.31.0](NightCoop.md) — 0.1.2-beta (`NightCoop`).

Requisitos autorais registrados: RED4ext e patch do jogo compatíveis.

## Phantom Liberty e compatibilidade

Framework genérico do banco de dados do jogo. README exige jogo2.5 eRED4ext; scripts fixos sem recursosEP1 obrigatórios.

Nivel da evidencia: Official framework requirements and static role. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- O consumidor pode clonar registroDLC ausente; isso não transforma TweakXL em mod que exigeDLC.
- BMW/Yamaha do catálogo estão retidos por falhasTweakXL reproduzidas, não por esta classificaçãoPL.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: TweakXL-1.12.0.zip; SHA256 `a7c39b769e6c51d9f27412a101fb87e6a1fb8452f0e37ee0d1d1f3f44e656fa8`.
- `red4ext/plugins/TweakXL/LICENSE` — 1090 bytes; SHA256 `dcd5cfc3e620a938b674b5b74a18bba24ac1d8c1353271dac8599b0620238629`.
- `red4ext/plugins/TweakXL/THIRD_PARTY_LICENSES` — 14482 bytes; SHA256 `0b7ec160e8f8882c384023d879a3f76f5b197f22c496473bfba96d09e9b0a3e3`.
- `red4ext/plugins/TweakXL/TweakXL.dll` — 1466368 bytes; SHA256 `2765cc55833945422b333fd2db1ee67078b70558edef0f9bd453e00d55614951`.
- `red4ext/plugins/TweakXL/Data/ExtraFlats.dat` — 22637 bytes; SHA256 `7143fe962beb17c84851f6cb64e29fdc52323a41925465005a8bab22bff8a198`.
- `red4ext/plugins/TweakXL/Data/InheritanceMap.dat` — 627816 bytes; SHA256 `dfc376014bfb3accc72e757f35bd3c78ed2052d8b876fcbd730127746f114c18`.
- `red4ext/plugins/TweakXL/Scripts/TweakXL.Global.reds` — 5242 bytes; SHA256 `0d91d4c690fd3d6f72863e0caea746964ed8023a8d62d10311de3cf6e927fc47`.
- `red4ext/plugins/TweakXL/Scripts/TweakXL.reds` — 35 bytes; SHA256 `d3366f0bbe95373164ef1382e8567ca5bc108455440cc645c792ba3e6215b266`.

## Fontes

- https://github.com/psiberx/cp2077-tweak-xl

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.