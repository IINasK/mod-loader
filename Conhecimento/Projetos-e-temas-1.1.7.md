# Mod Loader 1.1.7 — projetos, menu e logo transparente

## Abertura e projetos

A abertura normal usa GeneralProjectHome: Criar novo projeto e Importar projeto lado a lado, projetos salvos e, abaixo, projetos prontos. Cyberpunk 2077 é o primeiro pacote pronto. A lista não é carregada nem instalada automaticamente na primeira abertura. Projetos novos começam sem mods ou categorias sugeridas, com o primeiro preset usando o nome escolhido.

Cada projeto tem um ID aleatório e guarda a biblioteca e os registros em ModLoaderGeral/Projetos/ID. O índice projects.json contém só identificadores, nomes, tema/idioma e pasta padrão de downloads. Não contém pacotes baixados, mods, saves ou cópias do jogo. O cache de downloads existente é compartilhado; não há cópia de cache por projeto. Listas, variantes escolhidas, destinos e recibos ficam no estado do projeto correspondente.

Se já existir ModLoaderGeral/lista.json e não houver índice, a biblioteca antiga vira um projeto salvo legacy. Estado, recibos DPAPI, instalações genéricas e sessões P2P continuam no mesmo caminho. Não mover, copiar ou regravar esses arquivos na migração. O nome usa o preset ativo quando disponível, senão Projeto anterior. Preferências anteriores são preservadas.

Criar a partir de Cyberpunk gera outro projeto com as receitas fixadas, sem alterar projetos existentes. No uso real, mantém a descoberta de pasta do Cyberpunk; em fixtures, isso é desativado. Abrir um salvo carrega sua janela. Projetos no cabeçalho volta à Home; fechar o editor também volta. Durante instalação/scan aplicam-se as guardas existentes. O auxiliar P2P próprio é descartado ao fechar a janela, como antes; não deixar o processo do projeto anterior ativo ao trocar.

## Menu e arquivos de projeto

O menu contém a seção Projeto: Novo/Abrir na primeira linha e Exportar/Excluir na segunda. Presets continuam sendo gerenciados no título da tabela; suas ações não estão nesse menu. Opções é uma engrenagem independente no cabeçalho. Compartilhar P2P permanece no rodapé da tela de trabalho.

Novo/Abrir retorna à Home e permite criar/importar outra biblioteca, sem substituir a atual. Importar/Abrir usa o JSON exportado pelo gerenciador e pede um nome; destinos e recibos privados não são importados. Exportar usa o formato de lista existente, com referências dos mods, variantes, categorias e presets. Não confundir com o ZIP de mods instalados do P2P.

Excluir pede confirmação e retira o projeto da biblioteca. Não desinstala arquivos do jogo nem elimina recibos, backups ou o estado local conservado. Um legacy excluído não reaparece automaticamente enquanto o índice existir. Reabrir dados eliminados manualmente não cria uma lista vazia sobre a anterior. Índice inválido é recusado sem reconstrução silenciosa.

O índice tem limite de 1 MiB/200 projetos, nomes de 120 caracteres, IDs GUID de 32 dígitos e gravação atômica. Caminhos de dados são calculados por ID, nunca pelo nome fornecido. Safe.NoLinks recusa reparse/junction, e Safe.In recebe caminhos relativos com barras normais. A pasta de downloads antiga ausente pode ser corrigida nas Opções; escolher uma nova exige uma pasta local existente.

## Temas e logo

A logo SVG não tem retângulo de fundo. Os 12 polígonos e a geometria da referência foram preservados. No aplicativo, vermelho/branco/detalhes são mapeados para Accent/Foreground/Muted do tema para manter contraste. No ícone do Windows, as cores originais vermelho e branco permanecem, com alpha transparente em todos os sete tamanhos. O gerador usa o mesmo SVG e não reintroduz fundo sólido.

Novo tema light-red (Claro — vermelho) disponível nas Opções e padrão de novas bibliotecas, além de Claro — violeta e Escuro. Preferências anteriores não são trocadas na migração. Tema, idioma e pasta de downloads escolhidos na Home persistem; novos projetos herdam essas preferências. Mudar as opções no editor também atualiza as preferências da Home.

Tema e idioma são preferências globais da interface e acompanham a abertura de outro projeto. Destino, lista, variantes, registros e pasta de downloads do projeto continuam locais a ele; a pasta escolhida globalmente é o padrão para projetos novos.

O verificador GitHub agora é compartilhado por Home e todos os editores na mesma sessão. Continua sendo uma consulta por abertura do aplicativo, mesmo ao trocar de projeto ou tema. A atualização do aplicativo não instala mods.

## Validação

Suíte geral final: 1.142 verificações em 19 relatórios, incluindo fila/instalação/P2P/snapshots já existentes, 50 verificações do armazenamento de projetos e 44 da Home. Abrir a janela real do projeto e voltar por Projetos foi testado em fixtures, preservando o hash do JSON. Tema/idioma conferidos em seis combinações, logo/ICO com alpha real, menu com quatro ações, alinhamento dos controles e largura reduzida. Nenhum teste instala mods no jogo real.

Os primeiros ensaios detectaram dois ajustes necessários: Safe.In não aceita Path.Combine com barras invertidas na referência relativa; DataPath passou a usar Projetos/ID. Os asserts antigos de P2P/Opções no menu foram atualizados para a nova disposição. A conferência visual encontrou uma coluna da Home maior que a janela após reduzir a largura; as colunas-raiz foram fixadas em 100% e o teste passou a conferir os limites da janela, não só o pai imediato. Essas falhas foram corrigidas antes da entrega.

Detalhes, hashes e resultados ficam em Validacoes/1.1.7.json. Ensaios de rede entre computadores e gameplay não foram repetidos nesta revisão de interface e organização.
