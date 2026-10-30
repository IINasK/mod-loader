# Native Settings UI

- ID: `NativeSettings`
- Versao fixa: commit 4065b51; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Adiciona páginas nativas de opções emConfigurações>Mods para scriptsCET.

## Como usar

- Abrir Configurações>Mods; cada mod registra sua própria página.

## Dependencias

- [CET](CET.md) — v1.38.1 (`CET`).

Requisitos autorais registrados: CET>=1.25 conforme init.lua fixado; biblioteca declara1.97 e é usada comCET1.38.1 neste pacote.

## Phantom Liberty e compatibilidade

SomenteLua/UI de configurações do jogo base; requisito autoral éCET>=1.18.1. Sem assetsDLC.

Nivel da evidencia: Official requirements and five Lua files. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- É diferente deModSettings. Os dois coexistem porque atendem consumidores diferentes.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: Cron.lua; SHA256 `26f9685c60a86cc5624eabd0a2aec9796c4bba020156e05fad7e4648bd1938d6`.
- Pacote: EventProxy.lua; SHA256 `12eddc9b754ad409e9c10227c2bfb3976e0ecb0ea47035aea0b5c2a32ac141b2`.
- Pacote: init.lua; SHA256 `c3db971bf2c3117f81ebe5e6586589e0495bea77632deb2685c55e4011d9d86c`.
- Pacote: Ref.lua; SHA256 `6ae5a3c3e470ad970c0de72d0a82e39a644ce65fc257701a958cc781e2dd9627`.
- Pacote: UIButton.lua; SHA256 `163a11919b723b06ed35d3b1739081fa7ba2a2268b7fada71c77c71d30d7d487`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/nativeSettings/Cron.lua` — 2851 bytes; SHA256 `26f9685c60a86cc5624eabd0a2aec9796c4bba020156e05fad7e4648bd1938d6`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/nativeSettings/EventProxy.lua` — 7025 bytes; SHA256 `12eddc9b754ad409e9c10227c2bfb3976e0ecb0ea47035aea0b5c2a32ac141b2`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/nativeSettings/init.lua` — 64508 bytes; SHA256 `c3db971bf2c3117f81ebe5e6586589e0495bea77632deb2685c55e4011d9d86c`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/nativeSettings/Ref.lua` — 1278 bytes; SHA256 `6ae5a3c3e470ad970c0de72d0a82e39a644ce65fc257701a958cc781e2dd9627`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/nativeSettings/UIButton.lua` — 4552 bytes; SHA256 `163a11919b723b06ed35d3b1739081fa7ba2a2268b7fada71c77c71d30d7d487`.

## Fontes

- https://github.com/justarandomguyintheinternet/CP77_nativeSettings

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.

Conferência adicional30/10/2026: o arquivo fixado emcommit4065b51 declara version=1.97 eminCETVersion=1.25. A versão da receita continua sendo ocommit; não houve atualização da biblioteca para oFPV.
