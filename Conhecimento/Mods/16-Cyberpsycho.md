# Cyberpsycho Encounters

- ID: `16-Cyberpsycho`
- Versao fixa: 0.8.0; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: unverified.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Cria ataques aleatórios de cyberpsicopatas e resposta policial/MaxTac.

## Como usar

- Jogar no mundo aberto. Ajustar frequência/regras emModSettings. Manter separado da densidade de conflitos entreNPCs doNightCityAlive.

## Dependencias

- [CET](CET.md) — v1.38.1 (`CET`).
- [redscript](redscript.md) — v0.5.32 (`redscript`).
- [ModSettings](ModSettings.md) — v0.2.27 (`ModSettings`).
- [TweakXL](TweakXL.md) — v1.12.0 (`TweakXL`).
- [Utilities2077](Utilities2077.md) — 0.7.0 (`Utilities2077`).
- [Codeware](Codeware.md) — v1.21.1 (`Codeware`).

Requisitos autorais registrados: Codeware,CET para logs,ModSettings,redscript,TweakXL,Utilities2077.

## Phantom Liberty e compatibilidade

Nexus não listaDLC como requisito, mas YAML clonaVehicle.v_standard3_militech_hellhound_police e referenciaMerrimac. A disponibilidade exata desses assets na instalação2.5 semDLC não foi comprovada; menção aDogtown emswitch isolado não é prova de exigência.

Nivel da evidencia: Unconfirmed required vehicle assets in exact package. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Autor informa convoy policial podendo nascer em paredes eAV parcialmente em prédios.
- ArmedApartments pode permitir ataques dentro de apartamentos.
- Não desligar por engano quando o usuário pede reduzir somente brigas deNPCs.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: CyberpsychoEncounters_0.8.0.zip-20489-0-8-0-1763320969.zip; SHA256 `23865c5990ffe57fa9f6d2b9b57eff50a85cf861a0fea9b14191df0bf95fdaf9`.
- `r6/scripts/CyberpsychoEncounters/CyberpsychoEncounters.reds` — 130739 bytes; SHA256 `264983b88df77847090003a30daea989eabf61d90893c892ac4fa527bd06e33a`.
- `r6/tweaks/CyberpsychoEncounters/CyberpsychoEncounters.yaml` — 297282 bytes; SHA256 `ccf520b65fa038c73478af7e0f0f6e93bae3669d26c4ebf4f664033ab5586d43`.

## Fontes

- https://www.nexusmods.com/cyberpunk2077/mods/20489
- https://github.com/U0047/CyberpsychoEncounters

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.