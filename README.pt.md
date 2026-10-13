🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 [Français](README.fr.md) · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 **Português (Brasil)** · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS Meter

**O medidor de DPS gratuito para AION 2** — dano e cura ao vivo de todo o seu grupo, uma sobreposição que não intercepta cliques, rankings de chefes, um mapa de mascotes e coleta, uma agenda de eventos e temporizadores de chefes mundiais. Para Windows, de código aberto e nunca lê a memória do jogo.

<p align="center">
  <a href="../../releases/latest"><b>⬇ Baixar para Windows</b></a> · <a href="https://aiondps.com">Site</a> · <a href="https://aiondps.com/features">Todos os recursos</a> · <a href="https://aiondps.com/instances">Rankings de chefes</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="A janela do medidor: cada jogador com ícone de classe, DPS, dano total e uma barra de participação">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="O overlay que não captura cliques, como pequenas fichas sobre o jogo">
</p>

> Os nomes, jogadores e números nas imagens abaixo são dados de demonstração.

## Por que usar

- DPS, iDPS e HPS ao vivo de cada jogador do seu grupo ou raide
- Uma sobreposição que deixa os cliques passarem
- Detalhamento de habilidades com taxa de crítico e curva de DPS
- Rankings de chefes e perfis públicos de personagens em aiondps.com (envio opcional, desativado por padrão)
- Mapa interativo, agenda de eventos, temporizadores de chefes mundiais e sobreposição de rede
- Seguro: captura passiva de pacotes, sem ler a memória nem injetar código
- Gratuito, de código aberto (MIT) e se atualiza sozinho

Funciona com a versão global de AION 2 — todas as regiões lançadas pela NCSOFT em 5 de outubro de 2026. Coreia e Taiwan estão algumas versões à frente, então o reconhecimento de habilidades pode ser incompleto lá.

