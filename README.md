# Mod Loader

<p><img src="Instalador/Online/Fonte/Assets/ModLoader.svg" alt="Mod Loader" width="112"></p>

Repositório público: [IINasK/mod-loader](https://github.com/IINasK/mod-loader). [Baixar o executável mais recente](https://github.com/IINasK/mod-loader/releases/latest).

Gerenciador de mods para Windows, com listas, presets e categorias definidos pelo usuário. Cyberpunk2077 2.5 é um pacote incluído; outros jogos podem ter suas próprias listas e destinos. Mantém downloads oficiais, identificação por SHA256, monitor da pasta Downloads, backup e desfazer. O usuário conclui os downloads gratuitos Manual/Slow no Nexus; o aplicativo confere o arquivo original e continua a fila autorizada. Receitas específicas e verificações de versão do pacote Cyberpunk não são inferidas para outros jogos.

A versão **1.1.8** mantém a seleção inicial de projetos, abre o Cyberpunk pronto diretamente e reutiliza sua biblioteca. O menu Projetos lista os pessoais antes dos padrões; os três riscos reúnem as ações de lista, Compartilhar e Opções em uma coluna. Menus fecham por segundo clique, sem X. Salvar, descartar e excluir preset ficam junto ao seletor; presets padrão são protegidos. Corrigidos o primeiro quadro das animações, as margens dos pop-ups, as bordas e o alinhamento do rodapé. A Home usa a mesma frase de créditos do editor. [Uso e validação](Conhecimento/Menus-e-estabilidade-1.1.8.md) · [Registro para melhoria do design](Conhecimento/Design/Registro-de-correcoes.md).

A versão1.1.6 consulta as Releases deste repositório uma vez por abertura, em segundo plano e sem login. Se existir uma versão estável mais nova com executável publicado, aparece um aviso para abrir a página oficial de download. Não troca nem executa programas automaticamente. Falta de internet não impede o uso. Presets novos não herdam categorias vazias do pacote; grupos que o usuário criar persistem e acompanham a lista exportada. O título da tabela tem um separador mais visível nos dois temas. Consulte [presets e atualizações](Conhecimento/Atualizacoes-e-presets-1.1.6.md).

A versão1.1.5 oferece **Compartilhar P2P > Exportar mods instalados**: um ZIP temporário com os arquivos do preset identificados no jogo, dependências e configurações permitidas. Funciona depois de excluir os downloads originais. O destinatário autoriza a pasta local, recebe, confere e instala automaticamente; os ZIPs são removidos após sucesso, sem cache permanente do pacote. Registros e backups locais permitem reconhecer, remover e reexportar os mods instalados. Não sincroniza a pasta inteira do jogo, saves ou contas. Consulte [uso e limites atuais](Conhecimento/Pacotes-instalados-1.1.5.md), [motor P2P e fontes](Conhecimento/Compartilhamento-P2P-1.1.4.md) e [interface1.1.3](Conhecimento/Interface-1.1.3.md).

## Organização

- `Instalador/Online/Fonte`: interface, catálogo, downloads, importação e testes.
- `Instalador/Fonte`: motor compartilhado, validação de caminhos, backup e fixtures.
- `Instalador/Online/Programa/catalogo.json`: receitas e fontes fixadas; não contém os mods.
- `Conhecimento/INDICE.md` e `Conhecimento/mods.json`: fichas dos138componentes do pacote Cyberpunk, função, uso, dependências, versões e evidências de compatibilidade;135entradas visíveis.
- `AGENTS.md`: histórico das decisões do projeto. As instruções antigas foram revogadas pelo usuário; as referências às auditorias locais não implicam inclusão dessas pastas no Git.

Os perfis Com/Sem Phantom Liberty usam a auditoria das dependências diretas e transitivas. Evidência documental/estática não substitui teste em um jogo semDLC. O FAQ do NightCoop permite jogar sem a expansão; saves que dependem dela não abrem nesse modo. Trocar a lista do instalador não apaga arquivos existentes nem desliga a expansão por conta própria.

Na versão1.0.24 há83mods visíveis comDLC e76semDLC. Night City FPV1.31.4 foi acrescentado para teste com as dependências existentes; scripts compilados isoladamente, RED4ext1.31.0 preservado. JSONs pessoais do FPV são semeados só se ausentes e preservados ao remover; código e ZIP continuam verificados por hash. EXtra e FL00R SHXT (poses), Unlimited Attribute Reset e No-Clip foram integrados para teste, sem atualização do RED4ext nem troca de corpo. A loja Nola x Veegee e os conjuntos Vintage, Rough e Lethal foram incluídos com suporte declarado ao corpo padrão feminino. ImmersiveThirdPerson eNightCityAlive exigem a expansão. NightCityAllies, CyberpsychoEncounters, EconomyPunkStandard, EXtra e Night City FPV ficam fora da opção semDLC até evidência suficiente para essas receitas; continuam na lista comDLC. A seleção usa também as dependências, não somente o requisito direto do mod.

Os [padrões de configuração1.0.23](Conhecimento/Configuracoes/padroes-1.0.23.md) incluem os ajustes de direção e câmera salvos, com preservação das configurações existentes de cada jogador.

A1.0.24 acrescenta o [perfil FPV Nask667 e o atalho de ativação](Conhecimento/Configuracoes/fpv-1.0.24.md), preservando perfis, atalhos e seleção anteriores. A curva é uma aproximação autorizada para Actual Rates; o mesmo perfil inclui gravidade e referência de força dos motores30% maiores. Calibração e identificação do controle não são compartilhadas.

## Leitor incorporado na versão 1.0.31

RAR e 7z agora são lidos pelo 7-Zip oficial 26.05, incorporado ao executável, sem depender do tar do Windows. O mesmo EXE inclui as licenças e o código-fonte correspondente original, acessíveis pelo menu Sobre e licenças. Nenhum mod acompanha o programa. Os 12 pacotes RAR/7z atuais (65 arquivos) foram conferidos com o leitor novo.

O catálogo atual tem 95 componentes: 93 linhas visíveis com Phantom Liberty e 84 sem a expansão; BMW/Yamaha continuam retidos. O botão Pular e continuar da 1.0.30 e o aceite/remoção rastreável de diferenças da 1.0.29 continuam disponíveis. O perfil FPV atual é NASK; Nask667 abaixo pertence ao histórico da 1.0.24. Consulte Conhecimento/Configuracoes/fpv-nativo-1.0.28.md para os valores finais aprovados.

## Compilar

A logo original está em `Instalador/Online/Fonte/Assets/ModLoader.svg`. `preparar-logo.ps1` gera o ícone do Windows diretamente desse vetor, sem ferramentas ou downloads adicionais. O mesmo SVG é desenhado no cabeçalho do aplicativo.

No Windows com .NETFramework4.x e PowerShell:

```powershell
./Build.ps1
```

A compilação usa o catálogo versionado e produz um único executável na raiz. Na primeira compilação, preparar-leitor.ps1 obtém os componentes e o código-fonte oficiais do 7-Zip 26.05, confere hashes fixados e prepara os recursos; o usuário final não precisa fazer esse download separado. Os binários de dependência ficam fora do Git e são reproduzíveis pelo script versionado. Nenhum mod, save, token, cookie ou credencial é empacotado. O catálogo inclui onze padrões de preferências sanitizados, autorizados pelo usuário para compartilhamento; são aplicados somente quando a configuração correspondente não existe. O catálogo embutido e o hash são recompilados juntos.

Os scripts históricos de remontagem do catálogo a partir dos pacotes baixados são ferramentas locais de auditoria: não são necessários para compilar o catálogo aprovado e não autorizam redistribuir arquivos de terceiros.

## Verificação e limites

Os testes rodam apenas em fixtures, com base inerte e navegação simulada. O modo `--self-test` exige um perfilWindows funcional para os recibos DPAPI e uma raiz curta de testes. Nunca execute fixtures como jogo ou confunda hashes válidos com sincronização emcoop.

No pacote Cyberpunk, preservar RED4ext1.31.0 e a base NightCoop de cinco arquivos; não atualizar dependências silenciosamente. Componentes retidos continuam registrados nas fichas. Mods personalizados usam arquivos e destinos explícitos, validação de caminhos e recibos próprios; o gerenciador não deduz requisitos de jogos desconhecidos.

## Conteúdo excluído

O Git usa lista explícita de arquivos permitidos. Downloads, mods originais, DLLs de terceiros, saves, caches, logs, backups, recibos e configurações pessoais permanecem apenas no computador. Cada autor mantém os direitos sobre seus mods; o gerenciador guarda links e receitas, sem reempacotá-los.

## Arquivos locais e limpeza

Os pacotes baixados, caches e cópias binárias de preparação/teste foram retirados da pasta do projeto após a instalação local. O código, o catálogo fixado, as fichas, os relatórios e os backups foram preservados. A compilação normal por Build.ps1 continua usando fontes e catálogo; ensaios específicos com pacotes originais exigem baixá-los novamente pelas fontes oficiais.

## Calibrador FPV 0.1.1

Ferramenta independente para comparar entradas e vídeos do Uncrashed/Cyberpunk. Fontes em Ferramentas/Calibrador FPV/Fonte; [uso, testes e limites](Conhecimento/Ferramentas/calibrador-fpv-0.1.1.md). Não muda o instalador ou o catálogo. Dependências e gravações ficam locais.
