# FPV nativo NASK — Mod Loader 1.0.28

Revisão de 02/11/2026. Corrige o falso conflito do FPV personalizado de Douglas. Nenhum arquivo do jogo real foi alterado nesta integração. As outras 94 receitas foram preservadas, inclusive as roupas da versão 1.0.27.

## Download e identificação

O loader continua obtendo o ZIP oficial Night City FPV 1.31.4, Nexus 31642/168826. Não existe download alternativo reempacotado: os três ajustes autorizados são aplicados na preparação, após conferir o hash da origem e antes de conferir o hash final. O ZIP completo continua com SHA256 6f0114f760f0d37d109f006955cc435d16a97b22a92c1987ec93a018b2183467.

- Código customizado idêntico ao aprovado: instalado correto, sem conflito nem reinstalação.
- Código oficial idêntico à origem: Setup NASK disponível; permite conversão com backup e reversão.
- Código diferente das duas identidades: continua protegido contra substituição automática.
- JSON de perfil ou calibração sozinho não comprova instalação; a conferência usa os 35 arquivos fixos. Os dez JSONs oficiais editáveis continuam preservados.

## Personalização aprovada

settings.lua: três limites de Center Sensitivity de 250 para 600. profile.lua: leitura de Center até 600. physics_model.lua: vortexRingThrustFluctuationPeak 0.08 para 0.15 e throttleChopTurbulenceWeight 0.35 para 1.00; maximumVortexRingThrustLoss 0.11 preservado. Nenhuma DLL, RED4ext, SDL ou fórmula de rates foi alterada nesta integração.

Perfil profiles/NASK.json: Roll Center 400 / Expo 0.50 / Max 660; Pitch e Yaw Center 500 / Expo 0 / Max 660; Feedforward 120; Tune Tightness 100; gravidade 11.340000152587891; massa 400 g; referência de força 8.4072429999999994; idle 7500 RPM; câmera 30 graus, FOV horizontal 135 e aspecto 16:9; throttle curve 0; bateria infinita. Esses são os valores do perfil final capturado e aprovado, não o antigo Nask667. O ajuste visual foi aprovado pelo usuário; nenhuma porcentagem objetiva de equivalência ao Uncrashed foi medida.

Instalação nova recebe NASK selecionado. Instalação existente recebe o perfil apenas se ausente e mantém a seleção, perfis, atalhos e calibração existentes. Para adotá-lo, selecionar NASK no menu do FPV. Não sobrescrever NASK existente para aplicar uma atualização silenciosa. O antigo Nask667 não é excluído.

## Uso

Conectar o controle USB antes de configurar. No CET, definir FPV Drone: Toggle em Bindings e, na mesma interface CET, abrir FPV Drone Controller Setup, escolher o dispositivo e calibrar seus próprios eixos. Depois, em Configurações > Mods > FPV Drone, escolher NASK e salvar/Overwrite se alterar valores. O perfil distribuído não contém GUID, calibração, ARM ou identidade do controle de Douglas. O usuário já pilotou e refinou o FPV com DualSense; o controle do destinatário exige configuração própria.

O arquivo é instalado pela estrutura legacy, sem deploy REDmod. A receita mantém RED4ext 1.31.0 e as dependências fixadas da base. O perfil sem Phantom Liberty não foi ampliado nesta mudança. O pacote completo continua usando a base NightCoop existente. O funcionamento do drone foi relatado pelo usuário; a integração do loader foi testada em ambiente isolado, sem novo voo ou ensaio de rede nesta entrega.

## Validações e reversão

908 verificações gerais em 24 relatórios; 136 verificações específicas com o ZIP oficial. Cobertura: transformação dos três scripts, instalação limpa, reconhecimento do jogo real somente por leitura, conversão oficial, preservação de perfil/índice, falha após escrita, recuperação antes de salvar Applied, remoção e restauração de originais, recibos antigos e recusa de alterações desconhecidas ou concorrentes. O backup da conversão é validado antes de restaurar. A reversão não substitui uma alteração posterior desconhecida.

As observações antigas de que os patches ainda não estavam no loader são históricas e foram superadas por esta versão. As fichas de Center e propwash preservam o histórico. Código personalizado pode diferir de uma instalação oficial de outro jogador; esta versão padroniza os mesmos arquivos aprovados para quem instalar por ela, sem desativar verificações de integridade.
