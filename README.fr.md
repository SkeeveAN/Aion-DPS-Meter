🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 **Français** · 🇷🇺 [Русский](README.ru.md) · 🇵🇱 [Polski](README.pl.md) · 🇹🇷 [Türkçe](README.tr.md) · 🇨🇳 [中文](README.zh.md)

# Aion DPS Meter

Un compteur de DPS et de butin pour **AION 4.6 (OriginAion)** qui fonctionne uniquement à partir
du fichier `Chat.log` du jeu.

Il lit un fichier texte que le client écrit de lui-même. Il ne capture aucun trafic réseau et ne
lit ni n'écrit dans le processus du jeu. Rien de votre partie ne quitte votre machine : ni dégâts,
ni butin, ni noms. La seule chose qu'il envoie est une vérification de mise à jour, qui demande à
GitHub s'il existe une version plus récente et peut être désactivée ; voir [Mises à jour](#mises-à-jour).

## Installation

1. Téléchargez `AionDpsMeter-win-Setup.exe` depuis la [dernière version](../../releases/latest) et
   lancez-le. Il n'y a rien à valider : l'installation se fait dans votre profil utilisateur et le
   compteur démarre. Aucun droit administrateur, aucun .NET requis.
2. Ouvrez **Settings → App Settings** et choisissez votre **dossier d'installation d'Aion** — le
   dossier racine, celui qui contient `bin64\game.dll`. La fenêtre indique immédiatement si elle a
   trouvé une installation valide et si un `Chat.log` s'y trouve déjà.

> **Vous venez de la 0.5.2 ou d'une version antérieure ?** Désinstallez d'abord l'ancienne
> (Paramètres Windows → Applications → *Aion DPS Meter*), puis lancez le nouvel installeur. Ces
> versions s'installaient dans `Program Files`, ce qui les empêchait de se mettre à jour
> elles-mêmes. C'est une étape unique ; ensuite tout se fait tout seul.

### Prérequis : le journal de chat du client doit être activé

