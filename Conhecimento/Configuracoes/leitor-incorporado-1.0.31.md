# Leitor incorporado — Mod Loader 1.0.31

## Diagnóstico e correção

Douglas relatou que o amigo encontrou LZMA codec is unsupported ao ler Ava Complexions All in one 1.7 e perguntou se isso seria causado pelo mod, aplicativo ou idade do PC. A captura comprova falha do descompactador tar do Windows que a implementação anterior usava. O pacote original é fixado por hash e foi extraído/conferido com êxito usando o leitor novo. Não há evidência de defeito físico nesse erro específico; CPU, RAM e armazenamento afetam tempo/recursos, não acrescentam suporte de codec a um programa.

A 1.0.31 substitui a dependência de tar.exe para RAR/7z pelo CLI completo oficial 7-Zip 26.05 x64 e sua 7z.dll, sem modificações. 7za/7zr reduzidos não são o leitor distribuído: o CLI completo inclui o suporte RAR. O Windows do usuário não precisa ter um descompactador instalado. ZIP continua no leitor .NET existente e arquivos raw mantêm o caminho anterior.

O leitor é materializado em diretório temporário novo, privado para a conta Windows/SYSTEM, com executável/DLL e pacote de entrada travados para leitura e conferidos por tamanho e SHA. O processo não usa shell, não abre janela e tem PATH restrito ao diretório de sistema. Não existe procura por tar.exe, 7-Zip ou WinRAR na máquina do destinatário. A limpeza libera processos/travas e remove apenas o diretório temporário próprio.

## Limites de segurança preservados

O pacote precisa continuar coincidindo com tamanho e SHA fixados. A listagem técnica é lida uma vez, com UTF-8, teto de 16 MiB, 20 mil entradas e timeout; são recusados links simbólicos/físicos, reparse points, tipos não regulares, caminhos inseguros, duplicatas, criptografia, fluxos alternativos e volumes divididos. A extração seleciona a entrada exata com curingas desativados e transmite somente seus bytes por stdout. Não entrega ao 7-Zip um diretório de extração de mods.

O limite é 8 GiB por entrada e 10 minutos de execução por extração. O fluxo tem de alcançar EOF, coincidir com o tamanho listado e terminar com confirmação do extrator (incluindo CRC) antes de encaminhar eventual divergência para a confirmação manual da 1.0.29. Pular e continuar da 1.0.30 continua disponível em falhas; não mascara erros de integridade. O motor instala apenas os destinos fixados e preserva arquivos existentes desconhecidos.

## Distribuição e reconstrução

Entrega continua um único EXE: catálogo, leitor oficial, licenças completas e fonte correspondente original estão embutidos. Não inclui mods, saves ou credenciais. Menu contextual Sobre e licenças permite ler os termos e salvar 7z2605-src.tar.xz com hash conferido. O código-fonte do leitor é o original da mesma versão dos binários; estes são executados como programa separado, sem alterações. Licenças LGPL/BSD/unRAR e créditos do autor acompanham a distribuição.

Build.ps1 chama preparar-leitor.ps1. Quando os componentes não existem, o script obtém os pacotes oficiais com hashes fixados, usa o bootstrap oficial apenas na preparação da compilação e extrai os componentes necessários. Nenhum instalador do 7-Zip é executado. Binários, fonte upstream e intermediários ficam fora do Git; código de integração, pins e script de reconstrução são versionáveis.

## Evidências

- 1.017 verificações em 28 relatórios, incluindo 41 do leitor: LZMA/LZMA2/PPMd, nomes literais com colchetes, trava do pacote, metadados inseguros, fluxo incompleto, integridade, licenças e exportação da fonte.
- Os 12 pacotes RAR/7z originais da lista passaram, totalizando 65 arquivos com hashes fixados, incluindo Ava Complexions, Material Override/PPMd e RARs de roupas e olhos.
- Fonte 7-Zip 26.05, instalador x64 e bootstrap foram baixados do repositório oficial indicado pelo site. Os três hashes coincidem com os digests publicados nos assets da release do GitHub.
- Catálogo de 95 componentes inalterado, incluindo filtro sem Phantom Liberty com 84 itens visíveis. RED4ext 1.31.0, FPV NASK, roupas, perfis pessoais, fila e remoção preservados. Nenhuma instalação no jogo real nesta entrega.

Hashes upstream:

- 7z2605-x64.exe: fd99e86f57d8f429ef5ed045bf98315af962e2309b0f1ba87a147a44c71647d0.
- 7z.exe: 94205cfc054dd3bbeb978905bc619a65b3f04e9e73bb2717841110b6f46d962e.
- 7z.dll: 78bf6b668d75801e3aa180b4ccb16e6d8395fba80dec8a065055bb6f8dfabb82.
- Fonte correspondente: 0b6080b8c3e7a579161d2d495224c0e51c467576cb4c7048b0ab485ef44cc22e.

## Compatibilidade e limites da conclusão

O site oficial informa suporte a 7z/LZMA/LZMA2 e extração RAR, além de versões antigas do Windows. O programa continua x64, como Cyberpunk 2077. O leitor não exige upgrade de hardware para acrescentar esses codecs. Em PCs lentos, a operação pode levar mais tempo; isso é separado do suporte ao formato. A compatibilidade do jogo, GPU, RAM e dos mods continua sujeita aos requisitos próprios.

Não houve teste no PC remoto nem em todas as versões antigas do Windows. O teste local confirma os mesmos pacotes e elimina a dependência do leitor variável do Windows; o amigo ainda deve testar a 1.0.31 no ambiente dele. Não alegar diagnóstico de saúde do hardware ou compatibilidade irrestrita de qualquer arquivo RAR/7z do mundo.

## Fontes primárias consultadas em 03/11/2026

- https://www.7-zip.org/ — formatos e sistemas suportados.
- https://www.7-zip.org/download.html — pacotes oficiais, bootstrap e fonte.
- https://www.7-zip.org/license.txt — termos de uso/distribuição.
- https://github.com/ip7z/7zip/releases/tag/26.05 — release e digests dos pacotes.
- https://api.github.com/repos/ip7z/7zip/releases/tags/26.05 — digests conferidos na obtenção.
