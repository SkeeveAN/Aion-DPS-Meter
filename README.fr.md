🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 **Français** · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS – Daevas Power Suite

**L'application compagnon gratuite et open source pour AION 2** — dégâts et soins en direct pour tout votre groupe, une surcouche qui laisse passer les clics, des classements de boss, une carte des familiers et de la récolte, un calendrier d'événements et des minuteurs de boss mondiaux. Pour Windows, open source, et il ne lit jamais la mémoire du jeu.

<p align="center">
  <a href="../../releases/latest"><b>⬇ Télécharger pour Windows</b></a> · <a href="https://aiondps.com">Site web</a> · <a href="https://aiondps.com/features">Toutes les fonctions</a> · <a href="https://aiondps.com/instances">Classements de boss</a>
</p>

<p align="center">
  <a href="../../releases/latest"><img src="https://img.shields.io/github/v/release/SkeeveAN/Aion-DPS-Meter?label=release" alt="release"></a>
  <a href="../../releases"><img src="https://img.shields.io/github/downloads/SkeeveAN/Aion-DPS-Meter/total?label=downloads" alt="downloads"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/license-MIT-blue" alt="MIT"></a>
  <img src="https://img.shields.io/badge/platform-Windows-lightgrey" alt="Windows">
</p>

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="440" alt="La fenêtre du compteur : chaque joueur avec icône de classe, DPS, dégâts totaux et barre de part">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="320" alt="L'overlay traversable sous forme de petites pastilles au-dessus du jeu">
</p>

> Les noms, joueurs et chiffres des images ci-dessous sont des données de démonstration.

## Pourquoi l'utiliser

- DPS, iDPS et HPS en direct pour chaque joueur de votre groupe ou raid
- Une surcouche qui laisse passer les clics
- Détail des compétences avec taux de critique et courbe de DPS
- Classements de boss et profils publics de personnages sur aiondps.com (envoi facultatif, désactivé par défaut)
- Carte interactive, calendrier d'événements, minuteurs de boss mondiaux et surcouche réseau
- Incantations de boss annoncées par la voix, journal de butin, outil d'attributs, calculateur de craft et checklist quotidienne
- Sûr : capture passive des paquets, aucune lecture de la mémoire, aucune injection
- Gratuit, open source (MIT) et se met à jour tout seul

Fonctionne avec la version mondiale d'AION 2 : toutes les régions lancées par NCSOFT le 5 octobre 2026. La Corée et Taïwan ont quelques versions d'avance, la reconnaissance des compétences peut donc y être incomplète.

