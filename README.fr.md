🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 **Français** · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Site web avec classements de boss communautaires et profils de personnages : **https://aiondps.com**

Un compteur de dégâts/soins pour **Aion 2**. Il lit le trafic réseau du jeu sur votre machine grâce au pilote
[Npcap](https://npcap.com) — passivement : il n'envoie jamais de paquet et ne touche ni le processus du jeu ni
sa mémoire. Rien de votre partie ne quitte votre machine, sauf si vous l'envoyez (voir [Envois](#envois)) ;
s'y ajoute la vérification des mises à jour, qui demande à GitHub si une version plus récente existe et peut
être désactivée ; voir [Mises à jour](#mises-à-jour).

> Aion classique (basé sur Chat.log) ne fait plus partie du compteur. La dernière version qui le prend en
> charge reste sur la branche [`aion1-included`](../../tree/aion1-included).

## Installation

1. Installez le pilote [Npcap](https://npcap.com) (le compteur en a besoin pour voir le trafic du jeu ; il ne
   fait pas partie de l'installateur).
2. Téléchargez `AionDpsMeter-win-Setup.exe` depuis la [dernière version](../../releases/latest) et lancez-le.
   Il s'installe dans votre profil utilisateur et démarre le compteur — pas de droits administrateur, pas de .NET.
3. Lancez le compteur, puis connectez-vous avec votre personnage. Le compteur lit votre personnage, équipement,
   compétences et plateaux Daevanion directement dans le jeu ; le serveur est détecté automatiquement.

## Utilisation

L'enregistrement démarre dès que le compteur tourne. **La pause jette** au lieu de différer : les événements
pendant une pause sont perdus, la reprise ne rejoue jamais un combat que vous avez laissé passer.

### Vues

- **Dmg** — dégâts par joueur, avec total et DPS, icônes de classe et liste triable. Le filtre **Mob/Boss**
  bascule la colonne entre DPS global et **iDPS** par cible. Les boss sont reconnus à partir des données du jeu
  et affichés par leur nom. **Double-clic** sur un joueur pour le détail des compétences.
- **Personnage** (icône de personne) — ouvre une fenêtre avec votre propre personnage : profil, équipement avec
  niveaux d'objet et enchantement, compétences avec niveaux et plateaux Daevanion. Elle garde votre dernière
  connexion et n'est donc jamais vide.

### Hide UI (overlay)

Transforme la fenêtre en petites pastilles traversables posées sur le jeu — une par joueur avec nom, dégâts et
DPS. Bascule avec **Ctrl+Alt+H**, de n'importe où.

### Copier

**Copy** place un classement d'une ligne, prêt pour le chat, dans le presse-papiers (`Nom 1.234.567 (890), …`) ;
**Copy All** donne un tableau Markdown pour Discord.

### Raccourcis clavier

Valables partout, même quand le jeu a le focus (à modifier dans **Paramètres → Interface → Raccourcis clavier**) : **Ctrl+Alt+H** overlay, **Ctrl+Alt+P** pause / reprise, **Ctrl+Alt+D** copier le classement, **Ctrl+Alt+Maj+X** vider la session. Ils remplacent les commandes de chat, qui ne peuvent pas fonctionner car le chat d'Aion 2 n'est pas lisible dans le trafic réseau.

## Envois

- **Les combats de boss** sont envoyés quand vous cliquez sur envoyer (bouton ou menu Session) : boss, joueurs
  présents, dégâts, soins, dégâts subis et compétences. Seuls les boss annoncés par le jeu et connus du catalogue
  sont acceptés.
- **Votre propre profil de personnage** (nom, classe, niveau, équipement, compétences, Daevanion, légion, serveur)
  peut être envoyé automatiquement quelques secondes après la connexion, pour que l'on vous trouve sur le site. C'est
  **désactivé par défaut** ; à activer dans les **Paramètres**.
- Sans envoi, rien ne quitte votre machine.

## Mises à jour

Le compteur se met à jour tout seul. Il demande à GitHub une version plus récente au démarrage et toutes les cinq
minutes, la télécharge en arrière-plan et l'installe au prochain démarrage — ni installateur ni UAC. Quand une mise à
jour est prête, une ligne verte apparaît ; un clic propose de redémarrer tout de suite. **App → Check for updates**
fait de même à la demande.

La vérification lit une seule URL et n'envoie rien d'autre que la requête :

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Désactivable sous **Paramètres → Mises à jour** ; l'élément de menu fonctionne quand même.

## Compiler depuis les sources

```
cd Client
dotnet build
dotnet run -- selftest                              # auto-tests (protocole, capture, décodage, ...)
dotnet run -- aion2-record <out.jsonl>              # enregistrer le trafic du jeu (« stop » termine)
dotnet run -- aion2-replay <fichier.jsonl>          # rejouer un enregistrement dans le vrai décodeur
dotnet run -- aion2-upload-dryrun <fichier.jsonl>   # construire les envois d'un enregistrement, rien envoyer
```

Windows uniquement (WPF). `Tools/aion2-dat` lit les tables de texte du jeu (noms en huit langues) ; voir son README.

## Remarque sur les règles des serveurs

Le compteur observe uniquement, passivement, le trafic réseau du jeu. Les éditeurs fixent néanmoins leurs propres
règles sur les outils tiers — consultez les conditions du jeu avant de l'utiliser.

## Avertissement et licence

Aion 2 ainsi que tous les noms, textes et illustrations associés sont la propriété de NCSOFT. Ce projet n'est ni affilié à NCSOFT, ni approuvé par NCSOFT, ni lié à NCSOFT ; le contenu du jeu reste la propriété de son détenteur. Le code source est publié sous [licence MIT](LICENSE) — la licence ne couvre que le code, pas le contenu du jeu.
