# Armazenamento e modpack instalado — 1.1.17

## Política

O aplicativo não deve manter uma segunda cópia permanente dos pacotes de mods. O pacote original continua no local escolhido para downloads, sob controle do usuário. Os arquivos extraídos ficam no destino do jogo. Os dados do projeto guardam lista, preferências, caminhos, tamanhos, hashes e recibos de instalação; não um espelho dos mods.

A janela de produção usa DownloadCache em modo de índice de originais, dentro de Cache do projeto. Importar verifica o arquivo e grava sua referência em sources.json, sem copiar o pacote. A referência sobrevive à reabertura. Se o usuário apagar o original, não há restauração silenciosa de uma cópia. Mods instalados continuam funcionando e a exportação usa os arquivos instalados. Para reinstalar um mod ausente poderá ser necessário baixar o original novamente.

Downloads diretos concluem na pasta de downloads selecionada, preservando colisões com arquivos existentes; não concluem como payloads em AppData. Arquivos parciais são temporários. As leituras de índice permanecem limitadas e recusam caminhos inválidos ou links. A validação de tamanho/hash dos arquivos originais e a instalação transacional permanecem.

O construtor de cache antigo permanece para os leitores/testes legados; a janela de produção foi conferida em execução usando o modo novo. Não adicionar um novo ponto de UI que volte a instanciar cache de payload para um projeto.

## Como os contadores são calculados

Os contadores usam a soma do tamanho lógico dos arquivos existentes, obtido com FileInfo.Length, sobre conjuntos de caminhos canônicos. Não são uma reserva de disco, estimativa do download nem alocação física do Windows.

- **Mods:** arquivos instalados conhecidos do projeto, deduplicados por caminho. O tamanho de cada mod pode conter o mesmo arquivo compartilhado; o total conta esse caminho uma vez.
- **Cache do projeto:** arquivos próprios de apoio, recibos, índice de originais, dados de rollback e suporte P2P daquele projeto. Novos pacotes de mods não entram como cópias permanentes.
- **Pacotes antigos:** cópias de versões anteriores ainda presentes, mostradas separadamente até a limpeza explícita. No projeto, somente as referenciadas por suas receitas confiáveis; na Home também entram os pacotes antigos globais sem atribuição. Não esconder bytes antigos apenas para reduzir o contador.
- **Total:** união dos arquivos instalados, dados de apoio, originais referenciados ainda presentes e pacotes antigos aplicáveis; sem contar o mesmo caminho duas vezes.
- **Home:** união de todos os projetos cadastrados, com o índice compartilhado uma vez. Dados comuns e pacotes globais não são duplicados pela quantidade de projetos. O projeto legado não recursa a pasta Projetos da biblioteca como se os outros projetos fossem seus dados.
- **Medidos:** itens com arquivos instalados medidos / itens visíveis; acompanha busca, preset e filtros, independentemente das caixas marcadas.

Na auditoria local, o contador exato disponível antes da correção foi 5.249.111.661 bytes no Cyberpunk padrão. O valor relatado de 2,58 GiB não foi reproduzido; sua causa permanece desconhecida. Não atribuir uma causa sem evidência. A conta antiga incluía 122 pacotes referenciados (5.214.179.385 bytes) mais 34.932.276 bytes de apoio. A referência antiga a 4,89 GiB descrevia essa medição, não reproduzia o valor informado pelo usuário.

Depois da correção, o mesmo projeto tem 34.932.276 bytes de apoio, aproximadamente 33,3 MiB, principalmente o executável Syncthing para P2P. A lista/configuração ocupa cerca de 53 KiB. No legado, o apoio próprio mede 52.088 bytes, sem somar os subprojetos. O volume de arquivos instalados conhecido permaneceu em 6.117.218.104 bytes; não foi executada alteração no jogo.

Limpeza local separada: após autorização explícita, 124 cópias internas foram identificadas pelo catálogo, tamanho e SHA-256 e removidas. Resultado conferido no disco: 5.231.396.280 bytes liberados, sem arquivos aprovados restantes. Um arquivo não identificado, de 6.728.110 bytes, foi preservado. Originais da pasta Downloads, jogo, saves e preferências foram preservados. A proposta de limpeza automática na abertura foi recusada e não foi implementada. Em outros PCs, usar a ação explícita Limpar cópias antigas nas opções da Home.

