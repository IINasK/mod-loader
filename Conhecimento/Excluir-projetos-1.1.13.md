# Lixeira nos projetos salvos — 1.1.13

A seta à direita de cada projeto personalizado salvo foi substituída pelo ícone oficial Lucide de lixeira. O alvo de clique mede44×44px e usa as mesmas coordenadas da pintura da linha. O nome tem espaço reservado para o ícone e continua abrindo o projeto quando clicado. A tecla Delete também inicia a exclusão; Enter continua abrindo.

São necessárias duas respostas Sim, em dois diálogos que identificam o projeto. A resposta padrão nas duas etapas é **Não**. Cancelar, fechar o diálogo ou responder Não em qualquer uma delas preserva o projeto. Cancelar não reconstrói a Home, evitando uma piscada desnecessária.

A exclusão reutiliza GeneralProjectStore.Delete: remove a referência do índice de projetos salvos. Conserva os mods instalados, os arquivos do jogo e os dados/recibos/backups locais. Não é uma desinstalação nem uma limpeza de disco. O projeto padrão continua fora dessa lista e protegido pelo armazenamento.

Validação:204checks em3relatórios de projetos/Home/marca, executados em D:/T113verified, com projetos fictícios. Os testes acionam o clique real da lixeira e o teclado, recusam a primeira/segunda confirmação, fecham o diálogo e confirmam as duas. Conferem alvo correto entre vizinhos, ausência de abertura acidental, índice persistido e hashes de arquivos preservados. Capturas nos três temas e nos dois idiomas; segunda confirmação renderizada conferida. Catálogo e motor de instalação não foram alterados.

Não houve exclusão de projetos pessoais nem alteração no jogo real. A suíte geral de1.1.12 não foi repetida nesta mudança localizada; a verificação de publicação/atualização fica no JSON de validação. Esta é uma alteração comum de tela, sem novo achado para manutenção da skill G3.
