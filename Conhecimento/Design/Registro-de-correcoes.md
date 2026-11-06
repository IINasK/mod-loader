# Registro de correções do Mod Loader

Registro solicitado por Douglas em 05/11/2026 para preparar um relatório futuro de melhoria da skill de design. Guardar as decisões e evidências do aplicativo; não alterar a skill global automaticamente a partir deste registro.

## Produto e navegação

- O produto é Mod Loader. Cyberpunk é um projeto padrão incluído, sem limitar a interface a esse jogo.
- Abrir o aplicativo deve mostrar a seleção de projetos. Não entrar automaticamente no último projeto ou no pacote Cyberpunk.
- Projetos pessoais aparecem antes dos padrões no seletor Projetos. Não usar mais o nome Pacotes, nem o parágrafo que explicava a troca de pacote.
- Um projeto pronto abre diretamente. Não pedir um nome ao clicar nem criar outra cópia a cada abertura. Nomes e definições de presets padrão ficam protegidos; personalizações são salvas em um preset pessoal.
- Não exibir a Home por um instante entre dois editores durante uma troca direta de projeto. Não esconder a origem antes de o destino poder ser exibido.

## Menus e controles

- Expandir lista deve ocultar os controles externos dentro da janela, sem maximizar o aplicativo. Manter ícone de restaurar no próprio cabeçalho da lista, preservar seleção/filtros/preset e não gravar esse estado na biblioteca.

- Menus de navegação devem poder abrir e fechar pelo mesmo botão. Um diálogo modal bloqueia esse segundo clique e não deve ser usado para esse tipo de menu.
- Menus de navegação não têm X no cabeçalho. Cancelar por segundo clique, clique fora, Escape ou desativação não pode executar a ação selecionada. Formulários de edição/confirmar continuam tendo seus próprios meios de cancelar.
- O menu de três riscos tem uma coluna: Nova lista, Abrir lista, Exportar lista, Importar lista; depois Compartilhar e Opções. Cada item leva um ícone oficial.
- Salvar/atualizar preset fica ao lado do seletor de preset, sob o endereço da pasta: disquete, X de descartar e lixeira de excluir, nessa ordem. Não duplicar essas ações no menu nem deixar a lixeira antiga no cabeçalho da tabela.
- Digitar o nome não deve salvar automaticamente. Disquete confirma nome/seleção; X volta ao último estado salvo. Renomear sem alterar seleção preserva os membros. Filtros não podem apagar membros marcados escondidos.
- Excluir preset não desinstala mods nem apaga a lista de mods. Projeto, lista e preset têm papéis diferentes; seus rótulos não são intercambiáveis.
- Na Home, Criar novo projeto e Importar projeto ficam lado a lado e mais finos, com altura de controle normal. Evitar grandes blocos desnecessários.
- Projetos salvos recebem destaque ao passar o mouse. Passar o mouse não abre nem confirma um item.

## Abertura, posição e pintura

- Falha reportada por Douglas após1.1.8: a moldura era arredondada, mas o preenchimento da seleção interna continuava retangular. Evidência: codex-clipboard-c109694a-c1ed-4514-8520-7b86d784c39f.png. A imagem comprova a inconsistência visual, não identifica sozinha a rotina de pintura responsável.
- A seleção/hover interna deve ter raio próprio coerente com o recuo e a geometria interna. Arredondar apenas a moldura ou aplicar recorte no limite externo não resolve a quina de um destaque recuado. Preservar borda, margem, texto, foco e área clicável; usar a pintura compartilhada. Conferir primeiro, intermediário e último item, nos estados hover/selecionado/foco, após scroll e resize.
- Aprendizado incorporado às referências Geometria e Auditoria visual da skill G3 Design. Esta rodada registra a regra; não altera o executável nem comprova a correção da interface do Mod Loader.

- Não mostrar o cartão inteiro antes de começar a animação de abertura. Preparar o estado inicial antes da primeira exposição; iniciar a expansão depois. Considerar também menus nativos hospedando controles.
- Pop-ups internos mantêm uma folga clara das bordas da janela dona. Se não couberem, devem ficar claramente fora dela com folga, quando houver espaço na tela. Evitar bordas quase coincidentes ou uma sobreposição de poucos pixels.
- Usar a mesma regra de posicionamento para formulários ancorados e listas dos seletores. Testar sobretudo o seletor Idioma na borda direita de Opções.
- Bordas devem ter espessura opaca suficiente para renderizar em tamanhos pequenos. Evitar contornos muito finos, regiões incompatíveis e restos de cor nos cantos.
- Verificar os limites reais da janela, não apenas os do pai imediato: um painel maior que a janela pode fazer testes locais passarem apesar de cortar botões.
- A última linha de um TableLayoutPanel pode absorver espaço restante e mudar a altura de um botão. Reservar uma linha flexível, mantendo ações com a mesma altura.
- Rolagem deve usar os controles próprios do aplicativo, sem setas nativas sobre cantos arredondados.