## Registro dos caminhos instalados

Reutilizar os recibos existentes: GeneralModInstaller registra cada caminho relativo, hash, tamanho e autoria; importações Cyberpunk usam recibos protegidos e a associação CyberpunkImports do projeto; P2P guarda manifesto/recibo dos arquivos recebidos. As receitas fixas reconhecem os caminhos conhecidos e suas dependências. A pasta de destino fica no estado do projeto. Raiz local + caminho relativo identifica o arquivo, sem necessidade de guardar seu conteúdo no cache.

Mods antigos da lista fixa ainda podem ser reconhecidos pelo catálogo. Não adivinhar a posse de arquivos desconhecidos colocados manualmente fora do gerenciador: sem receita ou recibo verificável, avisar antes de exportar.

## Gerar modpack sem conexão

No menu Modpack e compartilhar, Gerar modpack confere os arquivos instalados e salva um ZIP escolhido pelo usuário. Está disponível também no projeto pronto, como exportação somente de leitura; não libera alteração de seus presets protegidos. Abrir o diálogo ou gerar o ZIP não inicia rede nem baixa Syncthing.

A árvore dentro do ZIP começa na raiz do jogo de destino, por exemplo:

```text
archive/pc/mod/aparencia.archive
r6/tweaks/exemplo/config.yml
bin/x64/plugins/cyber_engine_tweaks/mods/exemplo/init.lua
_modloader/project.json
```

O usuário extrai o conteúdo na pasta raiz do jogo. Cada caminho intermediário é preservado, mesmo que só tenha um arquivo de 1 KiB. Não exportar Este computador, letra do SSD ou os diretórios particulares do remetente. _modloader/project.json preserva organização, nomes, presets e fontes públicas; não contém caminhos privados de download. Configurações autorizadas são filtradas para não incluir credenciais; seções INI são reconstruídas como INI, não como JSON.

Não existe cópia intermediária de toda a árvore: os arquivos instalados são lidos diretamente para o ZIP final, com buffer limitado. O ZIP salvo é um arquivo de exportação do usuário, não um cache permanente do gerenciador.

## Conferência e P2P

Antes de gerar o arquivo ou iniciar uma nova conexão de envio, a mesma inspeção verifica arquivos presentes, tamanho/hash e dependências. Mods incompletos ou alterados são apresentados juntos, com caminhos dos problemas. A exportação é interrompida até corrigir; não enviar um modpack silenciosamente incompleto. Durante a escrita, os arquivos são reconferidos para detectar alterações e o resultado incompleto é removido em caso de falha.

O protocolo P2P existente continua com manifesto e conteúdo deduplicado por hash; o receptor usa os caminhos do manifesto para instalar no destino dele. Mantém um único ZIP temporário durante a transferência, inclusive em interrupções para permitir retomada, removido após a conclusão confirmada. A retomada conserva o pacote já conferido, não reconstrói outro a cada reconexão. A geração manual independente usa a árvore de pastas convencional.

## Validação

Foram testados índice sem cópias e reabertura, download direto em Downloads, preservação dos originais, exclusão manual do pacote original seguida de exportação, árvore de um arquivo 1 KiB, extração em outro destino, presets/nomes sem caminhos privados, mudança durante exportação, faltantes agregados, escopo legado e soma da Home. O P2P local/manifestos e a interface foram verificados; não houve teste novo entre dois PCs reais. Suíte geral final e ciclo isolado de Setup aprovados. Uma falha transitória do teste antigo de visibilidade do seletor foi preservada; a repetição do mesmo código passou, com causa desconhecida.

A versão instalada foi atualizada com hash e entrada do Windows conferidos, sem forçar fechamento, mantendo os dois arquivos anteriores de preferências. O construtor da janela de produção foi inspecionado sem mostrar a janela/iniciar rede: usa índice de originais e cache no projeto correto. Resultado completo em [Validacoes/1.1.17.json](Validacoes/1.1.17.json).