Aion n'écrit `Chat.log` que si l'option interne `g_chatlog` est activée. Cet interrupteur se
trouve dans le client du jeu, pas dans cet outil — activez-le comme vous le feriez normalement
(par exemple avec [ShugoConsole](https://github.com/grenadium/ShugoConsole)). **Aion DPS Meter ne
touche jamais au processus du jeu pour cela** ; si le fichier n'est pas écrit, le compteur n'a
rien à lire.

## Utilisation

L'enregistrement démarre dès que le compteur tourne et qu'un dossier Aion valide est renseigné.

**Votre historique de chat n'est jamais lu.** Au démarrage, le compteur se place à la fin
*actuelle* du `Chat.log` et ne traite que les lignes écrites à partir de cet instant — comme un
magnétophone qu'on vient d'allumer, pas comme un scanner d'archives. **La pause écarte** au lieu
de différer : les lignes écrites pendant la pause sont définitivement ignorées, une reprise ne
rejoue donc jamais un combat que vous avez volontairement laissé de côté. Vos conversations
privées passées, le chat de légion et les chuchotements ne sont jamais consultés.

### Vues

- **Dmg** — dégâts par joueur, avec total et DPS, icônes de classe et tri par colonne. Le filtre
  **Mob/Boss** fait passer la colonne du DPS global au véritable **iDPS** par cible (dégâts sur
  une cible divisés par la durée d'engagement commune du groupe avec elle).
- **Loot** — ce qui est tombé et pour qui : personne, objet, quantité et rareté. Les reliques
  comptent en plus dans les points d'Abysse de chaque personne.

### Hide UI (superposition)

Transforme la fenêtre en petites étiquettes traversables au clic, que vous pouvez laisser sur le
jeu — une par joueur, avec nom, dégâts et DPS. Bascule avec **Ctrl+Alt+H**, depuis n'importe où,
pour que ce ne soit jamais un aller sans retour.

### Copy

**Copy** place dans le presse-papiers un classement d'une seule ligne, prêt pour le chat
(`Nom 1.234.567 (890), …`). Dans la vue Loot, cela produit un résumé de butin pour le chat d'Aion,
et **Copy All** donne un tableau Markdown pour Discord.

### Commandes en jeu

À taper comme des lignes de chat normales pour piloter le compteur sans quitter le jeu :

| Commande | Effet |
|---|---|
| `.ui` | basculer la superposition Hide UI |
| `.pause` / `.resume` | arrêter / reprendre l'enregistrement |
| `.dmg` | copier le classement des dégâts dans le presse-papiers |
| `.cleardmg` | effacer la session en cours |
| `.loot` | copier le résumé du butin dans le presse-papiers |

Seuls les personnages enregistrés dans les paramètres peuvent les déclencher : un `.cleardmg`
tapé par un inconnu dans un canal que vous ne lisez même pas ne peut donc pas effacer votre
session.

## Mises à jour

Le compteur se met à jour tout seul. Il interroge GitHub au démarrage puis toutes les cinq
minutes, télécharge la nouvelle version en arrière-plan et la met en place au démarrage suivant.
Aucun installeur à lancer, aucune invite UAC, rien à cliquer. C'est possible parce qu'il vit dans
votre profil utilisateur et non dans `Program Files` : il a le droit de remplacer ses propres
fichiers.

Quand une mise à jour est prête, une ligne verte apparaît dans la barre d'état en bas ; un clic
propose de redémarrer immédiatement. Refuser ne coûte rien : la version est déjà téléchargée et
s'appliquera au prochain démarrage normal. Pas de fenêtre surgissante, volontairement : le
programme est posé sur un jeu en cours, et un dialogue qui vole le focus en plein boss est pire
qu'une mise à jour tardive.

**App → Check for updates** fait la même chose à la demande et vous dit aussi quand vous êtes déjà
à jour.

La vérification lit une seule URL et n'envoie rien d'autre que la requête elle-même :

```
https://api.github.com/repos/SkeeveAN/Aion-DPS-Meter/releases
```

Désactivable dans **Settings → App Settings → Updates**. L'entrée de menu continue de fonctionner :
celle-là, c'est vous qui demandez, pas le programme qui décide.

## Quelle est sa précision ?

Validé contre trois fichiers `Chat.log` du *même* run de la Base d'approvisionnement de Sauro,
enregistrés sur trois PC différents (dont un client allemand), avec les PV réels des boss comme
référence. Les dégâts infligés à chaque boss tombent à **0,02 % – 2,8 %** de ses PV réels :

| Boss | PV réels | Mesuré | Écart |
|---|---|---|---|
| Guard Captain Ahuradim | 1.736.993 | 1.737.299 | +0,02 % |
| Derakanak the Reaver | 1.343.657 | 1.347.258 | +0,27 % |
| Archmagus Sayahum | 1.377.644 | 1.370.734 | −0,50 % |
| Commander Ranodim | 489.332 | 503.244 | +2,84 % |

Le reste est le surplus du coup fatal, qu'aucun compteur basé sur les journaux ne peut voir. Le
total d'un joueur est ressorti identique à l'unité près sur les trois machines.

Deux écarts connus et sans gravité : les dégâts absorbés par le bouclier d'un boss sont tout de
même journalisés (il paraît donc dépasser ses PV), et plusieurs monstres portant le même nom sont
additionnés.

## Compiler depuis les sources

```
dotnet build
dotnet run -- selftest                       # auto-tests du parseur et des calculs de DPS
dotnet run -- chatlog <chemin-vers-Chat.log> # analyser un fichier et afficher le résumé
```

Windows uniquement (WPF). Les auto-tests rejouent des lignes textuelles issues de vrais journaux
dans toutes les langues prises en charge : c'est le moyen le plus rapide de voir si une
modification du parseur a cassé quelque chose.

## À propos des règles du serveur

Cet outil ne lit qu'un fichier journal que le jeu produit lui-même. Cela dit, les serveurs privés
fixent leurs propres règles sur les outils tiers et les addons — un coup d'œil à celles
d'OriginAion s'impose avant utilisation.
