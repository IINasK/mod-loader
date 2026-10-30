# Spot On Radio

- ID: `SpotOnRadio`
- Versao fixa: 1.5; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

ConectaSpotify ao rádio54.4; também captura áudio de aplicativo local peloAppCapture.

## Como usar

- Abrir o jogo/carregar partida. Entrar na própria contaSpotify pelo navegador aberto uma vez ou pelo botãoLog in to Spotify na janelaCET.
- NoSpotify escolher dispositivoSpot On Radio; no carro sintonizar54.4Spot On Radio. O volume vem das opções de rádio do jogo.
- SeSpotifyConnect recusar áudio, testarAppCapture no painel usando o aplicativoSpotify.

## Dependencias

- [NightCoop + RED4ext 1.31.0](NightCoop.md) — 0.1.2-beta (`NightCoop`).
- [CET](CET.md) — v1.38.1 (`CET`).
- [RadioExt](RadioExt.md) — 2.92 (`RadioExt`).

Requisitos autorais registrados: RED4ext1.31.0,CET1.38.1,RadioExt2.92,Windows64,SpotifyPremium paraConnect.

## Phantom Liberty e compatibilidade

Somente scripts,painel,metadata e plugin de áudio; não usa assetsDLC. Requisitos específicos1.5 sãoRED4ext1.31.0,CET1.38.1,RadioExt2.92 eSpotifyPremium paraConnect.

Nivel da evidencia: Exact package and documented Spotify requirements. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Login/sessão ficam emAppData/SpotOnRadio, nunca compartilhar credenciais noEXE.
- Algumas contasPremium falham no clienteunofficial/librespot;AppCapture é alternativa.
- Autor declara contagem anônima única na primeira execução.
- Não há sincronização musicalNightCoop documentada.
- Nexus1.5 contémDLLFileVersion1.4.2.0; hashes exatos prevalecem.

Coop: Instalados localmente29/10/2026; sem login/reproducao/sincronizacao musical testada pelo agente.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: SpotOnRadio 34305 1.5 2026-10-27T10-19Z D2ZS2nnI3.zip; SHA256 `1b55f0713673221f18e9c619f9e990456383be781ebe611dd0fa08927da62f13`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SpotOnRadio/init.lua` — 24587 bytes; SHA256 `4fd53615c1820059b798a6d3b7d355ca940fd37c994121c757eb8b509d4ff72b`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/SpotOnRadioAppCapture/init.lua` — 14041 bytes; SHA256 `acdec280be6199d5dc07f674c99d30fe7a254f2e9132ca8ce02e73ad9da0248e`.
- `bin/x64/plugins/cyber_engine_tweaks/mods/radioExt/radios/SpotOnRadio/metadata.json` — 401 bytes; SHA256 `f9beed812a4a443c8e562427b9359f97d7e78e5154cc302df0e5ce84a5f5790e`.
- `red4ext/plugins/SpotOnRadio/THIRD-PARTY-NOTICES.txt` — 1942 bytes; SHA256 `cda884a390ae6ab9e9d6250ebd1fac9f9d9d741fb039f47d4913d2d5c2ce5885`.
- `red4ext/plugins/SpotOnRadio/spot_on_radio.dll` — 11513344 bytes; SHA256 `4dc5d118c084ebb98661889e3f2c649939493448686e1aefe7bcd5a18feef937`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/34305

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.