# Perfil sem Phantom Liberty — 1.0.28

Auditoria de 02/11/2026. Não houve instalação, remoção ou alteração do jogo. O executável 1.0.28 existente pode ser usado; esta conferência não exige uma nova versão.

## Resultado

Catálogo embutido com 95 componentes. O fechamento de dependências permite 86 sem DLC; BMW M4 CSL e Yamaha R1 continuam retidos por motivos anteriores, resultando em 84 linhas visíveis. Conferência feita com as funções do próprio EXE e com a janela real em modo de teste: as 84 linhas exibidas coincidem com a classificação e suas dependências. Nenhuma dependência necessária desse conjunto ficou de fora nem incluiu receita required/unverified.

Os 25 testes focados passaram: detecção pelo marcador original da expansão, filtro de dependências, seleção apenas das linhas visíveis, cancelamento da fila ao mudar perfil, bloqueio de download/instalação incompatível antes de copiar arquivos, importação sem autorização indevida e retorno ao perfil com DLC. Todos ocorreram em pastas sintéticas; navegador real não acionado pelos testes.

## Coop

NightCoop não exige Phantom Liberty, confirmado novamente na FAQ oficial em https://nightcoop.net/index.html usando o navegador integrado. Quando um convidado não tem o DLC, o anfitrião recebe a opção de reiniciar sem a expansão; nenhum arquivo precisa ser apagado. Saves feitos com a expansão não carregam nessa inicialização. Usar um save compatível com o jogo base ou iniciar uma campanha nova.

Todos devem usar a mesma versão do jogo e a mesma build do mod. Nossa receita é NightCoop 0.1.2-beta para Cyberpunk 2.5. Download da receita: https://nightcoop.net/dl/0.1.2-beta/NightCoop-0.1.2-beta-2.5.zip ; página oficial https://nightcoop.net/download.html . Esse componente não vem do Nexus. RED4ext 1.31.0 foi preservado.

## Itens excluídos do perfil

Exigem DLC declarado pelos autores, reconfirmado nas páginas atuais:

- Immersive Third Person — https://www.nexusmods.com/cyberpunk2077/mods/32203
- Night City Alive — https://www.nexusmods.com/cyberpunk2077/mods/10395 (inclusive nossa variante sem REDmod; REDmod e Phantom Liberty são exigências diferentes).

Sem confirmação suficiente para liberar a receita exata sem expansão, mantidos fora:

- Night City Allies.
- Cyberpsycho Encounters.
- EconomyPunk Standard.
- EXtra - Pose Pack.
- Night City FPV NASK.
- Vehicle Interior RT and Mirrors.
- Realistic Map 8K.

O estado unverified significa ausência de confirmação suficiente nesta auditoria, não incompatibilidade comprovada nem obrigação afirmada de comprar DLC. As outras fontes não foram todas reabertas em 02/11: foram utilizadas as fichas e inspeções de pacotes anteriores, além da conferência atual de todas as dependências do catálogo.

## Orientação para a sessão

No loader 1.0.28, escolher a pasta do jogo e o perfil Sem Phantom Liberty. O aplicativo tenta detectar a edição pelo marcador da expansão. O amigo pode selecionar os itens visíveis e usar a fila normal de baixar e instalar.

Trocar o perfil somente filtra a lista e cancela a fila anterior; NÃO desativa nem remove mods já instalados. Se o anfitrião já tem o conjunto completo com DLC, apenas selecionar Sem Phantom Liberty não iguala os arquivos entre os PCs. Preparar o mesmo conjunto para a sessão; investigar mensagens de divergência pelo log antes de remover arquivos. Não excluir a expansão, saves, pastas genéricas ou configurações para contornar isso.

A conclusão é documental e de funcionamento do instalador. Ainda não houve execução deste conjunto em um jogo realmente sem Phantom Liberty; por isso não afirmar compatibilidade runtime integral dos 84 itens.
