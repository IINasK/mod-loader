# Correção de veículos e Mod Loader 1.1.20 — 06/11/2026

ArchiveXL atualizado de 1.28.2 para 1.28.6 no jogo e no catálogo. RED4ext 1.31.0 e os cinco arquivos da base preservados. Oito arquivos ArchiveXL e um YAML BMW alterados, com backups e hashes anterior/posterior.

BMW S1000RR: removido o bloco que clonava registros inexistentes de física. A herança da Arch permanece. A configuração personalizada antiga de suspensão/centro de massa deixa de ser aplicada; teste de dirigibilidade pendente. Isso elimina a referência inválida no arquivo, sem comprovar ainda o resultado visual no jogo.

Mod Loader instalado atualizado para 1.1.20. Catálogo mantém 138 receitas: somente ArchiveXL e BMW foram alteradas; 136 outras receitas idênticas. A migração aceita apenas os hashes anteriores conhecidos. O patch da BMW exige fonte e resultado exatos; alterações desconhecidas são recusadas. Backup das fontes anteriores em Auditoria/Veiculos-correcao-20261106.

Validação: compilação e Setup concluídos; aplicativo instalado confere com o SHA256 preparado. Doze verificações do motor real em fixtures aprovaram migração, patch, rejeição de identidade alterada, preservação da base, reinstalação e desfazer com proteção de alterações posteriores. Nenhuma DLL do jogo foi executada nesses testes.

A suíte geral parou em uma asserção histórica de catálogo facial: espera 57 itens, mas a preparação do EXE anterior 1.1.19 já retorna 58. Falha preexistente, não corrigida ampliando este escopo; não declarar suíte geral aprovada. Primeira tentativa do script real falhou antes de alterar arquivos por conversão do parâmetro nulo no PowerShell; tentativa corrigida aplicada, ambos manifestos preservados.

Não houve novo lançamento do Cyberpunk após a correção. Logs posteriores, aparência de Lexus/Audi/Bugatti/S1000RR e coop ainda precisam de conferência. Saves e calibragem FPV não foram alterados. Não houve publicação remota desta revisão.
