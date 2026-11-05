# Logo ML integrada — 1.1.9

Geometria aprovada por Douglas no Estudo 02: quatro caminhos vetoriais, hastes de 52 unidades e folgas retas de 16. O SVG original é incorporado no executável e desenhado nativamente com GraphicsPath, incluindo arcos. Sem dependência de navegador ou fonte para desenhar a marca.

## Cores reais do aplicativo

| Tema | M — GeneralStyle.Foreground | L e detalhe superior — GeneralStyle.Accent |
| --- | --- | --- |
| Claro (`light`) | `#221D33` | `#5B3FD8` |
| Escuro (`dark`) | `#ECE8F6` | `#B8A4FF` |
| Claro vermelho (`light-red`) | `#2B2326` | `#D92E42` |

Estas são as variantes integradas à paleta real do app. As cores azul petróleo `#194660` / azul claro `#76C5E3` ficam na marca original e no ICO fixo do executável, que o Windows não recolore automaticamente.

A mudança de tema atualiza a logo e os ícones das janelas pelo fluxo central GeneralStyle.Apply, inclusive janelas já abertas quando esse fluxo é aplicado. Ícones são reutilizados por paleta, com liberação do handle temporário de GetHicon. O controle da logo usa BackColor transparente e exibe o fundo do pai.

## Recursos e compilação

- `Assets/ModLoader.svg`: cópia exata do SVG aprovado, SHA256 `E2ED35028B5B3104A1269CE234B04C052FBB131E24F8847C6342C11AE3CBE233`.
- `Assets/ModLoader.ico`: versão azul transparente do kit aprovado, sete imagens 16/24/32/48/64/128/256 px, SHA256 `9967FE9CE16BBA9AEC6ADED9E4B8E1E571C359C27DEAE35D8D0CB7288066F8F8`.
- `preparar-logo.ps1` confere a identidade do par SVG/ICO e a transparência/dimensão dos quadros antes de compilar. Reexportar ambos e atualizar a conferência ao aprovar geometria nova. O script anterior gerava somente polígonos e não serve para os caminhos curvos desta marca.
- O leitor de SVG é restrito aos comandos absolutos M/L/A/Z do recurso embutido; não aceita scripts, imagens, links, DTD ou recursos externos. Não é importador de SVGs do usuário.
- Catálogo, mods e saves preservados. O instalador permanece um executável único com os recursos incorporados.

## Validação

128 verificações aprovadas: marca 45, Home 57 e isolamento de presets 26. Conferidas renderizações nativas de Home e tabela, transparência RGBA/ICO, cores das três paletas e atualização de ícone na mesma janela. Evidências locais em `Auditoria/Logo-20261105`.

O teste de fundo transparente precisa renderizar o painel pai; DrawToBitmap apenas do controle transparente deixa os pixels de fundo com alpha zero, em vez da cor do painel. A asserção foi corrigida para verificar a composição real.

Compilação e conferência dos recursos embutidos aprovadas. A suíte geral e suas limitações estão registradas na evidência JSON da revisão; não confundir os 128 testes específicos com a suíte inteira.
