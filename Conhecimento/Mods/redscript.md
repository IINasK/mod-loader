# redscript

- ID: `redscript`
- Versao fixa: v0.5.32; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Compila scripts.reds de outros mods na inicialização do jogo.

## Como usar

- Não tem menu de uso diário. Instalar uma vez; conferir erros de compilação emr6/logs quando um script falhar.

## Dependencias

- [NightCoop + RED4ext 1.31.0](NightCoop.md) — 0.1.2-beta (`NightCoop`).

Requisitos autorais registrados: Jogo e compilador compatíveis; manter0.5.32 fixado.

## Phantom Liberty e compatibilidade

Compilador e carregador de scripts do jogo, sem assets da expansão. Exigência real é versão do jogo/compilador, não Phantom Liberty.

Nivel da evidencia: Compiler requirements and package role. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- Falha de um script dependente pode afetar o conjunto; logs distinguem erro de compilação de falha do download.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: redscript-v0.5.32-windows.zip; SHA256 `882c57a80b07e453bc8a34cd8430f460aceed789581de8743638e19dcfb7936f`.
- `engine/config/base/scripts.ini` — 39 bytes; SHA256 `ea8281682d4130f872e1703b25aa989a823f136fa1d886077af24aedb08b0d4c`.
- `engine/tools/scc_lib.dll` — 1930752 bytes; SHA256 `114851d192ec815119bbfa2272da572bf026fea8e5c0a910a4c8ad84b877dc09`.
- `engine/tools/scc.exe` — 430080 bytes; SHA256 `f7bf0a5a0986aec61d360ae79a3e957b8e21c0781309193a2a056c7bde0e90bc`.
- `r6/config/cybercmd/scc.toml` — 228 bytes; SHA256 `05802a334d72e87194d49765b7b852c2a983f8849851abcf3fd1366f64d4a934`.

## Fontes

- https://github.com/jac3km4/redscript/releases/tag/v0.5.32
- https://github.com/jac3km4/redscript

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.