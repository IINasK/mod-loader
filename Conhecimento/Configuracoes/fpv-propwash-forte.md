# FPV — propwash forte para teste local

Aplicado em 2026-11-02, com autorização de Douglas e Cyberpunk fechado. Objetivo: tornar mais perceptível a turbulência ao atravessar a própria esteira e após cortes de aceleração. Não representa equivalência calibrada ao Propwash Medium do Uncrashed.

## Alteração aplicada

Arquivo relativo à raiz do jogo: `bin/x64/plugins/cyber_engine_tweaks/mods/FPVDrone/fpv/physics_model.lua`.

| Campo | Original | Teste |
| --- | --- | --- |
| vortexRingThrustFluctuationPeak | 0.08 | 0.15 |
| throttleChopTurbulenceWeight | 0.35 | 1.00 |
| maximumVortexRingThrustLoss | 0.11 | 0.11, preservado |

O pico configurado usa o máximo já permitido pelo código. Os limites existentes da oscilação e da força continuam ativos. Isso não significa 15% de propwash total nem um aumento perceptivo garantido: geometria, estado da esteira e controlador influenciam o resultado. Comentários originais próximos ao valor 0.08 descrevem a base do autor, não a calibração deste teste.

Somente dois valores mudaram. Nenhuma alteração de gravidade, massa, motor, rates, Feedforward, Tune Tightness, câmera, binds, calibração ou perfis. Conferência de integridade preservou 41 outros arquivos, incluindo os protegidos disponíveis de RED4ext e entrada. A ampliação anterior de Center para 600 permanece intacta.

## Evidência e recuperação

- SHA256 original: `8b1ec396bce1783c0e9dcbfc7bf73231fd18d1ab8b39eab2d59b436395836882`.
- SHA256 aplicado: `27b2253c3156e8a4da4794074e1e9556cd2a0db51f6dc37e953de547b0f36ca0`.
- Backup e relatório: `Auditoria/FPV-Propwash-Forte/20261102-173809/` dentro do projeto.
- Script de reversão na mesma pasta: `reverter-propwash-forte.ps1`. Exige jogo fechado e hashes correspondentes; recusa sobrescrever alterações posteriores. `-ConferirSomente` apenas confere, sem restaurar.
- A reversão restaura somente physics_model.lua. Não restaura perfis nem remove o patch de Center.

Verificações: substituições únicas, reversão dos dois valores reproduzindo bytes originais, hash final e 36 combinações matemáticas do envelope de turbulência. São verificações de integridade e limites, não validação de voo. Reversão conferida em modo sem escrita.

## Teste de voo

Reabrir o jogo. Manter demais configurações e comparar descida sobre a própria esteira, corte de aceleração e retomada. Procurar oscilação adicional durante a descida/retomada; não confundir com rapidez para iniciar um giro, que envolve a resposta do controlador. Sem mudança de números adicionais até observar esse teste.

Ainda sem teste de voo desta alteração. EXE e catálogo do instalador não foram atualizados. Reparar/reinstalar FPV pelo gerenciador pode restaurar a base. O Lua alterado pode produzir divergência na comparação de arquivos do coop: usar a mesma revisão nos dois PCs ou reverter antes da comparação. Não ocultar a divergência. Não foi feita medição de equivalência ao Uncrashed.

## Atualização 1.0.28 — 02/11/2026
Patch integrado nativamente ao loader, com fonte oficial, transformação conferida, backup e reversão. Douglas aprovou o comportamento do propwash. Ver fpv-nativo-1.0.28.md para o perfil NASK final; os ensaios acima são históricos.
