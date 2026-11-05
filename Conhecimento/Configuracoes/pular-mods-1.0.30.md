# Pular e continuar — Mod Loader 1.0.30

## Pedido

Douglas pediu botão de skip após o amigo encontrar erro LZMA codec is unsupported ao preparar Ava Complexions All in one. A falha era do leitor tar do Windows, que não conseguiu listar o .7z. Aprovar uma divergência de tamanho não resolve ausência de suporte do extrator. Nesta rodada também pediu retirar o botão Auditar da barra superior.

## Comportamento

O aviso de falha agora mostra detalhes copiáveis do mod, pacote, entrada e erro, com botões Pular e continuar e Parar. Enter/fechamento/ESC seguem a ação Parar. Pular está habilitado somente se existe uma fila autorizada para o mesmo jogo e a operação não é uma troca de densidade. Fora disso, o botão fica desabilitado.

Pular retira da tentativa o mod com falha e todas as raízes autorizadas cujo fechamento transitivo depende dele. A tabela marca a falha e esses dependentes como Pulado, desmarca as linhas afetadas e explica o motivo no tooltip. Itens independentes mantêm sua seleção. O contador informa Pulados quando necessário e o resumo final diz quantos foram pulados, sem anunciar que todos foram instalados.

O fluxo retoma a fila original com um plano novo, sem repetir a lista de instalação que falhou. Não incrementa a autorização de geração nem autoriza novos mods. O pulo não aprova arquivos divergentes, não instala o mod que falhou e não remove arquivos já instalados. Downloads originais, cache, configurações, saves e componentes existentes permanecem preservados. Se a base falhar e todos os selecionados dependerem dela, todos esses itens serão pulados; não instalar complementos sem a base.

O estado vale para essa tentativa. Depois de corrigir a causa, marcar o item e iniciar Baixar e instalar novamente permite nova tentativa, sem bloqueio persistente. Atualizações de progresso antigas da fila não devem sobrescrever Pulado enquanto a fila está ativa. Ao concluir/cancelar, a autorização e os marcadores internos são encerrados.

O aceite manual de diferenças e os registros de remoção da versão 1.0.29 continuam funcionando. Na divergência que pode ser aceita, a confirmação existente aparece primeiro; recusar essa instalação ainda permite escolher Pular e continuar no aviso. Erros que impedem ler o pacote não oferecem instalação forçada.

## Auditor

O botão Auditar seleção foi retirado da barra superior, conforme pedido. O motor mantém suas verificações automáticas; o menu contextual Auditar mod e as informações dos detalhes permanecem disponíveis. Nenhuma alteração nas regras de segurança, receitas ou permissões de instalação.

## Validação

Teste com fila real e instalador em fixtures: pacote fixado com entrada ausente, mod independente instalado após pular, dois níveis de dependência pulados, item não selecionado ausente, base/mod já instalado/saves/pacotes/cache preservados, nenhum navegador real aberto, Parar interrompendo, fila limpa, nova autorização instalando a dependência corrigida, controles e estados do diálogo. São 22 verificações específicas incluídas na regressão completa.

O catálogo continua com 95 componentes, 84 linhas visíveis no perfil sem Phantom Liberty; os hashes das receitas, o motor de arquivos, RED4ext 1.31.0 e o FPV NASK não mudaram nesta versão. Nenhuma instalação foi feita no jogo real. Os testes anteriores de originais RAR/7z e FPV da versão 1.0.29 não foram repetidos, pois o motor e os pacotes são os mesmos.

## Limite prático

Pular permite continuar a instalação dos mods independentes; não adiciona suporte LZMA ao Windows do amigo. Os itens pulados continuam ausentes ou parciais e podem afetar a aparência de presets que os utilizem. O defeito específico de decodificação daquele computador não foi reproduzido localmente.

## Atualização 1.0.31
A dependência do tar do Windows descrita no histórico foi substituída pelo leitor 7-Zip oficial incorporado. Ver leitor-incorporado-1.0.31.md. Pular, aceite explícito e remoção continuam disponíveis.
