# Espaço por mod e cache do projeto — 1.1.16

A tabela mostra uma coluna Espaço logo depois do nome do mod. O contador Medidos: 100/135 significa que o tamanho instalado foi identificado para 100 dos 135 itens visíveis. Selecionar caixas não muda esse contador. Preset, pesquisa e filtros mudam tanto o numerador quanto o denominador.

## O que é contado

- Espaço do mod: soma dos arquivos instalados conhecidos daquele item. Um arquivo vazio identificado aparece como 0 B; tamanho desconhecido ou indisponível aparece como —. O tamanho divulgado do download não vira tamanho instalado.
- Mods: total dos caminhos instalados conhecidos do projeto, incluindo dependências reconhecidas. Arquivos compartilhados aparecem no tamanho de cada mod que os utiliza, mas entram uma vez no total.
- Cache do projeto: dados próprios do gerenciador daquele projeto, como lista/configuração, recibos e backups, mais pacotes internos referenciados, incluindo dependências e variantes conhecidas. Não cria outra cópia dos arquivos para medir.
- Total do projeto: união dos mods instalados, dados de apoio e arquivos de download referenciados ainda presentes. Um ZIP original em Downloads pode entrar no total sem ser cache interno. Nada de varrer o jogo inteiro ou toda a pasta Downloads.

A Home inclui também biblioteca e cache global, deduplicando caminhos entre projetos. Assim, a soma manual dos caches individuais pode exceder o cache global quando projetos compartilham pacotes. Logs globais sem atribuição não são inventados como dados de um projeto.

O número exibido é o espaço lógico usado pelos arquivos, não uma reserva, limite ou cota de disco. Pode diferir de Espaço em disco do Windows por compressão, alocação ou links físicos. Pastas desconhecidas e arquivos sem rastreio não ganham um dono por suposição. Totais incompletos recebem indicação parcial.

## Custo e atualização

A leitura usa tamanho e existência dos arquivos, sem reler o conteúdo dos mods para calcular hash. Os caminhos são deduplicados e cada tamanho é consultado uma vez por medição. Não seguir links/junctions; leitores de inspeção permitem que o instalador substitua os próprios registros. As medidas rodam em segundo plano, com uma execução por vez e uma atualização pendente, inclusive ao concluir a instalação de cada item. Não constituem uma fotografia atômica de uma instalação em andamento.

A identificação da variante/densidade Night City Alive, a integridade, aprovações e as regras de instalação/desinstalação continuam sendo verificações separadas. A medição não deve revalidar payloads pesados nem tratar um tamanho conhecido como aprovação de instalação.

## Fontes e testes

A fonte Jost foi mantida após a conferência e a correção do pedido pelo usuário. Não foi aberto relato G3 de defeito tipográfico inexistente. Não houve redesenho comum registrado como falha da skill.

Testes locais conferem arquivos compartilhados, tamanho zero, faltantes, mudança do tamanho real, remoção dos ZIPs originais, recibos P2P, dependências fixas e variantes, razão por filtro e seleção, português/inglês e os três temas em duas larguras. A suíte geral e o ciclo isolado do Setup passaram. O jogo, saves e mods reais não foram alterados. Evidências e limites: [validação](Validacoes/1.1.16.json).
