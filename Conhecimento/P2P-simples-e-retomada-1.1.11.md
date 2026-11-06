# P2P simplificado e retomada — 1.1.11

Revisão de05/11/2026. Usa o mesmo auxiliar oficial Syncthing2.1.7 e o mesmo motor de snapshots instalados; sem serviço de nuvem novo. Atualize o Mod Loader nos dois computadores.

## Enviar e receber

No menu Compartilhar, há dois campos: **Link para enviar ao amigo** e **Link recebido do amigo**, um ícone para copiar, Conectar, Pausar/Retomar e progresso. A pasta local aparece para quem recebe. Foram retiradas a lista de sessões e as ações separadas de criar/receber/autorizar/carregar. As sessões anteriores continuam guardadas internamente.

1. Quem envia abre o projeto/preset, deixa o campo recebido vazio e clica **Conectar**. O app prepara um ZIP temporário a partir dos arquivos instalados reconhecidos e das configurações permitidas. O primeiro campo mostra o convite para copiar ao amigo.
2. Quem recebe cola esse convite no segundo campo, escolhe sua pasta local e clica **Conectar**. Confirma o destino e devolve o link de resposta exibido no primeiro campo.
3. Quem envia cola a resposta no segundo campo e clica **Conectar**. Transferência, conferência e instalação continuam automaticamente. Mantenham os dois aplicativos abertos até concluir.

O link é um código de conexão ML-P2P1, não um endereço de download público. O convite/resposta mantém a autorização mútua existente. O programa não envia mensagens ao amigo sozinho. O auxiliar próprio pode ser baixado no primeiro uso; não é preciso instalar/configurar um Syncthing pessoal.

## Organização e origem

O ZIP mantém nomes personalizados, descrições, tags, categorias e presets definidos na lista. Presets mantêm seus IDs e nomes; o ativo aparece primeiro. Seus membros são limitados aos mods contidos na exportação, que inclui o preset escolhido e suas dependências. Não inventa arquivos de mods ausentes nem inclui outras pastas do jogo.

Links HTTPS públicos já cadastrados são preservados nos mods personalizados. Caminhos de arquivos no computador do remetente são substituídos pela identidade instalada. Um ZIP importado sem um link cadastrado não fornece uma página de origem verificável: o app não inventa esse endereço. Receitas fixas mantêm suas fontes confiáveis. Arquivos são instalados do pacote conferido, sem baixar novamente essas páginas.

## Pausa, queda e retomada

Pausar altera apenas a pausa local da sessão. O outro usuário vê **Pausado pelo amigo** e não pode liberar essa pausa. Se ambos pausarem, cada um precisa retomar sua própria pausa. A pausa é da transferência; a cópia transacional final termina sem interrupção para preservar os arquivos.

As sessões conservam identidade, hash, destino autorizado, vínculo do amigo e pausa em disco. Ao reabrir o projeto, sessões pendentes são retomadas. Desconexão temporária não apaga os blocos já recebidos. O progresso parcial usa os eventos de blocos do auxiliar, não o tamanho pré-alocado do ZIP. A barra fica abaixo de100% até conferir, instalar e limpar.

Uma instalação nova interrompida pode continuar pelo mesmo recibo protegido e pela preparação restante, desde que o pacote/manifesto e os arquivos ainda correspondam aos hashes esperados. Reconhece também uma cópia concluída antes de salvar o último marcador. Se o destino foi editado ou a preparação desapareceu, preserva os arquivos e informa a falha; não sobrescreve para forçar a retomada. Recibos antigos que não contêm os dados de recuperação continuam protegidos pela guarda anterior.

## Temporários e espaço

O receptor apaga o ZIP e a pasta de transferência após instalar e conferir. O remetente remove sua cópia de transporte após confirmar a entrega. Enquanto houver falha ou operação incompleta, o pacote é preservado para retomada. Preparação interrompida antes de cadastrar uma sessão tem marcador próprio; sua sobra é limpa na próxima abertura, sem varrer pastas de outros projetos.

Não há cache permanente do ZIP compartilhado nem cópia extra dele nos recibos. Permanecem os mods instalados, recibos, metadados e backups necessários para remover/desfazer, além do auxiliar e seu banco de sincronização. Excluir arquivos temporários não promete apagamento físico irrecuperável de setores de SSD.

## Evidências e limites

Ensaios em pastas isoladas verificam metadados, seis combinações de tema/idioma, recuperação de instalação e preservação de edição concorrente. Duas instâncias oficiais no mesmo computador transferem um ZIP parcial, observam pausas, interrompem monitor e processo filho, reiniciam com a mesma identidade, concluem por hash e eliminam os temporários. Outro ensaio usa os próprios botões Conectar das duas janelas, com ciclo normal de eventos WinForms, até devolver a lista instalada.

Falhas intermediárias registradas: campo em linha implícita cresceu além do pai; ícone herdou mínimo de80px numa coluna44px; limite de velocidade inicialmente não valia para LAN no teste; simulação de queda inicialmente encerrava só o monitor, deixando o worker com o banco aberto; ensaio UI sem message loop normal travou. Corrigidos layout, fixture de velocidade, encerramento da árvore própria e harness com Application.Run. O encerramento do auxiliar também tenta desligar o worker se o monitor já saiu.

Os achados reutilizáveis de layout foram enviados à caixa local privada de relatos G3 Design conforme a instrução recebida; a skill central não foi editada nesta revisão. Os relatos não acompanham o repositório público.

Resultados consolidados: [Validacoes/1.1.11.json](Validacoes/1.1.11.json). Sem instalação no jogo real, alteração de saves ou teste entre redes externas/CGNAT. A retomada depende de o remetente conservar a sessão e o pacote enquanto pendentes. Catálogo e RED4ext1.31.0 preservados.

Fontes do auxiliar: [estado remoto de pausa](https://docs.syncthing.net/rest/db-completion-get.html), [progresso por blocos](https://docs.syncthing.net/events/downloadprogress.html), [progresso remoto](https://docs.syncthing.net/events/remotedownloadprogress.html), [API de eventos](https://docs.syncthing.net/rest/events-get.html). A conexão entre redes reais continua exigindo teste com o amigo.
