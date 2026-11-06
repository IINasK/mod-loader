# Projetos e pesquisa — 1.1.12

## Navegação

A tela inicial continua sendo a entrada do programa. O cabeçalho do editor passa a ter somente o menu de três riscos; o seletor separado Projetos foi retirado, inclusive seu componente antigo.

O menu usa Novo projeto, Abrir projeto, Exportar projeto e Importar projeto, nos dois idiomas. Novo projeto usa a criação da Home, em vez de substituir o conteúdo da tabela aberta. Abrir projeto e Voltar ao menu principal levam à seleção inicial. O projeto atual permanece salvo.

Num projeto fornecido pelo programa, o menu contém somente Novo projeto, Abrir projeto, Voltar ao menu principal e Opções. Importar, exportar e compartilhar continuam disponíveis nos projetos personalizados. O despacho também recusa importação/exportação/exclusão do projeto padrão. As opções de instalar e selecionar variantes, além dos presets pessoais já existentes, permanecem como antes; a revisão não reinicializa bibliotecas nem altera os arquivos instalados.

## Pesquisa

O campo aparece entre o título do preset e os cabeçalhos Mods/Variante/Situação, inclusive na lista expandida. Um X limpa a consulta; Enter aplica imediatamente e Escape limpa. A atualização ao digitar espera 150ms para evitar reconstruir a tabela a cada tecla.

Combina nome, variante selecionada e situação apresentada no idioma atual. Ignora acentos e maiúsculas, aceita trechos e pequenos erros de digitação. Todos os termos devem corresponder a algum campo do mesmo item. Números ficam literais; consultas de menos de quatro letras não recebem aproximação. Até sete letras, tolera uma edição; de oito a32, duas. Troca de letras adjacentes também é aceita. Não reordena as categorias.

A política foi adaptada da busca local de clientes do G3 ERP, consultada somente para leitura. Não leva conectores/endereço/relevância específicos daquele domínio. Usa a biblioteca padrão do .NET, sem serviço externo ou nova dependência. A pesquisa se combina com filtros das colunas e membros do preset; não elimina marcações de itens que ficaram invisíveis. Pesquisa não altera nomes, fontes, categorias nem membros de presets.

## Seleção da Home

GeneralStyle.PaintChoice limpa a superfície e pinta a seleção arredondada com recuo. Home e seletor compartilham o desenho. Corrige o FillRectangle que deixava hover/seleção quadrados dentro da moldura curva. A origem histórica da escolha incorreta é desconhecida; não se atribuiu a causa à skill.

O relato privado foi salvo em docs/relatos-g3-design/2026-11-05-mod-loader-selecao-retangular.md porque a caixa central recusou a gravação. Não foi publicado no Git e a skill central não foi editada.

## Verificação

A validação em Validacoes/1.1.12.json registra a compilação, os relatórios e os limites. Testes nativos exercitam o retorno pelo botão real à Home, os menus personalizados/padrão, pesquisa composta e pixels do DrawItem da primeira/meio/última linha em três temas. Capturas foram conferidas. Fixtures preservam a guarda contra jogo/navegador reais.

Falhas intermediárias foram do harness: o primeiro assert confundia alterações de preferências/preset com mutação do conteúdo pesquisado; outro encerrava o message loop antes de terminar seu await. Foram corrigidos separando as verificações e exercitando o retorno real pela Home. Os testes de pesquisa e os testes de fila/recibos/P2P já existentes continuaram ativos.

Catálogo138registros/135visíveis/8configurações padrão, RED4ext1.31.0 e motor de instalação preservados. Não houve instalação no jogo, teste em outro PC, alteração de saves ou nova transferência pela internet. A retomada P2P entre dois processos já havia sido ensaiada em1.1.11; esta revisão repete os testes locais pertinentes, sem alegar novo teste entre computadores.
