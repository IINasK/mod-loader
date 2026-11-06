# Atualização automática — 1.1.26

Cada nova abertura do programa instalado consulta a última Release estável de IINasK/mod-loader. A consulta é única dentro daquele processo; mudar de projeto não repete a requisição. A distribuição continua sendo o mesmo Setup Inno de usuário, sem serviço, tarefa agendada ou framework novo.

Antes de abrir projetos, uma versão nova é baixada por HTTPS, conferida pelo tamanho e SHA-256 publicado pelo GitHub e pela identificação/versão do Setup. O aplicativo encerra normalmente, o instalador substitui a instalação registrada no mesmo diretório e reabre o programa. Os parâmetros silenciosos não solicitam reinício do computador, elevação ou encerramento forçado de outros aplicativos. Nenhum mod é atualizado por esse mecanismo.

Somente ModLoader.exe correspondente à instalação registrada no Windows executa a troca automática. Executáveis portáteis e cópias de desenvolvimento conservam o aviso de Release; não assumem o diretório da instalação real. É necessário instalar a 1.1.26 uma vez para habilitar a função nas máquinas com versões anteriores.

Sem rede, download incompleto, hash divergente ou outra instância em uso, a versão atual continua disponível. A consulta tem timeout de 3 segundos; a transferência possui limites de tempo/tamanho. Falha depois de iniciar o Setup permite abrir novamente o programa existente; não afirmar rollback de uma interrupção arbitrária de energia durante a gravação do instalador. Uma tentativa recente não é repetida em ciclo: há intervalo de 24 horas para o mesmo Setup.

O cache de suporte guarda temporariamente apenas um Setup, um pequeno registro de propriedade/tentativa e o log da atualização. Na abertura da versão nova, os arquivos próprios são removidos; se estiverem em uso, a limpeza tenta novamente. Arquivos desconhecidos e pacotes originais em Downloads são preservados. Nenhuma cópia dos mods é criada.

Validação: 30 verificações do parser, 25 do fluxo automático, 24 do Setup padrão, substituição real de um processo antigo por novo em instalação isolada com reabertura e preservação de configuração, além do download público com o transporte de produção. A identificação de instaladores Inno usa ProductVersion e FileDescription sem espaços de preenchimento; FileVersion pode estar vazio. Jogo real e computador do amigo não foram usados nesses testes.

Referência dos parâmetros oficiais: https://jrsoftware.org/ishelp/topic_setupcmdline.htm . Resultados resumidos em Validacoes/1.1.26.json; evidências privadas em T/R126/.