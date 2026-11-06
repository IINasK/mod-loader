# Erros de instalação — 1.1.25

O relato remoto mostrou várias linhas “Falha”, com tooltip reduzido a “Falhou”. A causa desse problema de exibição foi confirmada: RowStatus atribuía a mensagem à célula, mas DrawRows reconstruía a tabela usando apenas o estado curto e ainda sobrescrevia o tooltip duas vezes. Agora a mensagem pertence ao estado em memória do item/variante; redesenhar, filtrar e atualizar o tamanho não a perde. Rechecagem e alteração de fonte limpam o detalhe antigo. Registros continuam no sistema de logs existente, sem novo cache pesado ou cópias de pacotes.

No clique direito de um mod com erro: “Ver erro da instalação” mostra o motivo e “Copiar erros da instalação” copia versão do aplicativo, hash do catálogo, IDs/variantes e mensagens de todos os itens falhos. O log também guarda a exceção completa e a versão. A mensagem de referência fora da lista fixa agora identifica o ID ausente; a validação continua bloqueando IDs desconhecidos. Aprovações e verificações de integridade não foram removidas.

## Diagnóstico dos arquivos enviados

Os dois diagnósticos recentes fornecidos pelo usuário registram SHA256`391b724376b5cfffaa8a4fa67d63a6897ecce1d8ff35c18be0dd8c2d04ba3a1a`, correspondente à1.1.23. Não registram o campo general.install.failed adicionado na1.1.24. Isso comprova qual executável gerou esses arquivos; não comprova que o processo remoto continue nessa versão depois do envio.

Todos os12ZIPs fornecidos na Área de Trabalho coincidem por SHA256 com as receitas atuais (loja raem, dez pacotes de acessórios/roupas/paleta e Reporter). Nenhum foi alterado, movido, apagado ou copiado ao cache. A varredura enviada registra ArchiveXL antigo como versão anterior reconhecida, não como arquivo arbitrário.

Reprodução em fixture: a1.1.23 falha com “Mod fora da lista fixa” ao validar um recibo antigo do FPV; a1.1.25 valida o mesmo tipo de recibo pelos hashes históricos, sem reintroduzir o drone no catálogo. Esse é o defeito de histórico corrigido na1.1.24 e preservado nesta revisão. Os logs antigos enviados não contêm a mensagem detalhada de cada falha: não concluir que não exista outro impedimento remoto. Atualizar de fato o programa e repetir uma instalação é a confirmação necessária.

## Testes

Fixtures verificam mensagem completa após DrawRows, diagnóstico com os dois erros, ação real de copiar, ID desconhecido na mensagem, rejeição de download antigo como se fosse o novo e preservação/reaproveitamento dos dois ZIPs corretos. Repetidos testes de recibos históricos, campos P2P, densidade e Setup. Catálogo, dependências, RED4ext, jogo e saves reais inalterados. Sem novo teste P2P de rede ou suíte geral completa nesta alteração localizada. Provas em`Validacoes/1.1.25.json`; detalhes privados em`T/R125/` e`Auditoria/Falhas-amigo-20261106/`.
