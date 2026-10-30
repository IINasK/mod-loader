# InputLoader

- ID: `InputLoader`
- Versao fixa: v0.2.6; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Mescla configuraçõesXML de comandos e atalhos de outros mods.

## Como usar

- Configurar as teclas nos mods que utilizamInputLoader; o loader atua automaticamente.

## Dependencias

- [NightCoop + RED4ext 1.31.0](NightCoop.md) — 0.1.2-beta (`NightCoop`).

Requisitos autorais registrados: RED4ext compatível.

## Phantom Liberty e compatibilidade

Carregador genérico de inputXML eRED4ext, sem assets/requisitos da expansão no pacote.

Nivel da evidencia: Official role and static XML inputs. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- CacheXML gerado emr6/cache não define a versão instalada e não deve ser apagado como arquivo alheio.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: input_loader_v0.2.6.zip; SHA256 `6b420ba1e27b2e13c275a576a37a17d48771a0d35e52731f08e3584444a3e2b9`.
- `engine/config/platform/pc/input_loader.ini` — 110 bytes; SHA256 `5da4c3350381e5cc07b74ee3eafad7e705b64c11efd1df0769d95f6a7d442acd`.
- `red4ext/plugins/input_loader/input_loader.dll` — 1624576 bytes; SHA256 `341319105e9dfedc2f6fa81d492c55acd9bf6c39b60668738d2c34f96745de2a`.
- `red4ext/plugins/input_loader/inputUserMappings.xml` — 400 bytes; SHA256 `68c8d86ce1f48c06e48da44d53b4fad8b7552dcb275ea640380303b76b037e87`.
- `red4ext/plugins/input_loader/license.md` — 1072 bytes; SHA256 `9284f10a3ebe703a61c9c8ab0c1c889fddf8276405d2ac33c7377537d1cde448`.
- `red4ext/plugins/input_loader/readme.md` — 3171 bytes; SHA256 `d00a7467800cf8e4755940b005533ba6fa1071485c76f457a4ed86ec46438a24`.

## Fontes

- https://github.com/jackhumbert/cyberpunk2077-input-loader

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.