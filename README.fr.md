🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 **Français** · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS Meter

**Le compteur de DPS gratuit pour AION 2** — dégâts et soins en direct pour tout votre groupe, une surcouche qui laisse passer les clics, des classements de boss, une carte des familiers et de la récolte, un calendrier d'événements et des minuteurs de boss mondiaux. Pour Windows, open source, et il ne lit jamais la mémoire du jeu.

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
L'overlay ne s'affiche que lorsque le jeu est au premier plan. Activez-le de n'importe où avec un raccourci.

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

Un combat conserve ces données dans l'historique local, ce qui permet de revoir un ancien boss ; les combats enregistrés avec une version plus ancienne n'affichent pas de rotation.

<p align="center">
  <img src="Web-Frontend/images/features/rotation@2x.webp" width="640" alt="L'onglet Rotation de la fenêtre du joueur : compétences sur une frise chronologique avec le DPS derrière, l'ouverture et la rotation en bref">
</p>

### Tout votre personnage d'un coup d'œil

La fenêtre **Personnage** (icône de personne) montre ce que le jeu envoie à votre sujet : équipement avec niveau
d'objet et enchantement (y compris le niveau d'Amplification au-delà de +15), arcanes, connaissance des espèces,
toutes les compétences avec leurs niveaux et les plateaux Daevanion complets. Elle garde votre dernière connexion
et n'est donc jamais vide. Survolez une pièce pour ses détails (pierres, statistiques obtenues, pierre divine).

<p align="center">
  <img src="Web-Frontend/images/features/character-gear@2x.webp" width="640" alt="La fenêtre Personnage avec équipement, niveaux d'objet et enchantement">
</p>

### Carte interactive avec pets et récolte

Une carte en overlay montre votre position, les points d'apparition des pets que vous traquez et les points de
récolte que vous avez activés — herbes, minerais, bois, gemmes et plus — sans alt-tab vers un site web. Seul ce
que votre propre faction peut utiliser est listé, et la dernière position connue reste sur la carte tant que le
jeu est fermé. Les réglages des pets listent chaque région de Verteron, d'Altgard et de l'Abysse avec ses pets et
votre propre niveau de pet, plus un onglet **Rares** pour les pets ayant peu de points d'apparition. Des flèches
au bord indiquent le pet ou la ressource la plus proche.

<p align="center">
  <img src="Web-Frontend/images/features/map@2x.webp" width="520" alt="La carte interactive avec points d'apparition de pets et liste des pets">
  <img src="Web-Frontend/images/features/gathering@2x.webp" width="230" alt="Réglages de récolte : activez ce qui apparaît sur la carte">
</p>

### Horaires des événements avec rappels

Un overlay liste les prochains événements — Champ de bataille, sièges de l'Abysse (Siège d'artefact), Fête des
Shugo, Raid aérien de Beritra, Faille spatio-temporelle et plus. Choisissez un son et un volume par événement et
soyez prévenu quelques minutes avant son début. Un seul commutateur active ou désactive tous les rappels.

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

### Overlay de combat

Un cadre transparent avec jusqu'à trois éléments, que tu places et dimensionnes chacun séparément :

- **Barre d'incantation du boss :** dès qu'un boss commence une compétence, tu vois son nom, une barre qui se vide avec le temps restant et qui elle vise (toi en rouge). Une compétence qui a touché plusieurs joueurs est marquée **ZONE** ; **MORTEL** signifie que le coup le plus fort que tu en as reçu jusqu'ici est au moins égal à ta santé actuelle. Le compteur apprend chaque compétence (de zone ou non, quand ses dégâts arrivent) et s'en souvient d'une session à l'autre.
- **Cible de la cible :** si la cible sélectionnée avec Tab vise elle-même quelqu'un, tu vois qui : un boss avec son image et son nom, un joueur avec l'emblème de classe et son nom.
- **Cible du soigneur** (facultatif) : la barre de vie de la cible sélectionnée et les debuffs (ou buffs) qu'elle porte.

