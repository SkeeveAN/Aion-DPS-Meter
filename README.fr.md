🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 **Français** · 🇷🇺 [Русский](README.ru.md) · 🇨🇳 [中文](README.zh.md) · 🇹🇼 [繁體中文](README.zh-TW.md) · 🇧🇷 [Português (Brasil)](README.pt.md) · 🇯🇵 [日本語](README.ja.md) · 🇰🇷 [한국어](README.ko.md)

# Aion DPS Meter

Site web avec classements de boss communautaires, profils de personnages et plus encore : **https://aiondps.com** · [Toutes les fonctionnalités](https://aiondps.com/features)

Un compteur de dégâts/soins et une boîte à outils pour **Aion 2**. Il lit le trafic réseau du jeu sur votre machine
grâce au pilote [Npcap](https://npcap.com) — passivement : il n'envoie jamais de paquet et ne touche ni le
processus du jeu ni sa mémoire. Rien de votre partie ne quitte votre machine, sauf si vous l'envoyez (voir
[Envois](#envois)) ; l'autre chose qu'il envoie est une vérification des mises à jour, qui demande à GitHub si une
version plus récente existe et peut être désactivée ; voir [Mises à jour](#mises-à-jour).

<p align="center">
  <img src="Web-Frontend/images/features/meter@2x.webp" width="520" alt="La fenêtre du compteur : chaque joueur avec icône de classe, DPS, dégâts totaux et barre de part">
</p>

> Les noms, joueurs et chiffres des images ci-dessous sont des données de démonstration.

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

### Détail des compétences pour chaque joueur

**Double-clic** sur un joueur : chaque compétence avec ses coups, son taux de critique, son total, son min, son max
et sa moyenne, répartis par cible, plus une courbe de DPS sur tout le combat. Les taux de critique sont lus
directement dans les données de coups du serveur ; ils sont donc exacts.

<p align="center">
  <img src="Web-Frontend/images/features/details@2x.webp" width="640" alt="Détail des compétences d'un joueur avec une courbe de dégâts dans le temps">
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

### Overlay réseau

Ping, gigue, trafic, perte de paquets et temps passé en ligne, mesurés à partir du trafic que votre PC reçoit
déjà. Les valeurs passent du vert au rouge quand la connexion se dégrade. Choisissez les lignes souhaitées,
verrouillez-le en place, activez-le ou désactivez-le avec un raccourci.

<p align="center">
  <img src="Web-Frontend/images/features/network-overlay@2x.webp" width="340" alt="L'overlay réseau : ping, gigue, trafic et perte de paquets">
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

### Historique des combats

Les combats sont enregistrés localement pour que vous puissiez revoir un boss précédent (désactivable sous
**Paramètres → Interface**). Le compteur se réinitialise après un délai configurable hors combat.

## Utilisation

L'enregistrement démarre dès que le compteur tourne. **La pause jette** au lieu de différer : les événements
pendant une pause sont perdus, la reprise ne rejoue donc jamais un combat que vous avez laissé passer.

- **Copy** place un classement d'une ligne, prêt pour le chat, dans le presse-papiers (`Nom 1.234.567 (890), …`) ;
  **Copy All** donne un tableau Markdown pour Discord.
- L'apparence se règle sous **Paramètres → Interface** : thème (sombre/clair), taille de police, langue, aspect et
  opacité de l'overlay, démarrage avec Windows.

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
