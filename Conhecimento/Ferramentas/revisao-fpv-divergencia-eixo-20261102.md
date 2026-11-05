# FPV — divergência de eixo no ensaio de 00:28 — 02/11/2026

Após orientar Pitch Center500/Expo0/Max660, preservando Roll400/0,50/660, Douglas informou “feito”. Encontrada série20261102-002855-Serie-Cyberpunk-b46ce769/02-Cyberpunk.

NASKsalvo00:28:42 e snapshot SavedCyberpunkProfile de session.json às00:28:55 registram Roll500/0/660, Pitch400/0/660 eYaw400/0/660. Feedforward100, Tightness100, câmera0/FOV125; gravidade11,3400001526, massa400g e motor/TWR8,407243 preservados. Não interpretar o teste comoPitch500. Snapshot salvo não é telemetria contínua da memória do jogo.

Na leitura inicial havia11capture.json concluídos,1382quadros,0duplicados/erros registrados e nenhumcomplete.json. Isso é estado parcial observado, não declaração de falha da série ou do jogo. Nenhuma inspeção visual desta série foi usada para recomendar novo rate dePitch. Não apagar ou misturar com a referência deRoll400 anterior.

Comunicar a divergência de maneira direta, sem atribuir culpa ou afirmar que o usuário não salvou: houve gravação efetiva, mas com eixo/Expo diferentes do ensaio proposto. Para retomar o ensaio dePitch: restaurarRoll400/0,50/660; colocarPitch500/0/660; manterYaw400/0/660, Feedforward100/Tightness100, câmera0 e física; salvarOverwrite antes de gravar. A proposta não foi aplicada pelo agente, e não é calibração concluída.

Nenhum arquivo do jogo, save, Lua, RED4ext ou executável foi alterado. Snapshot privado deNASK/session emAuditoria/Comparacao-FPV-20261102/00-28-Divergencia-Eixo. Não publicar perfil pessoal noGit.
