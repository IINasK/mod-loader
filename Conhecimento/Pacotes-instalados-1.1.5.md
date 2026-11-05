# Pacote a partir dos mods instalados — 1.1.5

## Comportamento atual

Compartilhar P2P > Exportar mods instalados prepara **um único pack.zip temporário** a partir dos arquivos presentes na pasta de instalação, usando o preset e a variante em uso. Não precisa dos ZIP/RAR/7z originais nem do cache de downloads. O ZIP contém manifest.json e blobs deduplicados por SHA256; os caminhos de destino e proprietários de cada arquivo ficam no manifesto.

O remetente cria o convite; o destinatário confirma o dispositivo e a pasta local de instalação, autorizando instalar automaticamente após receber/conferir, e devolve a resposta. O remetente autoriza a resposta. A recepção e instalação continuam em segundo plano enquanto o Mod Loader estiver aberto, mesmo fechando o diálogo. A interface oferece pausa na transferência; uma cópia transacional em andamento precisa terminar antes de fechar o aplicativo.

Um ZIP único precisa chegar completo e passar pela conferência antes de instalar. Não se instalam pedaços incompletos. Depois da instalação, o ZIP recebido é excluído. O remetente também remove seu ZIP quando o auxiliar confirma a transferência completa. O compartilhamento é retirado do Syncthing antes de apagar os temporários, para a exclusão local não apagar a cópia em uso do outro computador.

Não há cópia do arquivo compactado em DownloadCache ou P2P/received neste fluxo. A preparação descompactada fica temporariamente no disco do jogo e é limpa em finally após sucesso ou falha. No AppData ficam somente o auxiliar, seu banco/logs limitados, sessões, metadados/recibos e backups necessários de configurações substituídas. ZIPs com falha permanecem temporários para Tentar instalar novamente ou Encerrar e liberar espaço; não são promovidos a uma biblioteca permanente de arquivos.

## Como identifica os arquivos

- Mods conhecidos: caminhos e hashes do catálogo, incluindo dependências e a densidade Night City Alive realmente instalada. O campo escolhido antigo não pode exportar uma densidade diferente da que está no jogo.
- Personalizados no Cyberpunk: recibos protegidos em .CoopModsOnline/importados e as associações locais persistidas. O download original pode ter sido apagado.
- Outros jogos: recibos do GeneralModInstaller, vinculados ao destino e à fonte cadastrada; arquivos preexistentes idênticos necessários ao mod também são incluídos.
- Pacotes recebidos anteriormente: recibo do snapshot e os arquivos atuais instalados, permitindo reexportar depois que o ZIP temporário foi removido.

Não faz varredura recursiva de toda a pasta do jogo. Um mod sem caminhos/recibos confiáveis, parcialmente instalado ou com código divergente é identificado por nome/caminho e impede exportar aquele preset até resolver a divergência. Não adivinha os arquivos e não inclui silenciosamente o jogo inteiro. Os cinco arquivos imutáveis NightCoop/RED4ext continuam exatamente fixados; nenhuma atualização do RED4ext é feita.

## Configurações

A opção Incluir configurações seguras dos mods vem marcada. Inclui somente JSON UserData conhecido e os caminhos/seções registrados em ModDefaults: seções específicas de mod_settings/user.ini, Shift/userConfig.json e NPCD_Hotline/mod_user_settings.json. As configurações atuais são lidas, não somente os padrões embutidos.

O INI global não é enviado inteiro: somente as seções dos mods selecionados. No receptor, essas seções são mescladas preservando outras seções, comentários e dados locais. JSON permitido passa por validação de conteúdo; limites de tamanho/profundidade e bloqueios de tokens, credenciais e caminhos privados continuam ativos. Configurações personalizadas JSON/INI/TOML também são inspecionadas antes do envio/uso. Não é uma promessa de descobrir automaticamente todo formato de configuração de qualquer mod.

