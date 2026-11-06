# Compartilhamento e avisos — 1.1.24

A preparação do convite era interrompida porque o Night City Alive instalado usava o perfil exato `legacy-low`, reconhecido pelo scanner, mas recusado pelo exportador. Não era falha de login ou de URL. Agora a exportação e o recebimento aceitam somente esse perfil antigo conhecido, com todos os caminhos, tamanhos e hashes conferidos. O ZIP preserva a densidade instalada; não converte para Baixa oficial, não altera o jogo do remetente e não passa a aceitar perfis arbitrários.

O convite continua sendo um código `ML-P2P1:` para colar no Mod Loader, e não uma página HTTPS. Ele só aparece depois da conferência e preparação do ZIP. A tela o prepara automaticamente; mostra a etapa no progresso, impede copiar um campo vazio e apresenta falhas de preparação no status. A ação Gerar modpack continua oferecendo conferência detalhada quando solicitada. O botão de copiar tem alvo de44px dentro de um campo de56px, com folga vertical e lateral, sem cobrir a borda ou o texto. Campos permanecem com uma linha.

## Avisos de instalação

A janela de falha oferece “Ocultar os próximos avisos desta instalação”. Confirmar essa opção continua a fila sem novas janelas para os erros seguintes. Todos os itens permanecem como falha na tabela e no log. Uma nova instalação restaura os avisos. Adicionar um item prioritário à operação atual não reinicia a escolha. Confirmações de conflito/autorização não são suprimidas por esta opção.

A causa do “Mod fora da lista fixa” também foi corrigida: recibos antigos eram validados usando apenas o catálogo atual. O leitor compartilhado agora usa identidades históricas restritas para mods retirados e arquivos antigos conhecidos. A tabela é somente de caminhos/hashes e relações de dependência, sem ZIPs, DLLs, scripts ou perfis do mod. Não fornece downloads nem recoloca esses mods na lista. DPAPI, hashes, propriedade, base imutável e registros incompletos continuam protegidos. Desfazer uma desativação que reintroduziria um mod retirado fica bloqueado com o backup preservado.

A origem das identidades históricas é o catálogo auditado1.1.18, SHA256`89c68e6bdb8c192977fcace5d30a4b8b25912d8780fd6067f77f5c7d41c6dc05`, e a receita pré-retirada recuperada do Git`6fcb9c72^`, catálogo SHA256`87a1039ef472f6e64011a49f5ea9a84839458738f287030014626dea1e9283b8`. Fragmentos de texto dos patches não são incluídos nessa tabela de recibos.

## Verificações e limites

Conferência local somente leitura:104recibos aprovados, nenhum erro; exportação inspecionada do Night City Alive e dependências com68arquivos. Fixtures verificam geração do ZIP com árvore relativa, recepção do perfil antigo, rejeição de perfil alterado, históricos adulterados, fila e opção real do diálogo. Telas conferidas em português/inglês e três temas. Transferência real em loopback verifica convite automático, pausa, interrupção/retomada, instalação na fixture e limpeza. Setup isolado passa24checks.

O jogo e o Mod Loader reais permaneceram abertos. Nenhum mod, save ou configuração real foi instalado, removido ou alterado nesta revisão. Não foi repetida a suíte geral dependente de fechamento, nem um teste entre dois computadores reais. Provas e hashes em`Validacoes/1.1.24.json`; capturas e detalhes privados em`T/R124/`.
