# Padrões de configuração do instalador 1.0.23

Captura dos ajustes salvos por Douglas em 30/10/2026, autorizada para compartilhamento como padrão inicial do pacote. Não sincroniza alterações posteriores do computador de origem.

O motor existente continua conferindo os pacotes e instalando as dependências. Depois de copiar os arquivos, aplica somente os padrões do mod escolhido e suas dependências. Se o código já estiver instalado e faltar somente a configuração, o plano aplica o padrão sem baixar o mod novamente. Não cria recibo falso de instalação de código.

## O que foi incluído

| Mod | Preferências |
| --- | --- |
| Drive Modes and Cruise Control 1.5 | Direção normal75%, Sport50%, Gentle65%; suavização desligada; sensibilidade por velocidade ligada. Aceleração normal50%, Sport100%, Gentle25%. Os demais ajustes salvos e atalhos também foram capturados. |
| Shift 1.14.0 | Arquivo JSON de preferências, incluindo câmera e efeito de velocidade; shake vibrations; efeito de velocidade no FOV desligado na captura. |
| Audioware | Seção própria do Mod Settings. |
| Cyberpsycho Encounters | Seção própria do Mod Settings; cooldown30min e multiplicador0.65 na captura. |
| Dark Future | Seção própria do Mod Settings. |
| Night City Allies | Seção própria do Mod Settings. |
| Unlimited Attribute Reset | Seção própria do Mod Settings. |
| NCPD Hotline | Idioma e preferências de interação; sem timestamps e estado de foco. |

Os oito registros estão em `FixedCatalog.Defaults`, cada um com mod, versão, destino, formato, conteúdo JSON de dados e SHA256. As receitas dos85mods permanecem iguais à1.0.22; RED4ext1.31.0 e dependências não mudaram.

## Preservação dos ajustes de cada amigo

JSON existente nunca é substituído. No `red4ext/plugins/mod_settings/user.ini`, o programa acrescenta somente as seções ausentes, sem modificar bytes das seções anteriores. Uma seção existente é considerada personalizada, mesmo que ainda use os valores originais do mod. Portanto, atualizar o executável não força estes novos valores sobre ajustes já salvos; o usuário pode ajustá-los pelo painel do próprio mod.

Backups e manifesto ficam em `.CoopModsOnline/defaults-backups/`. O motor prepara a operação inteira antes de escrever, usa cópia atômica e recusa mudanças concorrentes ou INI ambíguo. Em falha, reverte somente as próprias alterações que ainda tenham o hash esperado. Os padrões são dados pessoais do destino após a instalação: remover ou desfazer o mod os preserva.

## O que não foi exportado

Credenciais, tokens, sessões de Spotify ou navegador, caminhos pessoais, saves e calibração de controle não fazem parte do pacote. ITP foi auditado, mas suas preferências não diferenciavam do padrão; estado de perspectiva, gênero e sliders corporais não foram exportados. ImmersiveVDialogue não tinha arquivo salvo. Street Vendors mantém a configuração original do pacote. Nenhum arquivo executável de configuração Lua foi reaproveitado.

## Evidências

Suíte `--self-test`: regressões do monitor/fila existentes, validação dos padrões, preservação de edições, rollback, instalação normal e aplicação sem ZIP quando o código já está correto. Ensaios somente em fixtures. O jogo real foi lido para capturar preferências, sem escrita durante esta versão. Não houve execução de mod ou teste de dirigibilidade no jogo.
