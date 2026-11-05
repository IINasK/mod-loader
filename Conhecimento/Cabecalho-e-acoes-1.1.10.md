# Cabeçalho da tabela e ações — 1.1.10

Revisão de05/11/2026 sobre1.1.8 e a logo local1.1.9 já aprovada. Preserva a logo vetorial nova, as cores por tema, o catálogo e o motor existente.

## Alterações

O preset é escolhido pela seta no próprio cabeçalho da tabela. Não existe mais uma linha Preset separada acima dela. O nome continua editável quando o preset permite; disquete salva, X descarta o rascunho e lixeira exclui apenas o preset. Atualizar e expandir continuam à direita. A visualização expandida mantém o título e suas ações, ocultando somente os controles externos.

O seletor reutiliza GeneralPicker, em modo somente ícone. Mantém a lista, Enter, setas, Escape, foco e nome acessível. A lista aberta conserva a largura legível; o tamanho do botão não limita o tamanho do menu. Campos de Opções e os outros consumidores mantêm a apresentação anterior.

Desinstalar selecionados usa o mesmo botão preenchido, altura52px, raio e fonte13negrito de Baixar/Instalar, com preenchimento vermelho e texto branco. A largura se ajusta ao texto; não reduz o rótulo em inglês para forçar uma largura fixa. O comportamento, confirmações e motor de desinstalação permanecem os anteriores.

O divisor do menu usa margens verticais simétricas nas ações vizinhas. A linha fica equidistante dos limites pintados de Importar lista e Compartilhar; centralizar apenas na linha reservada não compensava a margem inferior antiga.

## Aprendizados da G3 Design

Atualizadas as referências de ícones/componentes e geometria da skill portátil: preferir ícones reconhecíveis nas ações de navegação; combinar ícone e texto nos menus; controles compactos sem texto conservam foco, dica e nome acessível. Divisores são medidos em relação aos elementos visíveis. As outras alterações já existentes no repositório G3 foram preservadas e não foram publicadas por esta tarefa.

O registro [Design/Registro-de-correcoes.md](Design/Registro-de-correcoes.md) conserva também o defeito anterior de seleção retangular dentro da moldura arredondada. Esta revisão não corrige esse outro defeito.

## Validação

Suíte geral final em fixtures: **1.431 verificações em22relatórios**, todas aprovadas. Inclui48checks de preset/rascunho/layout,85de menus e36de pintura de botões. Conferidos menus PT/EN nos três temas, cabeçalho integrado, modo expandido, teclado do seletor e alinhamento das ações. Imagens nativas do editor e menu foram inspecionadas.

Uma rodada intermediária parou no teste de atualização: a versão futura simulada1.1.10 passou a ser a versão atual, e o teste acessava um resultado nulo. O teste agora constrói a próxima revisão com base na versão real do executável, sem mudar o comportamento do verificador. A suíte inteira passou depois da correção; não confundir isso com falha de atualização do usuário.

Fonte e pacote são incrementais. Nenhum mod instalado, jogo iniciado, save alterado ou projeto pessoal migrado nestes testes. Não foi repetida transferência P2P entre PCs. O executável não inclui mods ou credenciais. Catálogo138/135/8 e RED4ext1.31.0 preservados.
