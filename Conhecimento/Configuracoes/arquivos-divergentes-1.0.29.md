# Divergências de arquivo — Mod Loader 1.0.29

## Pedido e resultado

Douglas relatou erros genéricos de RAR/7z e de tamanho, sem identificação do mod. A captura do amigo confirmou 84 itens na lista Sem Phantom Liberty, 19 instalados e 65 restantes; não havia erro de quantidade no filtro. O primeiro pendente era Material and Texture Override 5266/49809, mas não houve log do PC remoto para atribuir a causa definitivamente a esse pacote.

O instalador agora identifica mod, nome do pacote e entrada interna na falha de preparação. Erros de tamanho mostram esperado e observado; os erros do leitor do Windows informam código de saída/detalhe em vez de afirmar genericamente que há um link. A linha com erro é selecionada, fica como Falhou, recebe a mensagem completa no tooltip e incrementa o contador.

## Instalar mesmo assim

Quando o pacote original coincide com tamanho e SHA fixados, a extração é regular e concluída e um arquivo extraído diverge da receita, aparece a confirmação Instalar mesmo assim?, com Não como padrão. Mostra tamanhos, hashes e risco de falhas. A aprovação registra somente essa identidade, para esse jogo/catálogo, e repete a operação usando o motor existente. Nenhum arquivo é marcado como instalado só por aprovar: ainda é necessário copiar e conferir os bytes reais.

Não existe autorização genérica para pular todas as checagens. Não se oferece o aceite se o pacote original não for o fixado, o extrator não conseguir lê-lo, houver atalhos/entradas ambíguas, caminho inseguro, limite de leitura excedido, base imutável, dado pessoal, patch FPV ou variante de densidade. O arquivo de destino precisa estar ausente; arquivos existentes, conhecidos ou desconhecidos, não são substituídos por essa opção. As substituições aprovadas do FPV mantêm seu mecanismo anterior de backup/reversão.

Leitura limitada a 8 GiB por arquivo, como o teto de receitas já existente. A identificação da divergência usa o tamanho completo e SHA-256 após ler o fluxo até o fim; para RAR/7z, a saída bem-sucedida do extrator também é necessária. Uma preparação interrompida é limpa para não acumular cópias parciais. Não foi relaxada a validação de caminhos, links ou duplicatas.

## Reconhecimento, aviso e remoção

O registro é local, protegido com DPAPI da conta Windows, vinculado à pasta absoluta do jogo, catálogo, mod, pacote original, entrada, destino e identidade canônica. Fica em .CoopModsOnline/local-approvals/identidades.dat, fora dos recibos JSON. Limites: 1 MiB e 256 identidades. O catálogo original e suas 95 receitas não são modificados.

Após instalar, a célula Instalação mostra o ícone nativo de aviso do Windows. Passar o mouse explica que uma diferença foi aceita e pode causar erros. O duplo clique nos detalhes também exibe o aviso. Ao reabrir, a conferência aceita as identidades exatas registradas, sem aceitar alterações posteriores desconhecidas.

Para retirar: marcar o mod na primeira coluna e clicar no botão vermelho Remover selecionados. A remoção existente usa os arquivos/recibos conferidos e a quarentena; desfazer a remoção restaura a identidade aceita. Desfazer instalação remove só arquivos próprios intactos e preserva mudanças posteriores. O recibo registra o hash realmente preparado, inclusive ao reencontrar uma identidade canônica ou histórica aceita.

## Escopo e verificações

Nenhuma instalação foi feita no jogo de Douglas nesta entrega. Nenhuma dependência foi atualizada, RED4ext 1.31.0 e FPV NASK preservados. Sem alteração no catálogo, roupas, perfil sem DLC, fila de monitoramento ou acesso ao Nexus. O EXE é o único arquivo de entrega; não inclui mods nem credenciais.

Testes: interface com recusa/aceite/retomada/ícone; instalação real em fixtures; reconhecimento após reabrir; remoção individual e em lote; reversão; recibos antigos; dados alterados preservados; registro corrompido, de outra pasta/catalogo, excessivo e de componentes protegidos recusados. Conferidos também 12 arquivos RAR/7z oficiais, totalizando 65 destinos, no leitor deste Windows, e 136 verificações específicas do FPV.

O leitor RAR/7z continua sendo tar.exe do Windows. Uma falha de suporte do extrator no PC do amigo ainda exige o novo diagnóstico; a autorização manual não torna legível um pacote que o extrator não consegue abrir. A causa remota do erro anterior não foi reproduzida, portanto não alegar que já foi resolvida naquele PC.

## Falhas de desenvolvimento registradas

Na primeira compilação, nome de variável de teste conflitante (CS0136) foi corrigido por renomeação. A primeira soma de relatórios tentou somar propriedades Passed que eram listas de strings; os testes tinham concluído com código zero, mas a soma foi corrigida para considerar apenas contadores numéricos. Uma segunda execução completa foi feita após a limpeza de preparação e o registro do hash efetivo. Revisão independente identificou o caso de uma aprovação histórica; corrigido e coberto por teste de instalação/desfazer. Nenhuma dessas correções alterou os pins dos mods ou o jogo real.

O primeiro arquivamento de relatórios também encontrou JSONs intencionalmente corrompidos das fixtures. O coletor passou a ignorar esses arquivos de entrada e copiar somente os 26 relatórios numéricos válidos. A publicação foi retomada preservando os backups anteriores; isso não foi uma falha de testes ou do instalador.

## Atualização 1.0.31
A dependência do tar do Windows descrita no histórico foi substituída pelo leitor 7-Zip oficial incorporado. Ver leitor-incorporado-1.0.31.md. Pular, aceite explícito e remoção continuam disponíveis.
