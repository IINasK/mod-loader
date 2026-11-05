# Compartilhamento P2P — Mod Loader 1.1.4

## Uso

O menu de três riscos oferece **Compartilhar P2P**. O auxiliar oficial Syncthing 2.1.7 é baixado no primeiro uso e conferido antes de executar; não exige instalação global nem substitui um Syncthing pessoal existente. Não é necessário abrir sua interface ou informar chaves de API. O EXE do Mod Loader continua sendo o único arquivo entregue.

1. O remetente escolhe um preset e abre Compartilhar P2P > Compartilhar preset atual. Sem um preset específico, considera a lista inteira. São incluídas as variantes escolhidas e as dependências conhecidas. O diálogo pede confirmação das permissões de redistribuição.
2. O remetente copia o convite e envia ao amigo por um canal de sua escolha. O aplicativo não envia mensagens por conta própria.
3. O amigo abre Compartilhar P2P, cola o convite e clica Receber convite. Confere o identificador com o remetente e devolve a resposta gerada.
4. O remetente cola a resposta e clica Autorizar resposta. Um convite fica vinculado a um amigo. Para outro amigo, criar outro compartilhamento.
5. Ambos mantêm o Mod Loader aberto. O auxiliar transfere em segundo plano mesmo depois de fechar o diálogo; fechar o aplicativo encerra somente o auxiliar pertencente a ele. Para retomar após reabrir, abrir Compartilhar P2P novamente. Convites, identidades e pausas são preservados.
6. O destinatário seleciona o pacote recebido e clica Carregar lista ou Carregar/Instalar. Os arquivos são conferidos antes de trocar a lista. A troca continua pedindo confirmação, preserva os arquivos instalados e invalida associações antigas das fontes recebidas. Carregar/Instalar passa pela confirmação de destino e pela fila/motores existentes; receber sozinho não instala nada.

Pausar/Retomar controla o pacote selecionado. Encerrar e liberar espaço remove somente sua sessão e sua cópia de transporte. Originais, cache, arquivos recebidos já preparados e instalações permanecem. Ao encerrar a última sessão de um amigo, sua associação no auxiliar é removida.

## Arquivos e espaço

- Os pacotes de transporte ficam em Documentos/Mod Loader/Pacotes P2P, em subpastas próprias identificadas por sessão. Nunca é compartilhada a pasta inteira do jogo, Downloads ou AppData.
- No AppData/Local/ModLoaderGeral/P2P ficam o auxiliar (~35 MB descompactado), sua configuração privada, banco de sincronização, registro das sessões e cópias privadas de arquivos personalizados recebidos. A configuração privada não é exportada. Os logs do auxiliar são limitados a três arquivos de1 MiB.
- Arquivos conhecidos disponíveis vêm do cache original, conferidos por tamanho/SHA. Hard links no mesmo volume evitam uma segunda cópia física do cache do remetente; quando indisponíveis, usa cópia conferida. A pasta de envio é send-only. Personalizados são copiados para preservar o snapshot se o original for editado.
- No receptor, arquivos conhecidos entram no cache pelo hash confiável do catálogo. Personalizados recebem cópia privada independente da pasta sincronizada, deduplicada pelo hash. Assim, mudanças/exclusões remotas posteriores não alteram o arquivo aprovado para instalação.
- Arquivos originais ausentes não são reconstruídos a partir do jogo instalado. O pacote mantém seus links oficiais; o destinatário pode precisar concluir esses downloads pelo fluxo existente. Arquivos personalizados locais ausentes impedem preparar o pacote até reassociar o original.
- Cada convite fixa uma versão imutável do manifesto por SHA-256. Para compartilhar alterações, preparar outro pacote e enviar o novo convite. Esta versão não atualiza silenciosamente a lista instalada dos amigos.

## Confiança e limites

O convite contém somente tipo, identidade pública do dispositivo, ID do pacote, nome e hash do manifesto. Não contém senha, chave REST, certificado privado, pasta pessoal ou destino de instalação. A resposta deve corresponder ao mesmo pacote/hash. Aprovação mútua é necessária: conhecer o convite não autoriza outro computador sozinho.

O REST do auxiliar fica somente em127.0.0.1, protegido por segredo aleatório local. Usa perfil próprio, sem navegador, atualizações automáticas, pasta Sync padrão ou aceitação automática de outras pastas. Ao iniciar, pastas são reconstruídas apenas a partir das sessões locais aprovadas, sem conservar raízes órfãs da configuração anterior. O processo é encerrado pelo identificador próprio; nenhum outro Syncthing é fechado.

