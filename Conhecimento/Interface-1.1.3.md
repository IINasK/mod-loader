# Interface e armazenamento — Mod Loader 1.1.3

A interface continua nativa em C#/WinForms, sem Chromium, Electron, imagens de botões ou recortes da skill. Jost é a fonte de texto e Lucide 0.468.0 fornece os glifos vetoriais oficiais, embutidos com licença. Superfícies, bordas, recorte, realce e barras de progresso/rolagem são construídos em código. O estilo G3 permanece como referência visual; CSS não é executado neste aplicativo nativo.

## Organização

- O menu de três riscos reúne lista nova, importar/exportar, presets e Opções. Pacotes é separado, com Cyberpunk2077 como pacote disponível. A escolha troca somente a lista, mediante confirmação; não desinstala arquivos.
- Preset é um conjunto explícito de IDs da lista. Seu nome é editável no topo da tabela. Nova lista começa pedindo o nome do primeiro preset. A engrenagem cria um preset com os mods marcados ou atualiza o atual após confirmação. Criar/importar um mod com preset ativo inclui o ID nesse preset. A lixeira esvazia o conjunto; não desinstala nem apaga os arquivos dos mods.
- Cabeçalhos Mods/Variante/Situação abrem filtros próprios com multisseleção e Aplicar. Mods oferece categorias e busca por nome. Critérios entre colunas são combinados com E; valores de uma mesma coluna com OU. Cancelar não altera o filtro, seleção vazia mostra nenhum item e selecionar todos remove o respectivo critério. Os filtros são da visualização, não alteram a lista exportada nem a autorização de uma instalação.
- A exclusão de categoria é oferecida somente para categoria já salva. Após confirmação, itens vão para Sem categoria; presets, tags, variantes e arquivos são preservados. Sem categoria é a seção raiz e não é excluída.

## Renderização e manutenção

GeneralStyle.PaintCard compartilha a geometria do cartão, GeneralDialog recorta o corpo interno e GeneralReveal aplica o mesmo raio externo. A pintura limpa o fundo integralmente para não conservar pixels anteriores. Os pop-ups são limitados à área visível; quando cabem na janela, ficam dentro dela. Seletores mantêm texto/ícone e altura de44px consistentes.

GeneralScrollBar/GeneralScrollView implementam rolagem própria por roda, teclado, clique no trilho e arraste; expõem valor para acessibilidade e foco visível. A tabela deixa a rolagem nativa desligada. Campos longos e listas recebem a mesma barra, com recuo dentro da borda, evitando as setas cinzas nativas sobre cantos arredondados. Notificações de conteúdo/layout substituem atualização em cada pintura; hooks acompanham recriação/fechamento do controle. Fontes temporárias de pintura são descartadas no mesmo bloco em que são usadas.

Os motores de fila, autorização de arquivos, hash, instalação e reversão foram preservados. A atualização de estado só redesenha filtros de situação quando o rótulo muda, não em cada porcentagem.

## Armazenamento

A auditoria local encontrou aproximadamente9,4GB:4,07GB de preparações extraídas,5,24GB de125pacotes compactados distintos e101MB de logs. O vazamento era falta de limpeza de preparação após instalação/troca de densidade; ambas agora limpam em finally, inclusive após falha e rollback.

Preparações órfãs com mais de24horas são recolhidas na abertura. A manutenção recusa links, respeita operações/leases ativos e outra instância do app. Não exclui arquivos desconhecidos, fontes, backups, saves ou recibos. Opções oferece Limpar downloads com confirmação; reinstalar poderá exigir novo download. Originais na pasta Downloads não são removidos. Logs têm retenção8arquivos/64MiB e limite8MiB por sessão, protegendo sessões ativas.

O AppData real não foi limpo durante a implementação porque a1.1.2 estava aberta. Fechar versões anteriores e abrir a nova permite a manutenção segura. Não confundir cache em AppData com os backups de desfazer em .CoopModsOnline no jogo: estes permanecem protegidos.

## FPV

O Night City FPV saiu do pacote ativo, incluindo scripts customizados, perfil e atalhos antes embutidos no catálogo/defaults. Restam138receitas/135itensvisíveis/8defaults. Migração retira somente identidades antigas do pacote e da fila pendente; não bane uma importação custom explícita futura. O helper histórico de recibos não contém payload custom. O jogo não foi modificado. Snapshot privado foi preservado na Área de Trabalho com scripts, perfis e calibração; não vai ao Git.

## Referências de implementação

- [Microsoft — pintura personalizada](https://learn.microsoft.com/dotnet/desktop/winforms/controls/custom-painting-drawing)
- [Microsoft — double buffering](https://learn.microsoft.com/dotnet/desktop/winforms/advanced/how-to-reduce-graphics-flicker-with-double-buffering-for-forms-and-controls)
- [Microsoft — descarte de fontes](https://learn.microsoft.com/dotnet/api/system.drawing.font.dispose)
- [Microsoft — ciclo de vida de NativeWindow](https://learn.microsoft.com/dotnet/api/system.windows.forms.nativewindow.releasehandle)
- [Microsoft — preferência de rolagem](https://learn.microsoft.com/dotnet/api/system.windows.forms.systeminformation.mousewheelscrolllines)
- [Lucide Static](https://lucide.dev/guide/static), [licença](https://lucide.dev/license)
