# Mod Loader Cyberpunk coop

Repositório privado: [IINasK/mod-loader-cyberpunk-coop](https://github.com/IINasK/mod-loader-cyberpunk-coop).

Gerenciador Windows de um catálogo fixado de mods para Cyberpunk2077 2.5, com downloads oficiais, identificação por SHA256, monitor da pasta Downloads, backup e desfazer. O usuário conclui os downloads gratuitos Manual/Slow no Nexus; o aplicativo confere o arquivo original e continua a fila autorizada.

## Organização

- `Instalador/Online/Fonte`: interface, catálogo, downloads, importação e testes.
- `Instalador/Fonte`: motor compartilhado, validação de caminhos, backup e fixtures.
- `Instalador/Online/Programa/catalogo.json`: receitas e fontes fixadas; não contém os mods.
- `Conhecimento/INDICE.md` e `Conhecimento/mods.json`: fichas dos80componentes, função, uso, dependências, versões e evidências de compatibilidade.
- `AGENTS.md`: histórico das decisões do projeto. As instruções antigas foram revogadas pelo usuário; as referências às auditorias locais não implicam inclusão dessas pastas no Git.

Os perfis Com/Sem Phantom Liberty usam a auditoria das dependências diretas e transitivas. Evidência documental/estática não substitui teste em um jogo semDLC. O FAQ do NightCoop permite jogar sem a expansão; saves que dependem dela não abrem nesse modo. Trocar a lista do instalador não apaga arquivos existentes nem desliga a expansão por conta própria.

Na versão1.0.20 há78mods visíveis comDLC e73semDLC. A loja Nola x Veegee e os conjuntos Vintage, Rough e Lethal foram incluídos com suporte declarado ao corpo padrão feminino. ImmersiveThirdPerson eNightCityAlive exigem a expansão. NightCityAllies, CyberpsychoEncounters eEconomyPunkStandard ficam fora da opção semDLC até evidência suficiente para essas receitas; continuam na lista comDLC. A seleção usa também as dependências, não somente o requisito direto do mod.

## Compilar

No Windows com .NETFramework4.x e PowerShell:

```powershell
./Build.ps1
```

A compilação usa o catálogo versionado e produz um único executável na raiz. Nenhum mod, save, token, cookie ou preset pessoal é empacotado. O catálogo embutido e o hash são recompilados juntos.

Os scripts históricos de remontagem do catálogo a partir dos pacotes baixados são ferramentas locais de auditoria: não são necessários para compilar o catálogo aprovado e não autorizam redistribuir arquivos de terceiros.

## Verificação e limites

Os testes rodam apenas em fixtures, com base inerte e navegação simulada. O modo `--self-test` exige um perfilWindows funcional para os recibos DPAPI e uma raiz curta de testes. Nunca execute fixtures como jogo ou confunda hashes válidos com sincronização emcoop.

Preservar RED4ext1.31.0 e a base NightCoop de cinco arquivos; não atualizar dependências silenciosamente. BMW/Yamaha permanecem retidos pelas falhas reproduzidas. Arquivos desconhecidos não recebem instalação genérica.

## Conteúdo excluído

O Git usa lista explícita de arquivos permitidos. Downloads, mods originais, DLLs de terceiros, saves, caches, logs, backups, recibos e configurações pessoais permanecem apenas no computador. Cada autor mantém os direitos sobre seus mods; o gerenciador guarda links e receitas, sem reempacotá-los.

## Arquivos locais e limpeza

Os pacotes baixados, caches e cópias binárias de preparação/teste foram retirados da pasta do projeto após a instalação local. O código, o catálogo fixado, as fichas, os relatórios e os backups foram preservados. A compilação normal por Build.ps1 continua usando fontes e catálogo; ensaios específicos com pacotes originais exigem baixá-los novamente pelas fontes oficiais.