Primeiro o receptor aceita apenas pack.json. O hash é calculado sobre o mesmo buffer que será desserializado. Só depois de validar nomes, tamanhos, fontes e associações são liberados os nomes exatos dos arquivos do manifesto. Não são importados temporários, extras ou links. Antes de usar, todos os arquivos finais passam por tamanho/SHA e pela cópia privada. Limites: manifesto8 MB,5000arquivos,16 GiB por arquivo,128 GiB por pacote, até100sessões. As demais limitações do schema do gerenciador continuam valendo.

URLs de receitas conhecidas não são substituídas por caminhos genéricos: o catálogo restaura suas associações somente por identidade e fontes exatas. Bytes conhecidos continuam passando pelos hashes/receitas/dependências/build/DLC e pela proteção da base RED4ext1.31.0. Personalizados usam a instalação genérica já existente, com proteção de caminhos/conflitos/recibos/remoção. Esta funcionalidade não muda requisitos ou permissões dos mods.

Progresso parcial vem dos contadores do auxiliar, não do tamanho pré-alocado de temporários. Fica abaixo de100% antes da conferência. Conclusão da transferência não equivale a instalação. Sincronização do pack também não sincroniza saves, contas, configurações de dispositivos ou sessões de coop.

É necessário um remetente com os arquivos online. Conexão direta depende das redes; o auxiliar pode usar descoberta e relay criptografado quando necessário. Transferência entre redes externas/CGNAT continua dependendo do teste real com o amigo; os ensaios executados nesta implementação usam dois dispositivos isolados no mesmo computador, com descoberta/NAT/relays desligados.

## Dependência fixada e fontes

- Syncthing2.1.7, pacote oficial windows-amd64: https://github.com/syncthing/syncthing/releases/tag/v2.1.7
- ZIP SHA256:3ce7da770f546a8d02b65b2e7ee31f2a3b8b7c3f990ede045590cca4172ecfe1
- EXE SHA256:cd9aea20afda2b9bdba5aff2e6ac964ed5c2e741ed4ceb48ad9e1be096ca7891
- Download oficial: https://github.com/syncthing/syncthing/releases/download/v2.1.7/syncthing-windows-amd64-v2.1.7.zip
- Conferidor publicado: https://github.com/syncthing/syncthing/releases/download/v2.1.7/sha256sum.txt.asc
- Fonte correspondente, sem modificações: https://github.com/syncthing/syncthing/tree/v2.1.7
- Licença MPL2.0, AUTHORS e endereço da fonte ficam junto ao auxiliar local; binários e perfis não vão ao Git.
- REST: https://docs.syncthing.net/dev/rest.html
- Configuração via REST: https://docs.syncthing.net/rest/config.html
- Relays e criptografia: https://docs.syncthing.net/users/relaying.html
- Limites das pastas send-only/receive-only: https://docs.syncthing.net/users/foldertypes.html

## Falhas encontradas e corrigidas nos ensaios

- Primeira transferência: os dispositivos conectavam, mas o anúncio de pasta omitia a própria identidade. PUT de configuração reaproveita padrões; uma senha mascarada de espaços herdada no elemento próprio causava desconexão. Enviar somente o amigo, com encryptionPassword explicitamente vazio, deixa o Syncthing adicionar a entrada própria corretamente. A regressão verifica comprimento zero no REST, sem Trim. Referência do mesmo mecanismo: https://github.com/syncthing/syncthing/issues/10884
- O auxiliar marca .stignore como oculto no Windows. File.WriteAllText falhava ao atualizá-lo. A lista permitida agora é atualizada pela API db/ignores, que também aciona a conferência necessária.
- Cópia privada por sessão, mais nomes de teste profundamente aninhados, excedia MAX_PATH durante a cópia atômica. Recebidos agora compartilham um diretório privado indexado por hash, reduzindo caminhos e duplicação. Fixtures usam nomes curtos; limites de caminhos do motor Windows continuam aplicáveis.
- Auditoria estática identificou hash/leitura do manifesto em operações separadas, hard link de personalizado editável, progresso baseado em arquivo temporário pré-alocado, associações antigas, categoria inexistente em dependência e perda da pausa ao autorizar. Corrigidos antes da entrega e cobertos por ensaios.
- Ensaios iniciais fora do perfil Windows falharam em DPAPI; uma execução longa da suíte antiga de janelas falhou por criação de handle. Uma execução de caminho longo falhou antes da suíte nova. Os resultados finais são registrados separadamente, sem apagar essas tentativas ou mudar as proteções de produção para fazê-las passar.

Resultados finais e hash do executável: Validacoes/1.1.4.json. Nenhum mod foi instalado no jogo real ou enviado a terceiros durante esta implementação.
