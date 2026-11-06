# Compartilhamento parcial e convites —1.1.19

## Interface e fluxo

Os dois campos de convite são de linha única e têm a mesma superfície48px. O ícone Lucide copy fica dentro do campo de envio, com alvo44px e recuo que preserva o contorno. Um convite comprido rola horizontalmente; copiar mantém seu conteúdo completo. Ao abrir a tela, o aplicativo confere o preset e prepara o ZIP temporário automaticamente, por escolha do usuário. O campo é preenchido quando o convite real está pronto; a preparação mostra estado/progresso indeterminado, sem número inventado. Uma sessão pendente é retomada, sem criar outro pacote. Projeto sem itens pode receber um convite sem gerar um pacote vazio.

O remetente envia esse convite pelo canal que preferir. O amigo cola no Mod Loader, escolhe o destino e usa Conectar. O app dele gera uma resposta; o remetente cola essa resposta e usa Conectar para autorizar aquele computador. Então a transferência e a instalação continuam automaticamente. O convite não é um endereço HTTPS nem um download de navegador.

## Projeto incompleto

O ZIP continua compactado. O payload contém somente os arquivos instalados do preset, conferidos por recibos e receitas. A nova seção opcional Project do manifesto preserva a definição completa: nomes personalizados, descrições, categorias, tags/NSFW, variantes, escolha atual, presets e links públicos. List continua representando somente os mods realmente presentes no payload, para não dar autoria/recibo de instalação a um mod ausente.

Um item nunca instalado pode ficar apenas nos metadados, inclusive quando nenhum item do preset está disponível. Instalação registrada com arquivo ausente/alterado continua impedindo o envio e apontando o problema. Dependências de um item instalado continuam obrigatórias. Fontes locais privadas são substituídas por identidade installed://; não inventar links de origem. Um item que só tinha arquivo local pode precisar ser importado novamente pelo amigo. Caminhos absolutos de Downloads e dados de contas não viajam na definição. O receptor pode depois usar a fila habitual para completar os links públicos faltantes. Ambos devem usar1.1.19 para preservar o projeto completo; versões antigas não conhecem Project.

Gerar modpack também usa essa seleção parcial, com caminhos relativos à raiz do jogo e definição completa em `_modloader/project.json`. O ZIP manual e o ZIP interno do P2P têm organizações diferentes: o primeiro é para extrair na raiz, e o segundo contém blobs verificados para o próprio instalador. Nenhum exige reter os arquivos ZIP originais em AppData.

## Barra inferior

A cobertura conta mods do preset ativo, ou de toda a lista se não houver preset. Não usa o número de bytes, checkboxes nem filtros/pesquisa. Verde representa itens reconhecidos como baixados/prontos ou instalados; a parte restante é dividida por categoria.17/20 aparece como85%; duas faltas de Cars ocupam10% e uma de FPV5%. Cores são geradas deterministicamente a partir das categorias completas e distanciadas para facilitar a leitura. A paleta é reaproveitada durante atualizações de progresso. Hover informa categoria, quantidade e percentual, sem novo diálogo. Não é prova de integridade da instalação: o exportador reconfere os arquivos.

Processar/pular um item não aumenta artificialmente a cobertura. A barra do item atual mantém o progresso da operação atual. No diálogo P2P,100% significa que o pacote disponibilizado foi transferido/instalado; a barra do projeto continua mostrando os itens opcionais que faltam.

## Transporte e segurança já existentes

O motor é o **Syncthing2.1.7 oficial**, executado em perfil isolado do Mod Loader. Primeiro uso baixa o auxiliar oficial e confere hashes fixados do ZIP e do executável. Não instala nem adota um perfil pessoal do Syncthing. Não foi criado outro TCP ou esquema próprio de criptografia nesta mudança.

- Conexão de dados protegida por TLS e autenticação pela identidade/certificado dos dispositivos aprovados. A troca de convite/resposta vincula o computador autorizado à sessão e ao hash/tamanho do pacote.
- BEP divide os arquivos em blocos, compara SHA256 e solicita partes faltantes. Blocos incorretos são descartados pelo Syncthing. O Mod Loader ainda confere o hash do ZIP e os arquivos declarados antes de instalar.
- Sessões, identidades, pausas e temporários permitem retomar após interrupção/reabertura. Cada lado só libera a própria pausa. A retomada reutiliza os blocos temporários que continuam disponíveis; não prometer retenção infinita: o comportamento padrão do Syncthing pode recolher parciais antigos após cerca de um dia.
- O remetente deixa de compartilhar e elimina seu ZIP depois da entrega confirmada. O receptor elimina o temporário após verificar/instalar com sucesso. Em erro, preserva os dados necessários à revisão/tentativa. Backups e recibos pequenos seguem a política anterior.
- A conexão pode ser direta ou usar relays de terceiros. O relay não lê o conteúdo criptografado. Descoberta/relay podem conhecer identificadores e IPs; isso não é anonimato.

O convite em si é metadado codificado, não conteúdo criptografado. Não inclui a chave TLS privada. A criptografia protege o tráfego, não cifra o ZIP no disco nem garante a segurança de um mod escolhido pelo usuário. Troque convites por um canal confiável e revise relatórios/configurações antes de compartilhar.

Fontes oficiais consultadas em06/11/2026: [segurança](https://docs.syncthing.net/users/security.html), [blocos, hashes e temporários](https://docs.syncthing.net/users/syncing.html), [BEP e autenticação](https://docs.syncthing.net/specs/bep-v1.html).

## Catálogo e limites desta entrega

Advanced Crash Reporter34403/1684181.5.0 foi acrescentado com26arquivos, dependendo do CET existente. Nola Stockings Collection12572 foi retirado; VOIDWEAR12637 mantém somente Accessories e seu archive compartilhado. Casual/Tacticool não entram em novas instalações. O catálogo continua com138registros/135visíveis: um item retirado e outro acrescentado. RED4ext, CET, demais frameworks e os oito defaults permanecem idênticos.

Jogo e Mod Loader estavam abertos. O usuário escolheu continuar sem os testes que dependem de fechá-los. Houve ensaios de transferência real entre dois perfis locais, interrupção/retomada, metadados parciais, geometria nos três temas/PT/EN, fila, projetos e Setup isolado. Não houve teste entre dois PCs pela internet nem novo voo/coop/crash real. A suíte geral foi interrompida pela guarda de jogo aberto e não deve ser anunciada como concluída. Nenhuma proteção de produção foi relaxada. Remoção dos cinco arquivos locais e instalação do Crash Reporter no jogo continuam pendentes de jogo fechado; não são consequência automática de instalar uma versão nova do gerenciador.
