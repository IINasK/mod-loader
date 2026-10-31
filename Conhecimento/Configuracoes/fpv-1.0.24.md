# FPV: atalhos e curvas do pacote 1.0.24

Solicitação de Douglas em30/10/2026: compartilhar o atalho salvo do drone e aproximar a curva Betaflight RC Rate1, Super Rate0.7, Expo0 nos três eixos. O usuário escolheu explicitamente uma aproximação em Actual Rates.

## Perfil Nask 667

Roll, pitch e yaw usam Center Sensitivity200, Max Rate667graus/s e Expo0.55. O perfil foi derivado do5INCHFS salvo, alterando esses três parâmetros de cada eixo e, após nova solicitação do usuário, gravidade e força de motor. Os oito padrões da1.0.23, as85receitas, versões e hashes dos pacotes originais foram preservados.

Instalação nova: o índice recém-copiado do ZIP oficial pode selecionar o novo perfil automaticamente. Instalação existente: a seleção atual é preservada, mesmo quando o índice ainda tem exatamente o conteúdo de fábrica. Para usar a nova curva, escolha Nask667 em Configurações > Mods > FPV Profiles & Setup, no seletor de perfis. Alterações posteriores só ficam no perfil ao usar Overwrite Profile.

O arquivo `profiles/Nask 667.json` é criado somente se ausente. O gerenciador pode adicionar o perfil a um drone já instalado sem baixar o ZIP novamente. Remover ou desfazer o drone preserva perfis e atalhos.

## Aproximação matemática

A versão1.31.4 usa Actual Rates; não existe o trio RC Rate/Super Rate/Expo clássico na interface. Referência primária: [Betaflight rc.c, funções applyBetaflightRates e applyActualRates](https://github.com/betaflight/betaflight/blob/master/src/main/fc/rc.c).

Para stick normalizado x, a curva clássica pedida é200x/(1-0.7|x|), terminando em666.6667graus/s. A curva do perfil é200x+467|x|(0.55x^5+0.45x), terminando em667graus/s. Foram preservados a inclinação de200 no centro e o máximo arredondado pedido; Expo0.55 aproxima o restante da resposta.

Comparação em20001posições entre-1e1: erro máximo inferior3.6graus/s e RMS inferior2.1graus/s. São diferenças de setpoint, não medições de voo. Física, PID, calibração, câmera e resposta do controle ainda influenciam a sensação. Não afirmar equivalência exata ao Betaflight clássico.

## Atalhos CET

Foi capturado somente `FPVDrone.fpv_drone_toggle`, inteiro64bits61924494876344320. A mescla acrescenta comandos ausentes sem substituir comandos existentes, inclusive valor0. A tecla do overlay CET e os outros mods não são exportados nem substituídos. Uma tecla já ocupada por outro comando não é duplicada; o registro orienta escolher manualmente em CET > Bindings. Atalhos conflitantes podem, portanto, precisar de escolha no computador de destino.

O JSON existente é lido como dados; números64bits mantêm precisão, com backup, gravação atômica e rollback condicional do mecanismo1.0.23. JSON malformado é preservado e recusa a operação. O índice FPV só pode ser alterado se estiver ausente ou se esta instalação acabou de copiar a semente original; não basta ter aparência de fábrica.

## Controle individual

O botão ARM e os eixos estão em `red4ext/plugins/FPVDroneInput/controller-profile.json`. Esse arquivo guarda GUID, caminho do dispositivo e calibração dos sticks. Não foi copiado para o instalador. Cada jogador deve selecionar e calibrar o próprio controle e vincular ARM. O pacote não inclui dados de conta, credenciais ou identificação do DualSense.

## Testes e limites

Testes de mescla com números64bits, preservação de outros mods, conflitos de tecla, zero existente, JSON inválido, rollback, seleção inicial somente na instalação nova e preservação na remoção. A suíte usa arquivos e executáveis inertes em diretórios isolados. Nenhuma DLL do drone foi executada, não houve teste de voo ou modificação no jogo real nesta versão. RED4ext1.31.0 preservado.

## Ajuste adicional: gravidade e motor

O usuário pediu mais30% de gravidade e mais30% de motor. A gravidade do perfil passou de9.80665 para12.748645units/s². A referência Thrust-to-Weight Ratio passou de10.65000057220459 para13.845000743865968, multiplicando por1.3 a referência de força estática do conjunto motor/ESC/bateria. Massa, hélices, RPM e arquivos de código permanecem iguais. O modelo separa gravidade da referência de força; aumentar ambos na mesma proporção conserva a relação teórica força/peso, embora a resposta dinâmica mude. O próprio mod limita a força quando o conjunto simulado não alcança o valor solicitado; não prometer30% de empuxo medido sem teste de voo.

Fonte local primária: fpv/settings.lua (Thrust-to-Weight Ratio e Gravity), fpv/profile.lua (limites0.25..20 e1..25), fpv/physics_model.lua (referenceMaximumTotalForce calculada com gravidade padrão fixa). Perfil customizado e testes matemáticos não executam nem modificam esses scripts.