## Identidade e texto

- A logo é um vetor geométrico com fundo transparente. Não vetorizar o fundo sólido da imagem de referência junto com a marca.
- A logo no aplicativo acompanha as cores do tema; o ícone do Windows mantém vermelho e branco e transparência real.
- Manter Claro — vermelho, Claro — violeta e Escuro. Preservar preferências existentes ao atualizar.
- Ícones comuns vêm de uma biblioteca oficial, sem recortar imagens de referência nem desenhar símbolos improvisados.
- Português e inglês devem ser diretos e próximos em significado, sem expressões forçadas. Frase aprovada na Home e no editor: “Criado por Nask e Codex. Se achou ruim, faz você.” / “Made by Nask and Codex. If you don't like it, make your own.”

## Evidências para o relatório

- Registrar a versão, arquivos alterados, teste que detectou cada falha, correção e imagem depois da correção.
- Executar formulários reais em fixtures, com PT/EN, três temas, largura mínima e seletores próximos das bordas. Conferir primeiro quadro de animação, não só a imagem final.
- Verificar alpha do SVG/ICO, pixels das bordas, posições na tela, segundo clique do botão e ausência de efeitos ao cancelar.
- Preservar o histórico de testes que falharam e distinguir teste de interface, rede local e gameplay. Não afirmar teste no jogo quando houve apenas validação em fixtures.
- Resultados por versão ficam em Conhecimento/Validacoes. As imagens locais de teste são evidências, não assets que devem ser copiadas para a interface.

Última revisão concluída: 1.1.8. Validação: 1.394 verificações em22relatórios, com limites e falhas intermediárias em ../Menus-e-estabilidade-1.1.8.md e ../Validacoes/1.1.8.json.

- Rodapé: centralizar verticalmente os botões de alturas diferentes; não alinhar só o topo ou a base. Compartilhar fica no menu, sem duplicar o botão inferior.

## Cabeçalho e navegação — 1.1.10

- O seletor de preset e disquete/X/lixeira ficam no cabeçalho da tabela, junto ao nome editável. Remover o campo Preset duplicado acima. Manter essas ações ao expandir somente a lista.
- Desinstalar selecionados segue preenchimento, altura, raio e fonte de Baixar/Instalar, com cor destrutiva própria e contraste legível.
- Preferir ícones oficiais reconhecíveis para navegação; usar texto nos menus e nome acessível nos botões compactos. Incorporado à referência de ícones da G3 Design.
- Divisor entre grupos deve ter respiro simétrico entre os limites pintados dos itens, incluindo suas margens. Margem só embaixo deslocava o centro percebido. Corrigido com margens verticais iguais e medido em seis combinações de idioma/tema. Regra incorporada à geometria da G3 Design.
- 1.431 verificações/22relatórios aprovados em fixtures; imagens de cabeçalho e menu conferidas. O defeito anterior da seleção retangular continua apenas registrado, sem correção neste executável.


## Revisão1.1.12 — decisões atuais

- O seletor Projetos foi retirado: navegação pelo menu único, com Voltar ao menu principal. Criar/abrir/importar/exportar usam Projeto nos dois idiomas. O menu de projetos padrão limita-se a novo/abrir/voltar/opções; o personalizado mantém os demais recursos.
- Pesquisa local fica entre título e colunas e continua na lista expandida. Nome/variante/status, acentos, trechos e erros pequenos compõem filtros/preset, preservando seleção oculta.
- A seleção quadrada da Home foi corrigida na pintura compartilhada com o picker. Testes de DrawItem/pixels em três temas, inclusive primeira/meio/última linha. O relato privado ficou no fallback docs/relatos-g3-design após acesso negado na caixa central. Não publicar esse relato, não modificar a skill central.
- Texto e filhos internos da pesquisa recebem cores explícitas e recuo que não cobre o contorno arredondado. Conferência renderizada do campo e menus e testes de geometria, não somente build.
