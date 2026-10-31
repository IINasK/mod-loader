# Calibrador FPV 0.1.1

Ferramenta separada do instalador, criada em31/10/2026 para comparar Uncrashed e o drone FPV do Cyberpunk. Um único EXE com bibliotecas e licenças embutidas. Usa o ViGEmBus1.22.0 já existente neste computador; não instala nem atualiza drivers. Não altera RED4ext, mods, presets, calibração ou saves dos jogos por conta própria.

## Fluxo aprovado pelo usuário

- F10 inicia obrigatoriamente pelo Uncrashed e confirma a próxima etapa após reset/reposicionamento. Não usa R3 para avançar.
- Antes de cada etapa, mantém throttle e eixos neutros em zero por1s. Isso padroniza a entrada; não detecta pouso nem garante que um drone alto/tombado ficou nivelado.
- Nenhum L1 automático. O operador deixa cada drone pronto/armado conforme o jogo.
- Etapa vertical: subida100% pela duração configurada, corte para0, aguarda F10 depois do impacto observado. Timeout limita a etapa; não existe detecção de impacto.
- Etapas yaw/roll/pitch: subida curta, baseline, pulso de25/50/75/100% conforme seleção, retorno a eixo zero. Potência durante o giro é um valor configurável e fixo, não os50% do joystick com mola centralizado. Repetições1..3.
- Salva cada etapa; não grava durante reposicionamento. Ao concluir Uncrashed, envia Esc para pausar, focaCyberpunk e envia Esc para retomar, iniciando a mesma série.
- Para essa troca, Uncrashed deve começar pronto para voar e Cyberpunk deve estar no menu principal de pausaEsc. O programa não reconhece menus. Configurações profundas/CET abertos ou um CP já rodando quebram essa suposição.
- F11 interrompe imediatamente e manda potência0. Perda de foco durante preparo/gravação aborta; desconexão física remove o virtual. Nenhuma finalização de vídeo pode bloquear a liberação de potência.
- F4 libera o controle físico para calibrar/reposicionar, exigindo stick esquerdo no mínimo; F5 volta a zero e preserva a etapa em espera. Durante uma etapa, F5 interrompe a série.

## Primeira configuração

Pause os dois jogos antes de conectar. Abra o EXE, clique Ler DualSense e Conectar controle virtual. Selecione/calibre o Xbox360virtual nos dois jogos. Para mover os eixos em calibração, mantenha a potência física no mínimo e use F4. Depois F5 mantém0. O dispositivo físico e o virtual possuem calibrações separadas; verificar os indicadores dos jogos é necessário para conferir o comando recebido.

Use modo Acro. Salve os perfis, feche painéis/CET e prepare os estados de pausa descritos acima. F6 no jogo salva uma imagem do recorte para verificar captura/horizonte. Use janela sem bordas se a captura ficar preta. Marque a confirmação de preparo e use F10. Entre etapas, reset/recoloque pelo jogo, depois F10. Primeiro ensaio deve ser curto para validar eixos e captura antes da série completa.

O usuário confirmou sua convenção: stick esquerdo cima=throttle+, esquerda/direita=yaw; stick direito direita=roll+ e stick direito cima deve produzir narizparaCIMA. O macro envia rightYpositivo (stickup) e registra o efeito esperado; não inverte automaticamente a calibração dos jogos. O desenho enviado pelo usuário mostrava outra convenção, portanto a verificação visual nos dois jogos permanece necessária.

## Vídeos e dados

Saída em Documentos/FPV Calibrador/Serie-.../01-Uncrashed e02-Cyberpunk. Cada etapa tem clip.avi(MJPEG30fps, semáudio), frames.csv, capture.json e geometry.json. O recorte central é960×540 limitado à área disponível. Não captura o restante da janela/monitor. Poucos pixels seriam insuficientes para analisar giros e impacto; o horizonte deve continuar visível.

commands.csv registra relógio monotônico, evento, estado, trecho, entradas físicas e saídas enviadas. Vídeo e comandos usam o mesmo Stopwatch. Frames ausentes são duplicados para conservar duração real; duplicatas e timestamps das capturas são explícitos em frames.csv. geometry.json contém tamanho da janela, recorte e resolução. O agendamento do Windows não é tempo real e esses logs não equivalem à telemetria recebida pelo jogo.

session.json inclui configurações do ensaio e uma seleção segura dos parâmetros do perfilFPV salvo: rates, gravidade, peso, referência do motor e câmera. Não copia GUID, caminho do DualSense, calibração, credenciais ou saves. O yaw informado por imagens foi870/0/250; alterações ainda não salvas no perfil podem divergir dessa leitura.

Rotação pode ser estimada por referências do cenário e tempo entre voltas; aceleração/atraso exigem observar transientes. Subida/corte/queda misturam empuxo, velocidade inicial, gravidade e arrasto: não identificar gravidade absoluta só pelo vídeo sem escala/hipóteses adicionais. O programa não calcula novos parâmetros automaticamente nesta versão.

## Verificação

263asserts aprovados: gravador48, sequência178, conversões de gamepad17, ordem/estruturaWindows7, UI/filtros/recorte/política8, validação de atalhos5. Leitura física confirmou DualSense; probeViGEm respondeu sem criar virtual; probejanelas encontrou Uncrashed eCyberpunk3440×1440 sem mudar foco, capturar ou enviar entrada. Revisão independente corrigiu quatro falhas de fluxo/log/finalização. Ainda não houve criação real do virtual, injeção nos jogos, captura real de voo ou troca automática real; esses passos serão validados com o operador.

O instalador1.0.24 e seu catálogo85 permaneceram intactos. Versões, hash, relatório e limites estão em calibrador-fpv-0.1.1.json. Evidências completas e backup original do controle ficam somente em Auditoria/Calibrador-FPV-20261031; não enviar esse backup, que contém identificação do dispositivo.

## Código e compilação

Fontes em Ferramentas/Calibrador FPV/Fonte. preparar-calibrador-dependencias.ps1 obtém o cliente oficialNuGet1.21.256 por hash, extrai seu cliente nativo e reaproveita a SDL3auditada do FPV instalado, com licenças. compilar.ps1 usa csc/.NETFramework4 e produz o EXE na raiz. Dependências, vídeos, EXEs e auditorias não vão aoGit.

Fontes primárias: [ViGEmClient](https://github.com/nefarius/ViGEmClient), [NuGet1.21.256](https://www.nuget.org/packages/Nefarius.ViGEm.Client/1.21.256), [SDL3 axes](https://wiki.libsdl.org/SDL3/SDL_GamepadAxis), [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput), [FAQUncrashed](https://steamcommunity.com/app/1682970/discussions/0/6643422659556525796/). A build local do Uncrashed registrada é26145167. Bibliotecas embutidas são infraestruturas públicas de controle; nenhum pacote de mod ou asset do jogo foi incluído.

## Correção dos atalhos na0.1.1

A0.1.0 usouF12 para emergência, mas essa tecla é reservada pelo Windows para depuradores. O bloqueio de segurança impedia iniciar comF10. A0.1.1 usaF11, mantémF10 e informa exatamente a tecla indisponível com erroWindows. Não remove o bloqueio caso falte parada de emergência. Testes validam a lista real registrada (semF12), todos os estados de disponibilidade e registro nativo deF11. A sondagem deF10 encontra1409 enquanto a0.1.0 ainda está aberta; fechar a versão antiga antes de abrir a nova é obrigatório. Nenhuma recalibração ou alteração de arquivos do jogo foi feita.

Fonte: [RegisterHotKey Microsoft](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey).