Alterações normais nas configurações permitidas não tornam o código do mod corrompido no status. Reexportar um snapshot lê os ajustes atuais. A remoção preserva edições posteriores e arquivos compartilhados/preexistentes. Backups de ajustes substituídos ficam locais, nunca dentro do pacote enviado.

## Instalação e persistência

InstalledSnapshotInstaller usa a mesma validação de caminhos/links, formato de recibo protegido por conta Windows e padrões transacionais do projeto, com operação própria para o snapshot. O fluxo antigo de importação de arquivo genérico não serve para um pacote misto de arquivos conhecidos e personalizados, porque corretamente recusa destinos reservados do catálogo.

Antes de copiar: versão do jogo/DLC/dependências, base imutável, espaço disponível, hashes/tamanhos e todos os conflitos são conferidos. O jogo precisa estar fechado para modificar Cyberpunk. Código existente diferente é preservado e bloqueia a instalação; somente configurações permitidas podem ser substituídas com backup. Cópias são preparadas, conferidas e aplicadas com rollback; alterações concorrentes posteriores são preservadas. ZIP/entradas grandes são processados em fluxo, sem carregar todos os mods em memória.

O recibo contém manifesto, caminhos, hashes, proprietários e alterações, independentemente do ZIP. SnapshotReceipts associa cada mod/variante ao recibo local; a lista é persistida normalmente. Fontes personalizadas usam o marcador interno installed://, sem caminhos do remetente e sem abrir um endereço fictício no navegador. Exportar/importar somente a lista não substitui transferir o pacote.

Ao terminar com o diálogo aberto, a lista recebida pode ser carregada e passa a mostrar os mods instalados pelo recibo. Se o diálogo foi fechado, a instalação ainda ocorre; o pacote concluído pode ser selecionado depois para Carregar lista. A troca da lista permanece confirmável e preserva instalações existentes. Reinstalar um personalizado removido pode exigir novo envio, justamente porque o arquivo compactado não ficou guardado.

O protocolo de1.1.4 continua aceito por compatibilidade e preserva sua conferência de originais/cache. A exportação principal de1.1.5 usa o novo formato installed-zip, com tamanho/hash no convite e somente pack.zip liberado no transporte.

## Limites e falhas dos ensaios

O pacote continua condicionado à permissão de redistribuição dos mods escolhidos. Saves, contas, tokens, bibliotecas completas do jogo, recibos, backups e perfis privados não são exportados. Esta implementação não instalou nada no jogo real nem enviou arquivos pessoais.

Foram exercitados exportação com download original apagado, identidade/caminhos/duplicatas/arquivos extras, dependências, base fixa, configurações e dados privados, backup/rollback, remoção/propriedade compartilhada, reexportação, interface pt/en claro/escuro e dois auxiliares reais em loopback. A conexão entre computadores em redes diferentes continua dependendo do teste real.

Falhas corrigidas antes da entrega:

- O teste visual antigo não verificava o texto da nova introdução/checkbox e deixou recorte passar; altura corrigida e asserts próprios incluídos.
- O Syncthing pode manter a raiz de uma pasta parada aberta no Windows, inclusive após retirar a configuração. A limpeza tenta novamente; se só restam sessões ociosas/concluídas, reinicia somente o auxiliar pertencente ao app para liberar a raiz. Nunca reinicia outro Syncthing nem interrompe outras transferências ativas. Os ensaios preservam os resultados das tentativas que falharam.
- Staging extraído foi colocado no disco de destino, com conferência de espaço, evitando preencher AppData durante a instalação.
- Status foi corrigido para permitir ajustes das configurações sem acusar corrupção de código; hashes de código continuam obrigatórios.
- Nomes locais repetidos no novo ramo de progresso impediram uma compilação intermediária; corrigidos antes dos testes finais.

Resultados finais: Validacoes/1.1.5.json. Fontes do motor P2P e da dependência continuam documentadas em Compartilhamento-P2P-1.1.4.md. O uso de ZIP aproveita System.IO.Compression já presente, sem acrescentar compressor ou dependência global.
