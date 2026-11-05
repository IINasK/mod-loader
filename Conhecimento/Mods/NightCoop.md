# NightCoop + RED4ext 1.31.0

- ID: `NightCoop`
- Versao fixa: 0.1.2-beta; jogo 2.5.
- Grupo: Base.
- Phantom Liberty: not-required.
- Situacao: Disponivel conforme perfil e requisitos.

## Funcao

Adiciona sessões cooperativas ao Cyberpunk 2077. O pacote inclui a base RED4ext fixa.

## Como usar

- Abrir o jogo e usar Cooperativo no menu principal. Todos precisam da mesma build/protocolo.
- Se alguém não tem Phantom Liberty, usar a inicialização sem a expansão oferecida pelo mod. Saves que dependem da expansão não carregam nessa inicialização.

## Dependencias

Sem componente anterior na receita; preservar a base imutavel do pacote.

Requisitos autorais registrados: Cyberpunk2077 Windows64 em versão suportada; mesma buildNightCoop; loader já incluído.

## Phantom Liberty e compatibilidade

FAQ oficial responde que Phantom Liberty não é obrigatório e descreve reinício do anfitrião sem o DLC, sem apagar arquivos.

Nivel da evidencia: Explicit author FAQ. Sem ensaio runtime semDLC nesta revisao.

Genero/corpo: Consultar funcao e limites; componente nao e um refit corporal.

- A build0.1.2-beta usa protocolo143. O grupo precisa de build/protocolo compatível.
- Não apagar arquivos da expansão nem converter/restaurar saves automaticamente.
- O mod pode arquivar saves próprios de sessão ao iniciar; histórico emAGENTS/AUDITORIA.

Coop: Relato anterior deDouglas aprovou uso do pacote comDLC emcoop; esta ficha nao comprova runtime semDLC nem estende o relato a novosmods.

## Arquivos e identidade

Caminhos relativos a pasta do jogo. Lista completa de pacotes, hashes e tamanhos em../mods.json.

- Pacote: NightCoop-0.1.2-beta-2.5.zip; SHA256 `5418bc90a68c650344126a9532bec933d18c3e70cb9a97c515bb8695533c1621`.
- `bin/x64/winmm.dll` — 213504 bytes; SHA256 `c78308921e6697473b6407bf2a4ba1ac4c157b247031fbcdd91af5063a52af7d`.
- `red4ext/RED4ext.dll` — 1241088 bytes; SHA256 `58ddfe43766aa91249d11671b55a8323d5ec017f816d847affe4b6201dcd0709`.
- `red4ext/LICENSE.txt` — 1101 bytes; SHA256 `a0b08b28ae3931a5a41a1788aac6c7a391513bfc3599272021ea5b2c1af2f6f6`.
- `red4ext/THIRD_PARTY_LICENSES.txt` — 10203 bytes; SHA256 `b49e3d32f961c21e0b15f4c9b11da9455d7c9905a457b1dfa88504f6592220ff`.
- `red4ext/plugins/NightCoop/NightCoop.dll` — 19016192 bytes; SHA256 `65960e5926108e8de9dfd46476a10752f1ad7b8891152c81fd30af21dfa21a6f`.

## Fontes

- https://nightcoop.net/index.html
- https://nightcoop.net/download.html
- https://nightcoop.net/docs.html

Consulta:29/10/2026. Nao ampliar esta conclusao a versoes ou corpos novos sem revisar.
## Reconfirmação em 02/11/2026
FAQ oficial consultada no navegador integrado: não exige Phantom Liberty. Convidado sem DLC pode entrar após o anfitrião usar o reinício sem expansão oferecido pelo mod; saves feitos com a expansão não carregam nesse modo. A auditoria do perfil do loader 1.0.28 confirmou 84 itens visíveis e 25 testes focados em ambiente isolado. Ver ../Configuracoes/perfil-sem-phantom-liberty-1.0.28.md. Não foi feito ensaio runtime em jogo sem DLC.
