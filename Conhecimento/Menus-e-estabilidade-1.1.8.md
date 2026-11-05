# Menus e estabilidade — Mod Loader 1.1.8

Revisão de 05/11/2026, incremental sobre 1.1.7. O catálogo e o motor de instalação não foram substituídos.

## Uso

O aplicativo começa na seleção de projetos. Criar e importar usam botões de44px; os projetos salvos recebem destaque ao passar o mouse. O projeto pronto Cyberpunk2077 abre diretamente com nome fixo, sem pedir outro nome ou criar outra cópia a cada abertura. Projetos antigos sem identificação de template continuam pessoais.

O seletor Projetos mostra os pessoais primeiro e os padrões por último. Clicar novamente no mesmo botão fecha o menu; clique fora, Escape e desativação também cancelam. Não há X nesses menus de navegação. Formulários de edição mantêm seus próprios controles de cancelar.

O menu de três riscos tem uma coluna: Nova lista, Abrir lista, Exportar lista, Importar lista; depois Compartilhar e Opções. Abrir lista volta à seleção de projetos; importar recebe a lista JSON pelo fluxo existente. Ícones são da fonte Lucide oficial já incorporada. Compartilhar não ocupa mais o rodapé.

Ao lado do seletor de preset ficam disquete, X e lixeira. O disquete salva nome e seleção; X descarta as alterações do preset; lixeira exclui somente o preset pessoal. Nenhuma dessas duas últimas ações desinstala mods. Alterar somente o nome preserva seus membros; filtros não retiram os itens marcados que estejam escondidos. Presets padrão do Cyberpunk têm nome e exclusão protegidos; salvar uma seleção personalizada cria um preset pessoal.

As duas telas usam a frase aprovada: “Criado por Nask e Codex. Se achou ruim, faz você.” Em inglês: “Made by Nask and Codex. If you don't like it, make your own.” O rodapé centraliza verticalmente Desinstalar selecionados e Baixar/Instalar, mantendo o destaque maior do segundo.

## Correções de renderização

O ícone à direita de atualizar expande somente a lista dentro da janela: oculta cabeçalho, caminho, seleção de preset, resumo, barras e rodapé. Título, filtros da tabela, rolagem e itens continuam disponíveis. O ícone muda para restaurar; outro clique recupera a disposição anterior. Não altera o tamanho da janela, os filtros, o preset, os itens marcados ou os arquivos do jogo. É um estado temporário de visualização, não uma mudança salva no projeto. Os ícones maximize-2/minimize-2 são Lucide oficiais já incorporados.

- A região inicial é preparada antes da primeira exposição. A animação não mostra primeiro o cartão inteiro.
- Fechar durante a abertura contrai a partir do estado atual. Eventos atrasados Shown/Opened não reiniciam a abertura depois do cancelamento.
- Menus de navegação são modeless, permitindo o segundo clique no botão da janela principal. Só uma confirmação explícita executa a ação.
- Pop-ups e seletores compartilham posicionamento: recuo interno de24px quando couberem, ou afastamento externo claro de24px se houver espaço; fallback com margem de tela de16px.
- Bordas compartilham pintura opaca de2px, com limpeza dos cantos e geometria consistente. Não foram usadas imagens recortadas da skill.
- Menus usam linhas de altura fixa e uma linha flexível vazia, evitando que a última ação estique.
- Trocar diretamente entre projetos não reconstrói nem exibe a Home por um instante. A origem só é escondida quando a janela de destino pode ser pintada.

As animações solicitadas foram mantidas. As regras de design ficam no [registro de correções](Design/Registro-de-correcoes.md), para um relatório futuro da skill; nenhuma alteração global dessa skill foi feita nesta revisão.

## Validação e limites

Suíte final: **1.394 verificações em22relatórios**, todas aprovadas em fixtures isoladas. Inclui73checks do host de menus,80de estabilidade/posição/pintura,79de menus/diálogos,44de rascunho/alinhamento,57da Home e55do cadastro de projetos, além das regressões da fila, status, importação, remoção, P2P e snapshots. Evidência consolidada: [Validacoes/1.1.8.json](Validacoes/1.1.8.json).

Falhas intermediárias preservadas no histórico: última linha do menu esticava o botão Opções, corrigida com linha flexível; cancelar imediatamente após abrir podia reiniciar a animação por evento Shown atrasado, corrigido com guarda no evento. Os testes finais exigem a altura igual e conclusão do fechamento rápido.

Inspeção das imagens nativas confirmou Home, menus PT/EN em três temas e editor/rodapé. Os testes verificam regiões e primeiros quadros, não apenas o cartão já aberto. Não foi feita instalação no jogo real, alteração de saves, migração de biblioteca pessoal, gameplay ou transferência entre dois PCs nesta revisão. A confirmação final de aparência no monitor do usuário permanece uma verificação de uso, distinta dos testes automatizados.

Catálogo preservado:138receitas,135linhas visíveis e8padrões; SHA256 `89c68e6bdb8c192977fcace5d30a4b8b25912d8780fd6067f77f5c7d41c6dc05`. RED4ext1.31.0 preservado. O executável único continua sem mods, saves ou credenciais.
