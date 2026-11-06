# Convite de compartilhamento — 1.1.28

Ao abrir o compartilhamento, o campo mostra o início da conexão e recebe automaticamente o convite ML-P2P1 assim que a identidade da conexão fica pronta. Não espera conferir nem compactar os arquivos instalados. O ícone de copiar apenas copia integralmente o texto exibido; não cria sessão, não inicia rede e não prepara arquivo. Um projeto vazio pode receber; sem itens não se inventa convite de envio.

Antes, o convite incluía hash e tamanho do ZIP já pronto. Isso prendia o campo à preparação inteira, que pode demorar minutos. A inspeção local somente leitura passou com 140 itens e 6.211.971.722 bytes instalados; não foi gerado nem enviado um pack real desse jogo para testar.

O convite antecipado identifica a conexão e carrega uma capacidade aleatória de 256 bits. Quando o ZIP verificado termina, ready.json fixa SHA-256 e tamanho com HMAC-SHA256 vinculado à mesma sessão. O receptor valida antes de instalar e recusa alteração posterior. O transporte continua Syncthing 2.1.7, com identificação dos pares e retomada existentes. Não há URL HTTPS fictícia nem novo serviço. Ambos precisam usar 1.1.28 ou superior para os novos convites. Convites anteriores de ZIP instalado continuam aceitos.

O ZIP em preparação fica ignorado pelo transporte; só pack.zip e ready.json finais são liberados. Falha de preparação mantém o mesmo convite e mostra a causa, sem falso sucesso. A persistência conserva identidade, seleção e hash final para retomar. Códigos de convite, chaves, sessões e certificados nunca entram nas evidências públicas.

A limpeza agora tem ordem: depois de instalar, o receptor mantém o índice anunciado até o remetente confirmar entrega completa e retirar o compartilhamento. Estado desconectado/unknown não é confirmação. A retomada também recupera limpeza pendente. A API oficial documenta esses estados em https://docs.syncthing.net/rest/db-completion-get.html . Apenas o transporte pertencente à sessão é removido; instalação e recibos permanecem.

Testes em dois pares oficiais independentes, somente loopback, cobrem convite antes do ZIP, cópia na janela, falha de preparo, autenticidade, identidade persistida, instalação e limpeza dos dois lados. A regressão cobre formato antigo, configuração, pausa/reinício e retomada. Instalação de Setup é verificada em fixture. Não houve novo teste entre PCs remotos nem alteração do jogo real. Resultados sanitizados em Validacoes/1.1.28.json.
