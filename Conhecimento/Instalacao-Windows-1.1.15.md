# Instalação no Windows — 1.1.15

O download oficial passa a ser um instalador de verdade, não apenas o executável portátil. Ele instala o gerenciador para o usuário atual, cria o atalho do menu Iniciar, oferece atalho na área de trabalho e registra Mod Loader em Aplicativos instalados / Programas e Recursos, com versão, ícone e desinstalador. Não exige administrador e não instala mods no jogo durante essa etapa.

## Locais

Em uma instalação nova:

- Programa e desinstalador: `%LOCALAPPDATA%\Programs\Mod Loader`.
- Projetos, preferências, recibos e backups: `%LOCALAPPDATA%\ModLoader\Config`.
- Cache dos pacotes: `%LOCALAPPDATA%\ModLoader\Cache`.
- Logs: `%LOCALAPPDATA%\ModLoader\Logs`.

A preferência por tema/idioma/pasta monitorada continua no índice do projeto; os dados de cada projeto ficam em sua própria subpasta. A pasta monitorada para downloads do navegador continua sendo escolhida pelo usuário: ela é diferente do cache interno.

A biblioteca antiga em `%LOCALAPPDATA%\ModLoaderGeral` é reconhecida se ainda não existir uma biblioteca moderna. Cache e logs antigos em `%LOCALAPPDATA%\CyberpunkCoop` continuam sendo utilizados quando presentes. Nenhum pacote pesado é copiado ou movido apenas para mudar a organização. Diretórios modernos vazios não escondem a biblioteca antiga. Uma biblioteca moderna já existente tem prioridade. A seleção de caminhos não cria arquivos nem altera preferências; as guardas contra links/junctions continuam ativas.

Reinstalar ou atualizar usa a mesma identidade do aplicativo e o mesmo destino. Desinstalar o gerenciador remove seus arquivos conhecidos, atalhos e entrada do Windows, preservando projetos/configurações/cache e os mods já instalados no jogo. A lixeira dentro do programa continua sendo a ação separada para exclusão explícita dos mods rastreados.

## Distribuição e compilação

O asset público `Mod-Loader-1.1.15.exe` é o Setup. O executável interno é instalado como ModLoader.exe. O verificador de atualização anterior aceita o nome canônico do asset e abre a Release oficial; não executa instaladores automaticamente. O amigo só precisa desse Setup, sem ZIP, README, Inno Setup ou compilador. Os fontes e licenças do leitor de arquivos já continuam embutidos no aplicativo.

Build.ps1 compila a aplicação, confere recursos e gera também o instalador. O desenvolvimento requer Inno Setup 6 ou um caminho passado em -Compiler. O script de setup recebe a versão da aplicação e recusa um payload de outra versão. Usa a infraestrutura nativa de instalação/desinstalação; não há gerenciador de registro/desinstalador artesanal.

Requisitos do Setup: Windows10/11 x64 e .NET Framework4.7.2 ou mais recente. Se o Framework estiver ausente, o instalador informa a dependência; não baixa nem executa um runtime automaticamente. A comparação usa Release>=461808 na visão32bits do registro, conforme a documentação da Microsoft.

Fontes oficiais: [modo sem administrador](https://jrsoftware.org/ishelp/topic_setup_privilegesrequired.htm), [desinstalador](https://jrsoftware.org/ishelp/topic_setup_uninstallable.htm), [registro em Programas e Recursos](https://jrsoftware.org/ishelp/topic_setup_createuninstallregkey.htm), [detecção de .NET Framework](https://learn.microsoft.com/en-us/dotnet/framework/install/how-to-determine-which-versions-are-installed).

## Validação

A suíte completa da aplicação passou, com o teste novo de caminhos/preferências antigas e vazias. A instalação nativa foi testada com uma identidade temporária, pastas sob T e entrada temporária real de desinstalação no HKCU. Foram conferidos nome, versão, localização, hash do payload, atalho, reinstalação, execução do aplicativo instalado e desinstalação. A entrada temporária e os arquivos conhecidos foram removidos pelo próprio desinstalador; configurações/cache e um arquivo não rastreado foram preservados. A entrada de produção permaneceu intacta durante a simulação.

Depois do teste, o gerenciador1.1.15 foi efetivamente instalado neste PC, sem abrir/fechar outros programas ou tocar no jogo. A entrada Mod Loader1.1.15, o hash do binário e os dois atalhos foram conferidos; dois arquivos de preferências anteriores continuaram com o mesmo hash. O executável instalado também passou seu teste de caminhos. Não foram instalados mods, alterados saves ou atualizados RED4ext. Não houve teste em outro computador. Resultados e limites em Validacoes/1.1.15.json.