Un compteur de dégâts/soins et une boîte à outils pour **Aion 2**. Il lit le trafic réseau du jeu sur votre machine
grâce au pilote [Npcap](https://npcap.com) — passivement : il n'envoie jamais de paquet et ne touche ni le
processus du jeu ni sa mémoire. Rien de votre partie ne quitte votre machine, sauf si vous l'envoyez (voir
[Envois](#envois)) ; l'autre chose qu'il envoie est une vérification des mises à jour, qui demande à GitHub si une
version plus récente existe et peut être désactivée ; voir [Mises à jour](#mises-à-jour).


## Sommaire

[Installation](#installation) · [Ce qu'il fait](#ce-quil-fait) · [Utilisation](#utilisation) · [Raccourcis clavier](#raccourcis-clavier) ·
[Envois](#envois) · [Mises à jour](#mises-à-jour) · [Langues](#langues) · [Compiler depuis les sources](#compiler-depuis-les-sources)

## Installation

1. Installez le pilote [Npcap](https://npcap.com) (le compteur en a besoin pour voir le trafic du jeu ; il ne
   fait pas partie de l'installateur).
2. Téléchargez `AionDpsMeter-win-Setup.exe` depuis la [dernière version](../../releases/latest) et lancez-le.
   Il s'installe dans votre profil utilisateur et démarre le compteur — pas de droits administrateur, pas de .NET.
3. Lancez le compteur, puis connectez-vous avec votre personnage. Le compteur lit votre personnage, équipement,
   compétences et plateaux Daevanion directement dans le jeu ; le serveur sur lequel vous jouez est détecté
   automatiquement.

## Ce qu'il fait

### Dégâts et soins en direct pour tout le monde

Chaque joueur de votre groupe ou raid avec son nom, ses dégâts totaux, son DPS et une barre de part ; faction et
classe sont affichées sous forme d'icônes. Filtrez par classe, groupe ou cible. Le filtre **Mob/Boss** bascule
entre le DPS global et l'**iDPS** réel par cible ; les boss sont reconnus à partir des données du jeu et affichés
par leur nom. Un commutateur **JcE/JcJ** sépare les deux, et un wipe sur un boss a sa propre entrée (un crâne
marque une tentative après laquelle le boss a retrouvé toute sa vie).

### Cinq vues, à un clic d'écart

**Dégâts**, **Soins**, **Dégâts reçus** et deux vues **Tout en un**. Changez avec le raccourci (par défaut
**Ctrl+Alt+M**) ou en cliquant sur le libellé du mode dans l'overlay. *Tout en un* affiche côte à côte dégâts
reçus, soins et total ; *All in One Compact* garde les colonnes DPS et total habituelles et place les soins et les
dégâts reçus dans une petite seconde ligne sous le nom.

<p align="center">
  <img src="Web-Frontend/images/features/mode-all@2x.webp" width="460" alt="Vue Tout en un : dégâts reçus, soins et dégâts totaux côte à côte">
  <img src="Web-Frontend/images/features/overlay-chips@2x.webp" width="380" alt="L'overlay traversable sous forme de petites pastilles au-dessus du jeu">
</p>

### Un overlay traversable

**Hide UI** transforme la fenêtre en petites pastilles (ou en panneau compact) posées sur Aion 2 sans intercepter
le moindre clic. Opacité, barres de part de dégâts, vie du boss et dégâts reçus par joueur sont des options.
Le panneau compact peut, en option, laisser passer les clics de la souris vers le jeu. L'overlay ne s'affiche que lorsque le jeu est au premier plan. Activez-le de n'importe où avec un raccourci.

Facultatif (**Paramètres → Interface → Masquer pendant les menus du jeu**, désactivé par défaut) : le compteur et ses overlays s'effacent tant qu'un menu du jeu est ouvert (carte, inventaire, légion, boutique, Échap ...) et reviennent quand tu le fermes. Cela repose sur le curseur de la souris : dans le mode de contrôle qui masque le curseur pendant que tu joues, le jeu l'affiche à l'ouverture d'un menu ; maintenir la touche d'affichage du curseur (Alt par défaut) garde le compteur visible pour que tu puisses cliquer dessus, et **Ctrl+Alt+J** affiche les overlays pendant le menu ouvert. Dans le mode de contrôle classique, le curseur est toujours affiché, donc les menus ne se distinguent pas ; le compteur le remarque pendant le combat et reste simplement affiché.

### Détail des compétences pour chaque joueur

**Double-clic** sur un joueur : chaque compétence avec ses coups, son taux de critique, son total, son min, son max
et sa moyenne, répartis par cible, plus une courbe de DPS sur tout le combat. Les taux de critique sont lus
directement dans les données de coups du serveur ; ils sont donc exacts. Le tableau montre aussi comment les coups d'une compétence ont porté : **Dos**, **Face**, **Parfait**, **Double** et **Multi** (plusieurs frappes en un seul coup) en part de ses coups directs, lue dans les données de coups. Double (environ le double des dégâts), Parfait (un petit bonus) et Parade ont été vérifiés d'après les dégâts qu'ils infligent ; Dos et Face proviennent d'un octet de direction qui n'a pas encore été vérifié dans le jeu, et la fenêtre le signale.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="Détail des compétences d'un joueur avec une courbe de dégâts dans le temps">
</p>

### Rotation, effets et stagger

La même fenêtre a deux onglets de plus. **Rotation** dessine sur une frise chronologique chaque compétence utilisée par le joueur, au moment où il l'a utilisée : les compétences de soutien sur la ligne du haut, les compétences de dégâts en dessous, et une compétence enchaînée juste après une autre une ligne plus bas, de sorte qu'une série d'enchaînements forme un escalier. Derrière, on voit le DPS du joueur (la moyenne des cinq dernières secondes) et les moments où un monstre a été en stagger. Faites glisser pour vous déplacer dans le combat, **Ctrl + molette** zoome, et en survolant une icône, vous voyez l'usage (quand, quel usage sur combien, le temps écoulé depuis le précédent, le DPS à ce moment). En dessous se trouvent l'**Ouverture** (les premières compétences dans l'ordre) et la *rotation en bref* : Usages, Par min, Écart entre deux usages, Premier et la part des dégâts. **Effets** liste chaque effet qui était sur le joueur avec sa durée d'activité, la part venant du joueur lui-même, le nombre de fois où il a été appliqué et qui l'a donné ; un effet que deux joueurs ont maintenu en même temps ne compte qu'une fois.

Les sessions enregistrées dans un fichier avec les données de rotation (voir Sessions) les montrent de nouveau une fois chargées.

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="L'onglet Rotation de la fenêtre du joueur : compétences sur une frise chronologique avec le DPS derrière, l'ouverture et la rotation en bref">
</p>

### Replay d'un combat de boss (test précoce)

Dans la fenêtre du joueur d'un combat de boss, **Replay** rejoue le combat vu de dessus : votre groupe sous forme de points aux couleurs des classes avec les dernières secondes de leur déplacement, le boss avec sa direction de regard et sa santé, les zones des compétences qu'il prépare (ambre pendant la préparation, rouge à l'impact ; en pointillés tant que la taille n'est qu'une estimation), un anneau rouge sur celui qu'il a touché, les morts barrés. Sous la carte, une chronologie montre les grandes attaques du boss (ambre), les coups sur le groupe (rouge), les morts, les stagger (grisés) et la santé du boss ; cliquez ou faites glisser dessus pour aller à cet instant. Lecture et pause (Espace), vitesses de la moitié au quadruple, ← et → avancent ou reculent de cinq secondes, faites glisser la carte, molette pour zoomer, double-clic pour la recadrer.

Les positions sont les relevés propres au jeu : il les envoie avec les mouvements et les compétences, pas à intervalle régulier, c'est pourquoi un déplacement est tracé en ligne droite entre deux d'entre elles. Le jeu ne dit jamais quelle est la taille de la zone d'une attaque : le meter apprend la taille de chaque compétence d'après qui elle a touché et la retient d'un combat à l'autre. Seuls votre groupe et les boss sont affichés, jamais les autres joueurs. Un combat conserve son replay dans l'historique local ; les combats enregistrés avec une ancienne version n'en ont pas.

<p align="center">
  <img src="Web-Frontend/images/features/replay@2x.webp" width="640" alt="Le replay d'un combat de boss : le groupe autour du boss, une zone en préparation, la chronologie avec la santé du boss">
</p>

### Tout votre personnage d'un coup d'œil

La fenêtre **Personnage** (icône de personne) montre ce que le jeu envoie à votre sujet : équipement avec niveau
d'objet et enchantement (y compris le niveau d'Amplification au-delà de +15), arcanes, connaissance des espèces,
toutes les compétences avec leurs niveaux et les plateaux Daevanion complets. Elle garde votre dernière connexion
et n'est donc jamais vide. Survolez une pièce pour ses détails (pierres, statistiques obtenues, pierre divine).

Le compteur remarque aussi lequel de vos **presets de compétences** est actif et conserve chaque preset qu'il a vu : niveaux des compétences, stigmas et pierres. L'onglet des compétences montre chaque preset avec les pierres de ses compétences, et l'infobulle d'une compétence liste les pierres et les effets qu'un stigma débloque, avec les mots du jeu.

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="La fenêtre Personnage avec équipement, niveaux d'objet et enchantement">
</p>

### Carte interactive avec pets et récolte

Une carte en overlay montre votre position, les points d'apparition des pets que vous traquez et les points de
récolte que vous avez activés — herbes, minerais, bois, gemmes et plus — sans alt-tab vers un site web. Seul ce
que votre propre faction peut utiliser est listé, et la dernière position connue reste sur la carte tant que le
jeu est fermé. Les réglages des pets listent chaque région de Verteron, d'Altgard et de l'Abysse avec ses pets et
votre propre niveau de pet, plus un onglet **Rares** pour les pets ayant peu de points d'apparition. Des flèches
au bord indiquent le pet ou la ressource la plus proche. Dans un donjon ou une instance de groupe, la carte bascule sur cette instance (25 cartes tirées des images du jeu, pour les instances qui ont des monstres pets) et montre ses monstres pets.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="La carte interactive avec points d'apparition de pets et liste des pets">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="Réglages de récolte : activez ce qui apparaît sur la carte">
</p>

### Horaires des événements avec rappels

Un overlay liste les prochains événements — Champ de bataille, sièges de l'Abysse (Siège d'artefact), Fête des
Shugo, Raid aérien de Beritra, Faille spatio-temporelle et plus. Choisissez un son et un volume par événement et
soyez prévenu quelques minutes avant son début. Un seul commutateur active ou désactive tous les rappels. Les heures suivent l'horloge UTC du serveur de jeu : elles sont donc identiques dans toutes les régions, été comme hiver, et s'affichent à l'heure de votre PC. Les boss exécuteurs de l'Abysse (lun, jeu, sam) et le Seigneur gardien Nahma (ven, dim) sont aussi listés, et dix sons de notification sont au choix.

<p align="center">
  <img src="Web-Frontend/images/features/timetable@2x.webp" width="380" alt="Réglages des horaires avec un son et un volume par événement">
</p>

### Les boss du monde sur votre overlay

Activez un boss de terrain pour voir son minuteur sur l'overlay et entendre son son dès qu'il apparaît. Les
horaires viennent directement du jeu ; le serveur les envoie pour la carte où vous vous trouvez, les autres cartes
affichent les leurs une fois que vous y êtes passé. Son et volume se règlent par boss, regroupés par région.

<p align="center">
  <img src="Web-Frontend/images/features/worldboss@2x.webp" width="380" alt="Réglages des boss mondiaux : chaque boss de terrain par carte avec son temps de réapparition, un interrupteur, un son et un volume">
</p>

### Overlay réseau

Ping, gigue, trafic, perte de paquets et temps passé en ligne, mesurés à partir du trafic que votre PC reçoit
déjà. Les valeurs passent du vert au rouge quand la connexion se dégrade. Choisissez les lignes souhaitées,
verrouillez-le en place, activez-le ou désactivez-le avec un raccourci.

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="L'overlay réseau : ping, gigue, trafic et perte de paquets">
</p>

### Calculateur de stats (bêta)

Voyez ce qu'un changement de stats donne sur vos dégâts avant de remplacer une pièce. Il part de vos propres stats telles que le jeu les envoie ; vous indiquez de quel côté vous attaquez surtout (face, dos, je ne sais pas) et votre taux de crit, ou vous reprenez les deux de votre dernier combat. Saisissez un changement dans n'importe quelle stat et lisez les dégâts avec, face aux dégâts actuels, ainsi que la quantité de chaque stat qui donne un pour cent de plus. Le mode expert ajoute les buffs et la résistance du boss et reste en bêta. Le résultat est une estimation, car la formule de dégâts du jeu n'est pas publiée : il indique « 3,8 % de plus », jamais « tant de dégâts ». **Démarrer l'enregistrement** note vos stats et les coups d'un combat pendant 10 minutes au plus ; **Envoyer** les transmet après avoir montré exactement ce qui part - ni nom, ni légion, ni adresse IP, seulement un identifiant aléatoire de votre installation - pour améliorer le calculateur. L'idée vient du Stat Efficiency Calculator de [RATmeter](https://github.com/Kuroukihime/AIon2-Dps-Meter) ; la mise en page, le code et la formule sont les nôtres.

### Rapport réseau

Sous **Outils → Réseau** : **Enregistrer le rapport** écrit les 15 dernières minutes de votre connexion (ping, gigue, pertes de paquets, temps de réponse du routeur, d'internet et du serveur de jeu, coupures et leur cause, type d'adaptateur) dans un fichier JSON formaté nommé `aiondps_VERSION_DATE_HEURE.json`, à l'endroit de votre choix ; **Envoyer le rapport** transmet le même rapport au développeur. Il ne contient aucune adresse IP de votre part ni aucun nom (ni du PC, du compte Windows, d'un adaptateur, d'un personnage ou d'une légion) ; avant l'envoi, une fenêtre liste ce qu'il contient et ce qu'il ne contient pas. L'idée d'un rapport de diagnostic enregistré vient du diagnostic d'environnement du meter Aletheia.

### Overlay de combat

Un cadre transparent avec jusqu'à trois éléments, que tu places et dimensionnes chacun séparément :

- **Barre d'incantation du boss :** dès qu'un boss commence une compétence, tu vois son nom, une barre qui se vide avec le temps restant et qui elle vise (toi en rouge). Une compétence qui a touché plusieurs joueurs est marquée **ZONE** ; **MORTEL** signifie que le coup le plus fort que tu en as reçu jusqu'ici est au moins égal à ta santé actuelle. Le compteur apprend chaque compétence (de zone ou non, quand ses dégâts arrivent) et s'en souvient d'une session à l'autre.
- **Cible de la cible :** un bloc qui se déplace, se redimensionne et se met à l'échelle d'un seul tenant, avec des éléments que tu peux aussi placer séparément à l'intérieur. Si la cible sélectionnée avec Tab vise elle-même quelqu'un, tu vois qui : un boss avec son image et son nom, un joueur avec l'emblème de classe et son nom. La **barre du soigneur** (facultative) ajoute au bloc la barre de vie de cette cible et ses debuffs (ou buffs).

Déverrouille l'overlay dans **Paramètres → Combat de boss** pour l'organiser : fais glisser le corps d'un élément pour le déplacer, tire sur ses bords pour le redimensionner, tire sur son coin pour le mettre à l'échelle ; la poignée en bas à droite de tout le cadre met tous les éléments à l'échelle ensemble. Verrouillé, il laisse passer les clics et ne s'affiche que lorsque le jeu est au premier plan. La voix Windows choisie peut aussi **annoncer les incantations de boss** à voix haute (avec ou sans le nom de la compétence, vers toi, vers tout le monde, mortelle), même avec l'overlay désactivé tant qu'Aion est au premier plan ; les paramètres proposent des phrases d'exemple à écouter. **Ctrl+Alt+C** l'active et le désactive, tout comme le raccourci *Tous les overlays activés / désactivés*.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="L'overlay de combat de boss : deux incantations de boss avec barre de temps et le bloc « Cible de la cible » avec image, nom, barre de vie et debuffs">
</p>

### Butin, attributs et checklist quotidienne

La fenêtre **Outils** les liste à gauche. L'outil **Butin** montre ce que tu ramasses au sol, par session et par carte, sous forme de page et d'overlay (**Ctrl+Alt+L**). L'outil **Attributs** montre l'anneau des attributs avec tes points, ce que fait chaque attribut et chaque valeur de Seigneur (0,1 % par point), tous les bonus additionnés, et un onglet de statistiques qui explique chaque statistique avec sa part venant de l'anneau. La **checklist** garde tes tâches quotidiennes et hebdomadaires, lit dans le jeu les quêtes quotidiennes obligatoires et l'achat hebdomadaire d'énergie d'Odyle, et laisse le reste à cocher à la main. Chaque outil mémorise sa propre largeur et sa propre hauteur.

### Calculateur de craft

Marque les recettes que tu veux fabriquer et combien de fois (pour tout le compte, afin que tes seconds personnages les voient aussi, ou pour un seul personnage), et le **calculateur de craft** liste, recette par recette, les matériaux qui te manquent encore. Les produits intermédiaires sont décomposés en ce que tu dois vraiment récolter : trois marteaux de forgeron demandent 18 lingots, soit 36 minerais, et les lingots que tu possèdes déjà comptent en premier. Le stock est lu dans le jeu, depuis l'inventaire, l'entrepôt du personnage et l'entrepôt du compte, et un matériau qui se trouve dans un entrepôt l'indique. Laisse la page ouverte sur un deuxième écran ou affiche la même liste en overlay dans le jeu (**Ctrl+Alt+R**). Les recettes viennent des tables du jeu ; l'ordre de ta liste décide quelle recette reçoit le stock en premier. Connecte-toi une fois pour que le stock soit lu ; ce que tu déplaces entre l'inventaire et un entrepôt apparaît après le prochain tri.

### Annonces et Twitch Drops

Un bouton en forme de mégaphone ouvre les annonces officielles de NCSoft sur aiondps.com — maintenance, pannes,
nouveaux serveurs, transferts de serveur, compensations — les plus récentes en premier, avec une étiquette colorée
par type et un filtre. Tant qu'une campagne de Twitch Drops AION 2 est en cours, un bouton Twitch ouvre une page
avec ses récompenses, les heures à regarder, un compte à rebours et les streams où les Drops sont activés en ce
moment. Une pastille *Nouveau* s'affiche quand quelque chose est plus récent que votre dernière visite.

<p align="center">
  <img src="Web-Frontend/images/features/announcements@2x.webp" width="430" alt="Annonces officielles d'AION 2 avec filtre par type">
  <img src="Web-Frontend/images/features/twitchdrops@2x.webp" width="430" alt="Campagnes Twitch Drops avec récompenses et streams en direct">
</p>

### Le meter dans votre stream (OBS, Streamlabs)

Affichez le meter en direct par-dessus votre jeu sans filmer sa fenêtre : activez **Paramètres → Streaming → Overlay de stream pour OBS activé**, puis utilisez **Copier le lien pour OBS** (ou, dans l'icône de la zone de notification, *Copier le lien de l'overlay de stream (OBS)*), ajoutez une source **Navigateur** dans OBS ou Streamlabs, collez le lien et réglez la taille de la source. La page suit le meter en direct : la vue actuelle (dégâts, soins, dégâts subis, le tout en une), les joueurs avec icône de classe et barres, et la santé du boss. Ajoutez `?scale=1.5` ou `?rows=5` au lien pour l'agrandir ou afficher moins de lignes.

Il fonctionne aussi avec **deux PC** (le jeu sur l'un, OBS sur l'autre) : copiez le lien sur le PC de streaming, il contient l'adresse du PC de jeu dans votre réseau. Seul votre propre réseau peut y accéder, jamais Internet ; le lien contient une clé aléatoire (**Nouvelle clé** invalide l'ancien lien), seul `GET` est servi, la page ne charge rien d'ailleurs et rien n'est envoyé à personne. Windows peut demander une fois d'autoriser le compteur dans le pare-feu : autorisez-le pour les réseaux privés. Les noms des joueurs sont **désactivés par défaut** - chacun apparaît avec sa classe, comme dans les envois - et peuvent être activés dans la même carte de paramètres.

### Sessions

Chaque carte et chaque instance est enregistrée comme session à part, pour revoir un passage précédent. Un contrôle de plausibilité compare les dégâts aux points de vie perdus par le boss.

<p align="center">
  <img src="Web-Frontend/images/features/sessions@2x.webp" width="760" alt="Gestionnaire de sessions et analyseur de combat dans une fenêtre">
</p>

Le gestionnaire de sessions et l'**analyseur de combat** partagent une fenêtre avec une séparation déplaçable : choisissez une session à gauche, et à droite s'ouvrent les détails du joueur (compétences, rotation, effets, carte de replay), limités à cette session. Au-dessus de la liste des joueurs, vous passez entre **dégâts, soins et dégâts subis** ; la liste montre les morts et le temps passé mort. Le compteur continue d'enregistrer en arrière-plan. **Enregistrer dans un fichier** peut emporter la rotation, les effets et les données de replay (une case à cocher mémorisée ; le fichier devient bien plus gros, environ 0,5 à 5 Mo pour un combat de boss de cinq minutes), pour qu'un fichier chargé les montre aussi. La fenêtre mémorise sa position et la séparation.

## Utilisation

L'enregistrement démarre dès que le compteur tourne. **La pause jette** au lieu de différer : les événements
pendant une pause sont perdus, la reprise ne rejoue donc jamais un combat que vous avez laissé passer.

- **Copy** place un classement d'une ligne, prêt pour le chat, dans le presse-papiers (`Nom 1.234.567 (890), …`) ;
  **Copy All** donne un tableau Markdown pour Discord.
- L'apparence se règle sous **Paramètres → Interface** : thème (sombre/clair), taille de police, langue, aspect et
  opacité de l'overlay, démarrage avec Windows.
- Vos réglages survivent à un plantage : le fichier de réglages est écrit en une seule fois, et une copie du dernier démarrage est conservée à côté. Si le fichier est un jour endommagé, cette copie est restaurée (le fichier endommagé est conservé à côté). **Paramètres → Général → Réinitialiser tous les réglages** demande confirmation deux fois et peut être annulé pendant 30 secondes ; vos choix d'envoi et de confidentialité, la vérification des mises à jour et votre personnage restent tels quels.

<p align="center">
  <img src="Web-Frontend/images/features/settings@2x.webp" width="340" alt="Paramètres : interface, overlay et comportement">
  <img src="Web-Frontend/images/features/hotkeys@2x.webp" width="340" alt="Paramètres : raccourcis clavier">
</p>

## Raccourcis clavier

Valables partout, même quand le jeu a le focus. Cliquez sur un champ dans **Paramètres → Raccourcis clavier**,
appuyez sur la combinaison voulue (elle exige Ctrl, Alt, Maj ou Win) ; **×** la supprime. Ils remplacent les
commandes de chat, qui ne peuvent pas fonctionner car le chat d'Aion 2 n'est pas lisible dans le trafic réseau.

| Par défaut | Action |
|---|---|
| **Ctrl+Alt+H** | Overlay (masquer la fenêtre, afficher les pastilles) |
| **Ctrl+Alt+P** | Pause / reprise |
| **Ctrl+Alt+D** | Copier le classement des dégâts |
| **Ctrl+Alt+Shift+X** | Vider la session |
| **Ctrl+Alt+U** | Envoyer les dégâts du boss |
| **Ctrl+Alt+M** | Mode suivant (cinq vues) |
| **Ctrl+Alt+T** | Overlay des horaires |
| **Ctrl+Alt+K** | Carte interactive activée / désactivée |
| **Ctrl+Alt+E** / **Ctrl+Alt+G** | Flèches au bord pour les pets / les ressources |
| **Ctrl+Alt+B** | Overlay des boss du monde |
| **Ctrl+Alt+N** | Overlay réseau |
| **Ctrl+Alt+C** | Overlay de combat |
| **Ctrl+Alt+L** | Overlay de butin |
| **Ctrl+Alt+R** | Overlay de craft |
| **Ctrl+Alt+J** | Afficher les overlays pendant ce menu |
| **Ctrl+Alt+O** | Overlay des raccourcis (une liste déplaçable des raccourcis) |
| **Ctrl+Alt+A** | Tous les overlays activés / désactivés |

## Envois

- **Les combats de boss** sont envoyés quand vous cliquez sur envoyer (bouton de la barre d'outils, menu Session ou
  **Ctrl+Alt+U**) : le boss, les joueurs présents, dégâts, soins, dégâts reçus et compétences. Seuls les boss
  annoncés par le jeu et connus du catalogue sont acceptés. L'envoi automatique est disponible et **désactivé par
  défaut**.
- **Votre propre profil de personnage** (nom, classe, niveau, équipement, compétences, Daevanion, légion, serveur,
  faction) peut être envoyé automatiquement quelques secondes après la connexion, pour que l'on vous trouve sur le
  site. C'est **désactivé par défaut** ; à activer dans les **Paramètres**.
- Sans envoi, rien ne quitte votre machine.

## Mises à jour

Le compteur se met à jour tout seul. Il demande à GitHub une version plus récente au démarrage puis toutes les
cinq minutes pendant qu'il tourne, la télécharge en arrière-plan et l'installe au prochain démarrage — ni
installateur ni UAC. Quand une mise à jour est prête, une ligne verte apparaît dans la barre d'état (et une pastille
sur l'overlay transparent) ; un clic propose de redémarrer tout de suite. **App → Rechercher des mises à jour** fait
de même à la demande. Une ancienne version continue d'envoyer pendant 72 heures après la sortie de sa successeure,
puis doit être mise à jour.

La vérification lit une seule URL et n'envoie rien d'autre que la requête :

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Désactivable sous **Paramètres → Mises à jour** ; l'élément de menu fonctionne quand même.

## Langues

Anglais, allemand, français, espagnol, russe, chinois (simplifié et traditionnel), portugais (Brésil), japonais et
coréen — à régler sous **Paramètres → Interface → Langue**. Ce README existe aussi en
[Deutsch](README.de.md), [Español](README.es.md), [Français](README.fr.md), [Русский](README.ru.md),
[中文](README.zh.md), [繁體中文](README.zh-TW.md), [Português (Brasil)](README.pt.md),
[日本語](README.ja.md) et [한국어](README.ko.md).

## Compiler depuis les sources

```
cd Client
dotnet build
dotnet run -- selftest                              # self-checks (protocol, capture, decoding, ...)
dotnet run -- aion2-record <out.jsonl>              # record the game's traffic ("stop" ends it)
dotnet run -- aion2-replay <file.jsonl>             # replay a recording through the real decoder
dotnet run -- aion2-upload-dryrun <file.jsonl>      # build the uploads of a recording, send nothing
```

Windows uniquement (WPF). `Tools/aion2-dat` lit les tables de texte du jeu (noms en huit langues) ; voir son README.

## Remarque sur les règles des serveurs

Le compteur observe uniquement, passivement, le trafic réseau du jeu. Les éditeurs fixent néanmoins leurs propres
règles sur les outils tiers — consultez les conditions du jeu avant de l'utiliser.

## Avertissement et licence

Aion 2 ainsi que tous les noms, textes et illustrations associés sont la propriété de NCSOFT. Ce projet n'est ni affilié à NCSOFT, ni approuvé par NCSOFT, ni lié à NCSOFT ; le contenu du jeu reste la propriété de son détenteur. Le code source est publié sous [licence MIT](LICENSE) — la licence ne couvre que le code, pas le contenu du jeu.
