# Escolha de versões — 1.1.27

O Mod Loader pede uma decisão antes de substituir uma versão/conteúdo diferente encontrado no destino. O aviso informa o nome do mod, a versão do binário quando disponível e a versão/arquivo solicitado. Não inferir números de versão a partir de um hash diferente: arquivos sem metadados aparecem como conteúdo diferente ou versão não identificada.

- **Manter atual:** preserva os arquivos e adia o item. Outros itens da fila continuam. Em recebimento P2P, adia o pacote inteiro antes da cópia e conserva o ZIP para nova tentativa; não registra uma instalação parcial como concluída.
- **Usar do pacote:** permite a troca suportada após confirmação, com conferência do destino e backup/reversão. A escolha vale somente para aquele conflito; ocultar avisos de falha da fila não suprime decisões de versão.

A fila verifica também as dependências conhecidas e a troca de densidade do Night City Alive. Código desconhecido/incompatível e a base imutável NightCoop/RED4ext continuam protegidos. Uma confirmação não afrouxa hashes, caminhos, dependências nem a receita oficial do pacote.

Packs recebidos agrupam vários arquivos conflitantes do mesmo mod em uma decisão. Antes de gravar, todos os destinos são preparados e conferidos. Trocas consentidas mantêm backups no registro de instalação; a remoção pode restaurar o arquivo anterior se o destino e o backup continuarem íntegros. Arquivos modificados durante a decisão são preservados e interrompem a operação.

Importados genéricos passam a registrar somente o SHA-256 do ZIP/RAR/7z de origem, além dos caminhos/hashes já existentes. Um arquivo novo no mesmo caminho não é confundido com a versão já instalada. Recibos antigos continuam legíveis; quando o hash da origem estiver ausente, a instalação confere o conteúdo em vez de inventar uma correspondência. Não há cópia permanente do pacote original.

Limites: uma estrutura de arquivos diferente em instalação registrada exige remover a versão anterior primeiro, evitando misturar código antigo e novo. Imports personalizados que colidem com arquivos do Cyberpunk continuam exigindo remoção explícita da instalação anterior; o botão de substituição fica indisponível e o motivo é exibido. Arquivos adicionais sem registro de autoria não são apagados por dedução. Um pack de outra receita/catálogo continua sujeito à validação antes da escolha.

A atualização automática troca o aplicativo; mudanças nos mods continuam dependendo dessas decisões. Nenhum mod foi alterado no jogo real durante a implementação. Testes de fila, snapshots, P2P local de despacho, temas/idiomas, cache sem cópias, atualizador e Setup constam em Validacoes/1.1.27.json. Não houve novo teste de transferência entre dois PCs nem inspeção direta do computador do amigo.