# Pular o download atual — 1.0.32

O usuário pode selecionar todos e, ao chegar à página de um mod, decidir que não quer instalá-lo. A opção Pular e continuar da 1.0.30 só aparecia na falha de preparação. A 1.0.32 acrescenta Pular mod atual na barra inferior durante a espera pelo download autorizado da fila Baixar e instalar.

## Funcionamento

O botão retira o mod e os itens selecionados que dependem dele desta tentativa, desmarca suas linhas e mantém o estado Pulado. O motivo do dependente aparece na linha. A mesma lógica de dependências da opção de pular falhas foi reaproveitada. Nenhum arquivo já instalado é removido e nenhuma receita ou guarda do motor foi alterada.

O monitor anterior é cancelado e a geração da autorização é invalidada. A fila ProcessAutomaticQueue original continua com os demais itens e abre a página oficial somente do próximo necessário. Se o último for pulado, a tentativa termina com a quantidade pulada, sem declarar instalação total. Um pacote do item pulado que terminar depois na pasta monitorada não o autoriza novamente.

Uma nova seleção explícita pode retomar o item; o arquivo já concluído será reaproveitado depois das verificações normais de estabilidade, tamanho e SHA. Pular não cancela um download no navegador nem fecha a aba. O usuário pode cancelar esse download no próprio navegador se desejar. Não há lista permanente de bloqueio.

O botão não interrompe uma cópia em andamento. Fica indisponível durante preparação/instalação/conferência e fora da espera de uma fila autorizada. Download avulso e troca de densidade não ganham autorização por esse botão. O contexto de jogo e a geração do monitor precisam continuar os mesmos. A opção Pular e continuar na falha de preparação continua funcionando.

## Validação

1.035 verificações em 29 relatórios, incluindo 18 novas do pulo durante a espera. Foram exercitados o clique real no botão dentro de uma janela de teste, monitor, importador e instalador reais em diretórios fictícios; navegador simulado sem tráfego de rede.

Cobertura nova: ausência de fila, operações ativas, download avulso, próximo item, dependente, geração antiga cancelada, pacote tardio ignorado, instalação somente do restante, resumo dos pulados, preservação da base/saves/pacotes, nova seleção explícita com reutilização do download e pulo do último item. As regressões anteriores incluem leitor incorporado RAR/7z, aceite de diferenças e remoção/desfazer.

Catálogo dos 95 componentes, motor de instalação, RED4ext e leitor 7-Zip incorporado preservados. Nenhum arquivo do jogo real alterado nesta revisão. O EXE continua único, sem mods/saves/credenciais anexos.

## Falhas de ensaio corrigidas

A primeira fixture nova usava nome de diretório fora do prefixo obrigatório online-testes; o motor recusou corretamente o modo de teste. Corrigido somente o nome da fixture, sem relaxar a guarda. O ensaio de reuso imediato também precisava aguardar a janela normal de estabilidade de um arquivo preexistente; ajustado o ensaio para usar o mesmo método existente. O teste focado e a suíte final passaram depois dessas correções.

Não houve teste no computador do amigo nem download interativo real nesta revisão. O navegador usado no ensaio foi uma simulação, mantendo o contrato de isolamento.