Déverrouille l'overlay dans **Paramètres → Overlay de combat** pour l'organiser : fais glisser le corps d'un élément pour le déplacer, tire sur ses bords pour le redimensionner, tire sur son coin pour le mettre à l'échelle ; la poignée en bas à droite de tout le cadre met tous les éléments à l'échelle ensemble. Verrouillé, il laisse passer les clics et ne s'affiche que lorsque le jeu est au premier plan. **Ctrl+Alt+C** l'active et le désactive, tout comme le raccourci *Tous les overlays activés / désactivés*.

<p align="center">
  <img src="Web-Frontend/images/features/combat-overlay@2x.webp" width="480" alt="L'overlay de combat : deux incantations de boss avec barre de temps, la cible de la cible et la cible du soigneur avec vie et debuffs">
</p>

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

Affichez le meter en direct par-dessus votre jeu sans filmer sa fenêtre : activez **Paramètres → Interface → Overlay de stream pour OBS activé**, puis utilisez **Copier le lien pour OBS** (ou, dans l'icône de la zone de notification, *Copier le lien de l'overlay de stream (OBS)*), ajoutez une source **Navigateur** dans OBS ou Streamlabs, collez le lien et réglez la taille de la source. La page suit le meter en direct : la vue actuelle (dégâts, soins, dégâts subis, le tout en une), les joueurs avec icône de classe et barres, et la santé du boss. Ajoutez `?scale=1.5` ou `?rows=5` au lien pour l'agrandir ou afficher moins de lignes.

Il ne fonctionne que sur votre PC : le serveur écoute sur `127.0.0.1` (rien en dehors de votre ordinateur ne peut l'atteindre), le lien contient une clé aléatoire (avec **Nouvelle clé**, l'ancien lien cesse de fonctionner), seul `GET` est servi, la page ne charge rien depuis ailleurs et rien n'est envoyé à personne. Les noms des joueurs sont **désactivés par défaut** - chacun est affiché par sa classe, comme dans les envois - et peuvent être activés dans la même carte de paramètres.

### Sessions

Chaque carte et chaque instance est enregistrée comme session à part, pour revoir un passage précédent. Un contrôle de plausibilité compare les dégâts aux points de vie perdus par le boss.

<p align="center">
  <img src="Web-Frontend/images/features/sessions@2x.webp" width="590" alt="Gestionnaire de sessions avec chronologie et contrôle de plausibilité">
</p>

L'historique retient quels combats de boss ont été envoyés en ligne : sa liste a une colonne **Envoyé**, peut n'afficher que les combats de boss non envoyés, et **Paramètres → Interface** indique combien il y en a. Les combats enregistrés par une ancienne version affichent un « ? » à la place (l'historique ne le suivait pas à l'époque).

## Utilisation

L'enregistrement démarre dès que le compteur tourne. **La pause jette** au lieu de différer : les événements
pendant une pause sont perdus, la reprise ne rejoue donc jamais un combat que vous avez laissé passer.

- **Copy** place un classement d'une ligne, prêt pour le chat, dans le presse-papiers (`Nom 1.234.567 (890), …`) ;
  **Copy All** donne un tableau Markdown pour Discord.
- L'apparence se règle sous **Paramètres → Interface** : thème (sombre/clair), taille de police, langue, aspect et
  opacité de l'overlay, démarrage avec Windows.
- Vos réglages survivent à un plantage : le fichier de réglages est écrit en une seule fois, et une copie du dernier démarrage est conservée à côté. Si le fichier est un jour endommagé, cette copie est restaurée (le fichier endommagé est conservé à côté). **Paramètres → Interface → Réinitialiser tous les réglages** demande confirmation deux fois et peut être annulé pendant 30 secondes ; vos choix d'envoi et de confidentialité, la vérification des mises à jour et votre personnage restent tels quels.

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
