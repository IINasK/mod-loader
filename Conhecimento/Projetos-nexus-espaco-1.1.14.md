# Projetos, fontes e espaço — 1.1.14

## Navegação

O cabeçalho tem Projetos, Voltar ao menu principal (ícone de casa) e o menu de três riscos, nesta ordem. Voltar sai do projeto para a Home; não fica mais dentro do menu. Projetos abre uma versão compacta da Home: criar/importar, projetos pessoais e, por último, o Cyberpunk pronto. Repetir o clique fecha o painel. Abrir o projeto atual não o reinicializa. Criar/importar desse painel cria outro projeto; não transforma nem substitui o projeto padrão.

Os menus internos mantêm a proteção dos projetos fornecidos pelo aplicativo. O menu do Cyberpunk não oferece exportar/compartilhar/alterar a lista padrão. O painel Projetos pode importar um novo projeto pessoal mesmo quando o usuário está no padrão.

## Importar uma fonte

Um ZIP/RAR/7z continua abrindo o editor ao ser arrastado. Um link HTTPS pode ser arrastado para o editor do projeto ou colado com Ctrl+V fora de campos de texto. Também pode ser escrito no campo de fontes do próprio mod. Links não causam instalação só por serem colados: o usuário revisa e escolhe Salvar ou Instalar.

Para o Nexus, a orientação é: escolha a versão desejada em Files e copie o endereço da tela com Slow download e Premium. O endereço identifica o arquivo por file_id. A página geral também é aceita; apenas mostra uma dica e pode não informar qual arquivo baixar.

A consulta tenta preencher o nome do mod, o rótulo de arquivo publicado e seu tamanho em bytes. O catálogo fixo fornece os dados já conhecidos sem rede. Nos demais casos, a consulta é pública, anônima e limitada: sem senha, chave de API, cookies ou sessão do navegador. Uma indisponibilidade ou bloqueio do Nexus deixa o preenchimento manual disponível. Não há tentativa de contornar Cloudflare.

A página exata 34305/169285 foi conferida no navegador integrado: o widget público mod-file-download informa filename=SpotOnRadio, filesize-bytes=4317184 e file-id=169285. Esse filename é um rótulo publicado; o nome físico do ZIP pode acrescentar versão e identificadores. A consulta anônima do executável recebeu HTTP403 neste ambiente. Portanto o preenchimento de fontes novas não é garantido quando o site bloqueia acesso externo. Nome digitado pelo usuário é preservado; uma resposta atrasada não altera o mod depois de trocar a fonte/variante.

Esses metadados acompanham salvar, reabrir e exportar projeto, mas são informativos. Não equivalem a hash aprovado, download concluído ou instalação. A conferência de conteúdo, o monitor e os recibos continuam independentes. O pacote dos instalados remove caminhos locais dos metadados antes do compartilhamento.

Fontes públicas: [arquivo Nexus usado na conferência](https://www.nexusmods.com/cyberpunk2077/mods/34305?tab=files&file_id=169285), [política da API](https://help.nexusmods.com/article/114-api-acceptable-use-policy). Não foi adicionado campo de chave/API/Premium.

## Espaço conhecido

Dentro do projeto, Mods soma os arquivos instalados conhecidos pelos recibos e pelas receitas do catálogo. Total do projeto acrescenta os downloads referenciados ainda existentes e seus dados locais: configurações, registros e backups. A Home e o painel Projetos mostram projetos com arquivos presentes e o total agregado, incluindo o cache próprio do aplicativo. Um arquivo de mesmo caminho físico é contado uma vez no agregado, mesmo quando usado por vários projetos.

As medidas usam o tamanho lógico em bytes, convertido em KiB/MiB/GiB. Não são uma leitura da alocação física em clusters, nem uma varredura de toda a pasta do jogo/Downloads. Cópias em caminhos diferentes contam separadamente; hard links não são deduplicados por identidade NTFS. Dados inacessíveis podem deixar a medida parcial/indisponível. O contador não cria ZIPs, não baixa arquivos, não calcula hashes dos payloads e não limpa dados automaticamente. A medição ocorre em segundo plano na abertura e depois da conferência de instalação.

A leitura de inspeção permite substituição/gravação concorrente dos registros. Uma corrida detectada durante a medida pode deixá-la parcial, mas não deve bloquear salvar, instalar ou excluir. Leituras transacionais de instalação/exclusão continuam usando seu comportamento estrito anterior.

## Lixeira da Home

A primeira etapa permite excluir somente a referência do projeto no app ou também apagar seus arquivos de mods instalados. A segunda mostra a revisão final. Cancelar é a ação padrão nas duas. Não existe Desfazer para essa exclusão.

Apagar mods utiliza caminhos e recibos conhecidos, verifica hash e autoria e adquire os bloqueios da operação. Preserva arquivos sem rastreio, preexistentes, modificados depois da instalação, compartilhados com outro projeto ou dependência externa, além da base protegida do catálogo. Não apaga a pasta inteira do jogo nem os downloads originais. A revisão lista os arquivos que serão apagados e os preservados com o motivo.

A remoção apaga arquivos diretamente; não cria outro backup pesado. Um diário pequeno registra o progresso. O projeto só sai do índice depois da operação completa. Se ela falhar, permanece para revisão e nova tentativa; o que já foi apagado não tem reversão. Para recuperar arquivos apagados será preciso reinstalar; para recuperar um projeto retirado do app será preciso importar uma cópia. Projetos padrão permanecem protegidos.

## Limites de validação

Testes utilizam arquivos e destinos artificiais, sem instalação/exclusão no jogo real, alteração de saves ou atualização do RED4ext. Os resultados finais estão em Validacoes/1.1.14.json. Pastas de teste foram encurtadas dentro de T para respeitar a política de organização e o limite de caminhos do Framework. Essa adaptação não relaxa guardas de caminhos, hash ou fixture. Tentativas intermediárias e seus limites são registradas na validação.