Um medidor de dano/cura e conjunto de ferramentas para **Aion 2**. Ele lê o tráfego de rede do próprio jogo na sua máquina
por meio do driver [Npcap](https://npcap.com) — de forma passiva: nunca envia um pacote e nunca toca no
processo do jogo nem na sua memória. Nada sobre a sua partida sai da sua máquina a menos que você o envie (veja
[Envio](#envio)); a única outra coisa que ele envia é a verificação de atualizações, que pergunta ao GitHub se existe uma versão
mais nova e pode ser desativada; veja [Atualizações](#atualizações).


## Conteúdo

[Instalação](#instalação) · [O que ele faz](#o-que-ele-faz) · [Como usar](#como-usar) · [Atalhos de teclado](#atalhos-de-teclado) ·
[Envio](#envio) · [Atualizações](#atualizações) · [Idiomas](#idiomas) · [Compilar a partir do código-fonte](#compilar-a-partir-do-código-fonte)

## Instalação

1. Instale o driver [Npcap](https://npcap.com) (o medidor precisa dele para ver o tráfego do jogo; ele
   não faz parte do instalador).
2. Baixe `AionDpsMeter-win-Setup.exe` da [versão mais recente](../../releases/latest) e execute-o.
   Ele é instalado no seu perfil de usuário e inicia o medidor — sem direitos de administrador, sem precisar do .NET.
3. Inicie o medidor e depois entre com o seu personagem. O medidor lê seu personagem, equipamento, habilidades e
   quadros de Daevanion diretamente do jogo; o servidor em que você joga é detectado automaticamente.

## O que ele faz

### Dano e cura ao vivo para todos

Cada jogador do seu grupo ou raide com nome, dano total, DPS e uma barra de participação; facção e classe são
exibidas como ícones. Filtre por classe, grupo ou alvo. O filtro **Mob/Boss** alterna entre o DPS geral e o
verdadeiro **iDPS** por alvo; os chefes são reconhecidos a partir dos dados do próprio jogo e exibidos pelo nome. Um
seletor **PvE/PvP** separa os dois, e um wipe em um chefe recebe uma entrada própria (uma caveira marca uma tentativa
após a qual o chefe voltou à vida cheia).

### Cinco visualizações, a um clique de distância

**Dano**, **Cura**, **Dano recebido** e duas visualizações **Tudo em Um**. Alterne com o atalho
(padrão **Ctrl+Alt+M**) ou clicando no rótulo do modo no overlay. *Tudo em Um* mostra dano recebido,
cura e total lado a lado, *Tudo em Um Compacto* mantém as conhecidas colunas de DPS e total e coloca
cura e dano recebido em uma pequena segunda linha abaixo do nome.

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="Visualização Tudo em Um: dano recebido, cura e dano total lado a lado">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="O overlay que não captura cliques, como pequenas fichas sobre o jogo">
</p>

### Um overlay que não captura cliques

**Ocultar UI** transforma a janela em pequenas fichas (ou um painel compacto) que ficam sobre o Aion 2 sem
capturar um único clique. Opacidade, barras de participação no dano, a vida do chefe e o dano recebido por jogador
são opções. O overlay só é desenhado enquanto o jogo está em primeiro plano. Ative-o de qualquer lugar com um atalho.

Opcional (**Configurações → Interface → Ocultar durante os menus do jogo**, desligado por padrão): o medidor e seus overlays saem do caminho enquanto um dos menus do jogo está aberto (mapa, inventário, legião, loja, Esc ...) e voltam quando você o fecha. Isso funciona pelo cursor do mouse: no modo de controle que esconde o cursor enquanto você joga, o jogo o mostra quando um menu abre; manter pressionada a tecla de mostrar o cursor (Alt por padrão) mantém o medidor visível para que você possa clicar nele, e **Ctrl+Alt+J** mostra os overlays durante o menu aberto. No modo de controle clássico o cursor está sempre visível, então os menus não podem ser distinguidos; o medidor percebe isso durante a luta e simplesmente continua visível.

### Detalhamento de habilidades de cada jogador

**Clique duas vezes** em um jogador: cada habilidade com acertos, taxa de crítico, total, mín., máx. e média, dividida por alvo,
além de uma curva de DPS ao longo de toda a luta. As taxas de crítico são lidas diretamente dos dados de acerto do servidor, portanto são exatas.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="Detalhamento de habilidades de um jogador com uma curva de dano ao longo do tempo">
</p>

### Rotação, efeitos e stagger

A mesma janela tem mais duas abas. **Rotação** desenha numa linha do tempo cada habilidade que o jogador usou, no momento em que a usou: as habilidades de apoio na faixa de cima, as de dano abaixo, e uma habilidade intercalada logo após outra, uma linha mais abaixo, de modo que uma sequência de intercalações parece uma escada. Por trás corre o DPS do jogador (a média dos últimos cinco segundos) e os momentos em que um monstro ficou em stagger. Arraste para se mover pelo combate, **Ctrl + roda** aplica zoom e, ao passar o cursor sobre um ícone, vê-se o uso (quando, qual uso de quantos, o tempo desde o anterior, o DPS naquele momento). Abaixo ficam a **Abertura** (as primeiras habilidades, por ordem) e a *rotação em resumo*: Usos, Por min, Intervalo entre dois usos, Primeiro e a parte do dano. **Efeitos** lista cada efeito que esteve no jogador com o seu tempo ativo, a parte que veio do próprio jogador, quantas vezes foi aplicado e quem o deu; um efeito que dois jogadores mantiveram ao mesmo tempo conta uma só vez.

Um combate guarda isto no histórico local, para que um chefe antigo possa ser visto de novo; os combates gravados por uma versão mais antiga não mostram rotação.

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="A aba Rotação da janela do jogador: habilidades numa linha do tempo com o DPS por trás, a abertura e a rotação em resumo">
</p>

### Todo o seu personagem num relance

A janela **Personagem** (ícone de pessoa) mostra o que o jogo envia sobre você: equipamento com nível de item e
encantamento (incluindo o estágio de Amplificação acima de +15), arcana, conhecimento de espécies, todas as habilidades com seus níveis
e os quadros completos de Daevanion. Ela guarda o seu último login, portanto nunca fica vazia. Passe o mouse sobre uma peça para ver seus
detalhes (pedras, atributos sorteados, pedra divina).

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="A janela Personagem com equipamento, níveis de item e encantamento">
</p>

### Mapa interativo com mascotes e coleta

Um overlay de mapa mostra a sua posição, os pontos de surgimento das mascotes que você procura e os pontos de coleta que você
ativou — ervas, minérios, madeira, gemas e mais — sem precisar alternar para um site com Alt+Tab. Só é listado o que a sua
própria facção pode usar, e a última posição conhecida permanece no mapa enquanto o jogo está fechado. As configurações de
mascotes listam todas as regiões de Verteron, Altgard e do Abismo com suas mascotes e o seu próprio nível de mascote, além de uma
aba **Raros** para mascotes com poucos pontos de surgimento. Setas na borda apontam para a mascote ou o item coletável mais próximo.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="O mapa interativo com pontos de surgimento de mascotes e a lista de mascotes">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="Configurações de coleta: ative o que aparece no mapa">
</p>

### Quadro de horários de eventos com lembretes

Um overlay lista os próximos eventos — Campo de Batalha (Battlefield), os cercos do Abismo (Artifact Siege), o Festival Shugo,
o Ataque Aéreo de Beritra, a Fenda do Espaço-Tempo e mais. Escolha um som e o volume por evento e seja lembrado
alguns minutos antes de ele começar. Um único botão liga ou desliga todos os lembretes.

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="Configurações de horários com um som e volume por evento">
</p>

### Chefes mundiais no seu overlay

Ative um chefe de campo para ver o seu cronômetro no overlay e ouvir o seu som assim que ele aparecer. Os horários
vêm diretamente do jogo; o servidor os envia para o mapa em que você está, e os outros mapas mostram os deles
depois que você passar por lá. Som e volume são definidos por chefe, agrupados por região.

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="Configurações dos chefes mundiais: cada chefe de campo por mapa com o tempo de reaparecimento, um interruptor, som e volume">
</p>

### Overlay de rede

Ping, jitter, tráfego, perda de pacotes e há quanto tempo você está online, medidos a partir do tráfego que o seu PC
já recebe. Os valores passam de verde para vermelho conforme a conexão piora. Escolha as linhas que deseja,
fixe-o no lugar e ative ou desative com um atalho.

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="O overlay de rede: ping, jitter, tráfego e perda de pacotes">
</p>

### Overlay de combate

Uma moldura transparente com até três elementos, cada um dos quais você posiciona e dimensiona separadamente:

- **Barra de conjuração do chefe:** no momento em que um chefe começa uma habilidade, você vê o nome dela, uma barra que diminui com o tempo restante e em quem ela está mirada (você em vermelho). Uma habilidade que atingiu vários jogadores é marcada como **ÁREA**; **LETAL** significa que o maior golpe que você recebeu dela até agora é pelo menos igual à sua vida atual. O medidor aprende cada habilidade (se é em área ou não, quando o dano dela chega) e a lembra entre as sessões.
- **Alvo do alvo:** se o alvo que você selecionou com Tab estiver mirando em alguém, você vê em quem: um chefe com imagem e nome, um jogador com o emblema da classe e o nome.
- **Alvo do curandeiro** (opcional): a barra de vida do alvo selecionado e os debuffs (ou buffs) nele.

Desbloqueie o overlay em **Configurações → Overlay de combate** para organizá-lo: arraste o corpo de um elemento para movê-lo, puxe as bordas para redimensioná-lo, puxe o canto para escalá-lo; a alça no canto inferior direito de toda a moldura escala todos os elementos juntos. Bloqueado, ele deixa os cliques passarem e só aparece enquanto o jogo está em primeiro plano. **Ctrl+Alt+C** o liga e desliga, assim como o atalho *Todos os overlays ligados / desligados*.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="O overlay de combate: duas conjurações de chefe com barra de tempo, o alvo do alvo e o alvo do curandeiro com vida e debuffs">
</p>

### Anúncios e Twitch Drops

Um botão de megafone abre os anúncios oficiais da NCSoft no aiondps.com — manutenção, quedas, novos
servidores, transferências de servidor, compensações — dos mais novos para os mais antigos, com uma etiqueta colorida por tipo e um filtro. Enquanto
uma campanha de Twitch Drops de AION 2 está em andamento, um botão Twitch abre uma página com as suas recompensas, as horas que você
precisa assistir, uma contagem regressiva e as transmissões que têm Drops ativados agora. Uma marca *Novo* aparece quando
algo é mais recente que a sua última visita.

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="Anúncios oficiais de AION 2 com filtro por tipo">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="Campanhas de Twitch Drops com recompensas e transmissões ao vivo">
</p>

### Histórico de lutas

As lutas são gravadas localmente para que você possa rever um chefe anterior (desative em
**Configurações → Interface**). O medidor é reiniciado após um tempo configurável fora de combate.

## Como usar

A gravação começa assim que o medidor está em execução. **Pausar descarta** em vez de adiar: os eventos
durante uma pausa são ignorados de vez, então retomar nunca reproduz uma luta da qual você ficou de fora.

- **Copiar** coloca na área de transferência um ranking de uma linha, pronto para o chat (`Nome 1.234.567 (890), …`); **Copiar tudo**
  gera uma tabela em markdown do Discord.
- A aparência é definida em **Configurações → Interface**: tema (escuro/claro), tamanho da fonte, idioma, aparência do overlay
  e opacidade, iniciar com o Windows.

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="Configurações: interface, overlay e comportamento">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="Configurações: atalhos de teclado">
</p>

## Atalhos de teclado

Em todo o sistema, também enquanto o jogo está em foco. Clique em um campo em **Configurações → Atalhos de teclado**, pressione a
combinação desejada (ela precisa de Ctrl, Alt, Shift ou Win); **×** a remove. Eles substituem os comandos de chat,
que não podem funcionar porque o chat do Aion 2 não é legível a partir do fluxo de rede.

| Padrão | Ação |
|---|---|
| **Ctrl+Alt+H** | Overlay (oculta a janela, mostra fichas) |
| **Ctrl+Alt+P** | Pausar / retomar |
| **Ctrl+Alt+D** | Copiar o ranking de dano |
| **Ctrl+Alt+Shift+X** | Limpar a sessão |
| **Ctrl+Alt+U** | Enviar dano do chefe |
| **Ctrl+Alt+M** | Próximo modo (cinco visualizações) |
| **Ctrl+Alt+T** | Overlay de horários |
| **Ctrl+Alt+K** | Mapa interativo ligado / desligado |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | Setas de borda para mascotes / itens coletáveis |
| **Ctrl+Alt+B** | Overlay de chefes mundiais |
| **Ctrl+Alt+N** | Overlay de rede |
| **Ctrl+Alt+C** | Overlay de combate |
| **Ctrl+Alt+J** | Mostrar overlays durante este menu |
| **Ctrl+Alt+O** | Overlay de atalhos (uma lista móvel dos atalhos) |
| **Ctrl+Alt+A** | Todos os overlays ligados / desligados |

## Envio

- **Lutas contra chefes** são enviadas quando você clica em enviar (botão da barra de ferramentas, menu Sessão ou **Ctrl+Alt+U**): o
  chefe, os jogadores que participaram, dano, cura, dano recebido e habilidades. Só são aceitos os chefes que o jogo
  anunciou e que o catálogo conhece. O envio automático está disponível e **desativado por padrão**.
- **O perfil do seu próprio personagem** (nome, classe, nível, equipamento, habilidades, Daevanion, legião, servidor, facção) pode
  ser enviado automaticamente alguns segundos após o login, para que você possa ser encontrado no site. Isso está
  **desativado por padrão**; ative em **Configurações**.
- Sem um envio, nada sai da sua máquina.

## Atualizações

O medidor se atualiza sozinho. Ele pergunta ao GitHub por uma versão mais nova na inicialização e a cada cinco minutos enquanto
está em execução, baixa-a em segundo plano e a ativa na próxima inicialização — sem instalador, sem aviso do
UAC. Quando uma atualização está pronta, uma linha verde aparece na linha de status (e um selo no
overlay transparente); clicar nela oferece reiniciar imediatamente. **App → Verificar atualizações** faz o mesmo sob demanda.
Uma versão antiga continua enviando por 72 horas depois que a sucessora foi lançada, e depois precisa ser atualizada.

A verificação lê uma única URL e não envia nada além da própria requisição:

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Desative em **Configurações → Atualizações**; o item de menu continua funcionando quando está desativado.

## Idiomas

Inglês, alemão, francês, espanhol, russo, chinês (simplificado e tradicional), português (Brasil),
japonês e coreano — definido em **Configurações → Interface → Idioma**. Este README também está disponível em
[Deutsch](README.de.md), [Español](README.es.md), [Français](README.fr.md), [Русский](README.ru.md),
[中文](README.zh.md), [繁體中文](README.zh-TW.md), [Português (Brasil)](README.pt.md),
[日本語](README.ja.md) e [한국어](README.ko.md).

## Compilar a partir do código-fonte

```
cd Client
dotnet build
dotnet run -- selftest                              # self-checks (protocol, capture, decoding, ...)
dotnet run -- aion2-record <out.jsonl>              # record the game's traffic ("stop" ends it)
dotnet run -- aion2-replay <file.jsonl>             # replay a recording through the real decoder
dotnet run -- aion2-upload-dryrun <file.jsonl>      # build the uploads of a recording, send nothing
```

Somente Windows (WPF). `Tools/aion2-dat` lê as tabelas de texto do jogo (nomes em oito idiomas); veja o README dele.

## Uma observação sobre as regras dos servidores

O medidor apenas observa passivamente o tráfego de rede do próprio jogo. Mesmo assim, as publicadoras definem suas próprias
regras sobre ferramentas de terceiros — vale conferir os termos do jogo antes de usá-lo.

## Aviso legal e licença

Aion 2 e todos os nomes, textos e arte relacionados são propriedade da NCSOFT. Este projeto não é afiliado, endossado nem conectado à NCSOFT; o conteúdo do jogo permanece com o seu proprietário. O código-fonte é publicado sob a [Licença MIT](LICENSE) — a licença cobre apenas o código, não qualquer conteúdo do jogo.
