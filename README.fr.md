🇬🇧 [English](README.md) · 🇩🇪 [Deutsch](README.de.md) · 🇪🇸 [Español](README.es.md) · 🇫🇷 **Français**

# Aion DPS Meter

Un compteur de DPS et de butin pour **AION 4.6 (OriginAion)** qui fonctionne uniquement à partir
du fichier `Chat.log` du jeu.

Il lit un fichier texte que le client écrit de lui-même. Il ne capture aucun trafic réseau et ne
lit ni n'écrit dans le processus du jeu. Rien n'est envoyé nulle part — tout reste sur votre
machine.

## Installation

1. Téléchargez `AionDpsMeter.msi` depuis la [dernière version](../../releases/latest) et
   lancez-le. Tout est inclus, vous n'avez pas besoin d'installer .NET.
2. Lancez **Aion DPS Meter** depuis le menu Démarrer (l'installeur propose aussi un raccourci sur
   le bureau).
3. Ouvrez **Settings → App Settings** et choisissez votre **dossier d'installation d'Aion** — le
   dossier racine, celui qui contient `bin64\game.dll`. La fenêtre indique immédiatement si elle a
   trouvé une installation valide et si un `Chat.log` s'y trouve déjà.

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

## Langues du client

Les lignes de chat sont reconnues en **anglais, allemand, français, espagnol et russe**. Deux
joueurs d'un même groupe peuvent utiliser des clients en langues différentes et être tous les deux
comptés correctement — le compteur compare chaque ligne aux tournures de toutes les langues, pas
seulement à celles de votre propre client.

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
