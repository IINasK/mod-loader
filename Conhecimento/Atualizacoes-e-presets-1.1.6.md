# Mod Loader 1.1.6 — presets e atualizações

## Produto e distribuição

O nome do projeto é Mod Loader. Repositório público: https://github.com/IINasK/mod-loader . Cyberpunk2077 é um pacote incluído, sem limitar o gerenciador a esse jogo. Os nomes internos legados de arquivos/classes não foram reescritos, para preservar recibos, caminhos, fila e migração. Novos jogos usam listas/destinos próprios; não recebem compatibilidade, descoberta de instalação ou receitas inventadas.

Douglas autorizou tornar público o repositório existente e distribuir o EXE pelas Releases. Antes da mudança foram examinados26commits/561blobs: sem indícios de credenciais reais nos padrões examinados e sem arquivos de mods, saves, EXEs/ZIPs/DLLs rastreados. Há caminhos locais com nome de usuário e histórico operacional nas fichas antigas, informados antes da publicação. Isso não é garantia de auditoria infalível. O endereço antigo é histórico; o remoto atual usa mod-loader.

## Atualizações do aplicativo

- GeneralUpdateChecker.CurrentVersion centraliza a versão da janela e da consulta; empacotar.ps1 usa a mesma versão de entrega.
- Uma consulta assíncrona por janela principal/sessão. Trocar tema, abrir menus e atualizar a lista não repetem a consulta. Fixtures não usam rede.
- API fixa HTTPS https://api.github.com/repos/IINasK/mod-loader/releases/latest , sem token, cookies, credenciais do Windows ou dados da lista. User-Agent ModLoader/versão, Accept application/vnd.github+json, API2026-03-10, TLS1.2, redirecionamento recusado, timeouts configurados de8s e limite de256KiB inclusive sem Content-Length.
- Versões numéricas de três componentes; v opcional. Sem aviso para versão igual/anterior, draft, prerelease, tag inválida ou publicação sem EXE completo. O arquivo deve ser Mod Loader X.Y.Z.exe ou Mod-Loader-X.Y.Z.exe, estado uploaded, tamanho positivo até256MiB e URL exata desta Release neste repositório.
- Aviso clicável junto ao contador abre a Release no navegador padrão, mediante clique. Não baixa/executa/substitui EXE automaticamente, nem toca no jogo. A atualização dos mods é um fluxo separado.
- Offline,404,rate-limit,JSON inválido e falhas de rede são ignorados nesta abertura, sem bloquear a interface ou disparar uma fila de tentativas. Na próxima abertura há nova consulta.
- Para próximas versões: incrementar a versão no código e no empacotamento, compilar/testar, fazer commit main e publicar Release estável vX.Y.Z com EXE e notas. A versão anterior passa a avisar na próxima abertura. Não incluir mods, saves, dados de contas ou caches nos assets.

## Categorias e tabela

Presets guardam ModIds e Categories opcionais no schema1. A tabela mostra as categorias usadas pelos membros do preset e as categorias vazias criadas explicitamente nele; não mostra sugestões vazias de outro pacote/preset. O grupo neutro Sem categoria permanece como ponto de entrada para adicionar itens. Todos os itens ainda permite acessar a organização global. Listas novas continuam vazias.

Criar categoria com nome que já existe permite associá-la ao preset, sem duplicar nem sobrescrever a descrição antiga. Renomear/excluir ajusta referências dos presets. Limpar preset remove seus membros e referências a categorias vazias, sem desinstalar arquivos. Editores e filtro de categoria usam os grupos do preset. Os campos novos persistem e acompanham exportar/importar; JSON antigo migra com Categories vazio. Referências a grupos inexistentes/repetidos são rejeitadas.

Uma linha nativa de1px, GeneralStyle.Line, separa título e cabeçalhos, com recuo16px dos lados. É mais contrastante que SoftLine das subcategorias em claro/escuro. Superfícies continuam desenhadas em código; sem imagens ou dependências novas.

A barra própria também verifica se a altura total das linhas cabe no espaço disponível: trocar uma tabela grande por um preset vazio não mantém a barra visível por uma contagem de linhas desatualizada do controle nativo.

## Logo do projeto

Douglas forneceu a imagem do monograma ML e pediu recriação vetorial. Assets/ModLoader.svg é a fonte única: viewBox2112x2016, fundo#242B30 e12polígonos, com vermelho#FF414E, branco#FFFFFF e detalhes#354049. Sem JPEG/PNG embutido, fontes, scripts ou recursos externos. O SVG tem1248bytes. A imagem JPG original permanece fora do projeto e do Git.

preparar-logo.ps1 usa XML seguro e System.Drawing nativo para gerar Assets/ModLoader.ico com quadros16/24/32/48/64/128/256 e um preview local. Só o SVG e o gerador são versionados; o ICO é derivado na compilação e incorporado no EXE. GeneralBrand desenha os mesmos polígonos em cada tema, sem dependência SVG externa, e define o ícone da janela. compilar.ps1 também usa /win32icon para Explorer/taskbar. Nome/produto/versão nativa do EXE são Mod Loader1.1.6.0; metadados e aviso usam a mesma constante de versão. README exibe o vetor original.

GeneralBrandTests confere ausência de imagem raster/external no vetor, cores e fundo em sete tamanhos, quadros ICO e ícone nativo do executável. O primeiro ensaio detectou área sem pintura na margem superior por diferença de proporção; GeneralBrand.Draw foi corrigido para pintar o retângulo inteiro antes de preservar a proporção. Novo ensaio geral após a correção. A mudança cabe na mesma1.1.6, pois ela ainda não tinha Release publicada quando o usuário pediu a logo.

O construtor antigo System.Drawing.Icon seleciona128px quando solicitado256px neste Framework; o quadro real256px foi validado pelo diretório ICO e pelo PNG decodificado, sem mudar o arquivo correto. Os demais seis tamanhos foram lidos pelo construtor nativo, e o ícone do EXE pelo Windows. Suíte final:1016verificações/17relatórios, com código e recurso de logo já integrados.

## Fontes oficiais consultadas

- https://docs.github.com/en/rest/releases/releases#get-the-latest-release
- https://cli.github.com/manual/gh_repo_rename
- https://cli.github.com/manual/gh_repo_edit
- https://cli.github.com/manual/gh_release_create

## Validação

GeneralUpdatesTests cobre comparação de versões, releases incompletas/prévias, origem do executável, tamanho, resposta inválida, cache da consulta e indisponibilidade. GeneralPresetIsolationTests usa o fluxo real de criar preset, persistência/exportação, categorias, exclusão/renomeação, clique do aviso e pixels do separador nos dois temas. A suíte geral existente também é executada; resultados e hashes em Validacoes/1.1.6.json. Nenhuma instalação no jogo real é necessária.

A primeira tentativa de --self-test-general no EXE da raiz não encontrou FixtureGame.exe: esse auxiliar só existe na árvore de compilação e não acompanha o programa distribuído. O teste foi repetido pela cópia de bytes idênticos em Programa, com a estrutura de fixtures correta. Não é falha da execução normal do aplicativo.
