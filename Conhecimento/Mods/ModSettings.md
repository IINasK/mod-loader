# ModSettings

- ID: `ModSettings`
- Versao fixa: v0.2.27; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Permite menus nativos de configuração para modsredscript.

## Como usar

- Abrir Configurações e a página de ajustes adicionada pelo mod; alguns ajustes pedem aplicar/recarregar.

## Dependencias

- [ArchiveXL](ArchiveXL.md) — v1.28.2 (`ArchiveXL`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).

Requisitos autorais registrados: RED4ext,redscript eArchiveXL compatíveis na seleção fixada.

## Phantom Liberty e compatibilidade

Framework genérico deUI e scripts. m_isDlcSettings no código é um estado da interface de configurações, não uma exigência de possuir Phantom Liberty.

Nivel da evidencia: Official role, requirements and static script review. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Não confundir comNative Settings UI: são duas bibliotecas distintas.
- Não instalar o pacoteall-in-one com dependências mais novas por cima da base fixa.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: mod_settings_v0.2.27.zip; SHA256 `33ce5f34bfec70f98cce9d3ded7f3ceb145392ad28e4dba1a3d141a736973879`.
- `red4ext/plugins/mod_settings/license.md` — 1072 bytes; SHA256 `9284f10a3ebe703a61c9c8ab0c1c889fddf8276405d2ac33c7377537d1cde448`.
- `red4ext/plugins/mod_settings/mod_settings.dll` — 1003008 bytes; SHA256 `0657294d06bcc9abc04ac2fea25702fd904de343d2dcb68e86d4a23caf6e5257`.
- `red4ext/plugins/mod_settings/ModSettings.archive` — 126976 bytes; SHA256 `6741ec88617def8ec6c42b5fe54970923ac9dc3413721dd10850ac02d56d5c2a`.
- `red4ext/plugins/mod_settings/ModSettings.archive.xl` — 91 bytes; SHA256 `8742606ff6cde630ca05d2bd03e763156a7814fbf5909a534f2d0f117e0b2b87`.
- `red4ext/plugins/mod_settings/module.reds` — 174 bytes; SHA256 `0cc914d006186da0fed7f4cd5ef7e2e634dfed7cd54e7b0e0b5573ac516029ce`.
- `red4ext/plugins/mod_settings/packed.reds` — 61823 bytes; SHA256 `a1cc6baa35dfd7111cbd474016860eb399041b3a5b304b1384d59eb9b983ca2d`.
- `red4ext/plugins/mod_settings/readme.md` — 5364 bytes; SHA256 `e46a7bac11719abc5358c82dcc2c257586989a6e486c21f95b4101dedf1afdf7`.

## Fontes

- https://github.com/jackhumbert/mod_settings

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.