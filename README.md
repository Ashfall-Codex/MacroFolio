<p align="center">
  <img src="https://repo.ashfall-codex.dev/img/macrofolio.png" alt="MacroFolio" width="128" />
</p>

<h1 align="center">MacroFolio</h1>

<p align="center">
  <b>Gestionnaire de macros pour FFXIV</b> : remplace la fenêtre <code>/macro</code> du jeu par un éditeur plus complet, avec grille visuelle, création assistée, sauvegarde par personnage, planches de grille, brouillons sans limite de lignes, textes RP et import / export.
</p>

<p align="center">
<a href="https://argus.arediss.fr"><img src="https://argus.arediss.fr/api/public/badges/b051254b-c68f-48e4-862e-c51db1c10aa3/badge.svg" alt="Contrôle qualité" /></a>
<a href="https://argus.arediss.fr"><img src="https://argus.arediss.fr/api/public/badges/b051254b-c68f-48e4-862e-c51db1c10aa3/badge.svg?metric=reliability" alt="Fiabilité" /></a>
<a href="https://argus.arediss.fr"><img src="https://argus.arediss.fr/api/public/badges/b051254b-c68f-48e4-862e-c51db1c10aa3/badge.svg?metric=security" alt="Sécurité" /></a>
<a href="https://argus.arediss.fr"><img src="https://argus.arediss.fr/api/public/badges/b051254b-c68f-48e4-862e-c51db1c10aa3/badge.svg?metric=maintainability" alt="Maintenabilité" /></a>
<a href="https://argus.arediss.fr"><img src="https://argus.arediss.fr/api/public/badges/b051254b-c68f-48e4-862e-c51db1c10aa3/badge.svg?metric=vulnerabilities" alt="Vulnérabilités" /></a>
</p>

<p align="center">
<a href="https://github.com/Ashfall-Codex/MacroFolio/actions/workflows/build.yml"><img src="https://github.com/Ashfall-Codex/MacroFolio/actions/workflows/build.yml/badge.svg" alt="Compilation du plugin" /></a>
<a href="https://github.com/Ashfall-Codex/MacroFolio/blob/main/LICENSE"><img src="https://img.shields.io/badge/Licence-AGPL--v3-blue.svg" alt="Licence : AGPL-v3" /></a>
<a href="https://github.com/Ashfall-Codex/MacroFolio/releases"><img src="https://img.shields.io/github/v/release/Ashfall-Codex/MacroFolio?include_prereleases" alt="Dernière version" /></a>
</p>

---

## Sommaire

- [Fonctionnalités](#fonctionnalités)
  - [Grille de macros](#grille-de-macros)
  - [Création et édition](#création-et-édition)
  - [Brouillons (WIP)](#brouillons-wip)
  - [Planches](#planches)
  - [Textes RP](#textes-rp)
  - [Import / Export](#import--export)
  - [Synchronisation avec le jeu](#synchronisation-avec-le-jeu)
  - [Pas d'automatisation de combat](#pas-dautomatisation-de-combat)
  - [Apparence](#apparence)
  - [Localisation](#localisation)
- [Installation](#installation)
- [Architecture](#architecture)
  - [Plugin (C#)](#plugin-c)
- [Build](#build)
- [Commandes](#commandes)
- [Licence](#licence)

---

## Fonctionnalités

### Grille de macros

- **Deux sets** : personnage actuel (100 slots) et macros communes (100 slots), choisis d'un sélecteur segmenté
- **Bordure verte** : macro active en jeu ; **bordure rouge** : désactivée, gardée en cache ; **bordure blanche** : sélectionnée
- **Désactivation non destructive** : une macro retirée du jeu reste en cache, prête à être réactivée
- **Glisser-déposer** : activer sur un slot libre, échanger deux slots actifs, désactiver en déposant sur le cache ; un slot occupé demande confirmation
- **Tout mettre en cache** en un clic
- **Édition rapide** de l'icône et du nom de la macro sélectionnée, en bas de la fenêtre
- **Clic droit** : modifier, exécuter, activer / désactiver, dupliquer, dupliquer ou déplacer vers l'autre set, exporter en `.txt`, supprimer définitivement (avec confirmation)
- Fenêtre redimensionnable, grille recalculée selon la place

### Création et édition

- **Assistant en 3 étapes** : icône, nom, actions
- **Sélecteur d'icônes** complet, indexé depuis les données du jeu, avec recherche par nom ou par numéro
- **Mode Classique** : zone de texte brute, comme dans le jeu
- **Mode Simplifié** : actions typées avec leur widget (emote libre, émote prédéfinie, action de classe, `/say` `/yell` `/shout` `/party`, compte à rebours, ciblage, son `<se.N>`, timer, `/nextmacro`, ligne brute) et une note libre par ligne
- **Émotes** filtrées par celles que le personnage a débloquées, **actions** filtrées par le job actuel et sa classe parente
- **Timeline** : horodatage cumulé de chaque ligne, éditable dans les deux sens (modifier un `<wait.X>` ou taper l'instant visé, ex. `1:39`, pour caler une action sur la musique)
- Aller-retour Classique ↔ Simplifié sans perte, la ligne brute servant de secours

### Brouillons (WIP)

- **Sans limite de lignes**, pensés pour les chorégraphies
- **Enregistrement des émotes** faites en jeu, délai entre chaque converti en `<wait.X>`
- **Lancer la séquence** : joue le brouillon en jeu à partir de la ligne choisie, par blocs de 15 lignes sur un slot libre, en respectant les waits
- **Publication** découpée automatiquement en macros chaînées (14 lignes utiles + `/nextmacro`) sur des slots consécutifs, avec détection des slots occupés
- Réordonnables, partagés entre tous les personnages

### Planches

- **Blueprint de grille** : quelle macro à quel slot, pour un set donné
- **Capture** de la grille actuelle ou **création vide**
- **Éditeur** 10×10 : choix de la macro par recherche, échange de cases par glisser-déposer
- **Application non destructive** : les macros hors planche sont désactivées, pas supprimées
- Partagées entre tous les personnages

### Textes RP

- **Bibliothèque de textes** partagée entre tous les personnages, réordonnable par glisser-déposer
- **Copie** dans le presse-papiers en un clic, prête à coller dans le chat
- **Envoi direct** dans le chat en option, découpé en messages de 500 octets, un toutes les 3 secondes. L'envoi automatique de messages est techniquement contraire aux conditions d'utilisation du jeu : à activer en connaissance de cause
- Compteur de caractères et de messages, aperçu, enregistrement automatique

### Import / Export

- **Import** de plusieurs `.txt` à la fois, chaque fichier passant par une fenêtre dédiée (set, icône, nom)
- **Export JSON** (plusieurs macros, format `schemaVersion: 1`) ou **TXT** (un fichier par macro, lignes brutes)

### Synchronisation avec le jeu

- Toutes les écritures passent par `RaptureMacroModule`, jamais directement par `char.dat`
- Vérification au login, à la fermeture de la fenêtre `/macro` native et au rechargement du plugin
- **Conflits** : si une macro a été modifiée en jeu, les deux versions s'affichent côte à côte, lignes différentes surlignées ; garder celle du jeu, restaurer celle de MacroFolio ou tout rétablir. Rien n'est écrasé sans accord

### Pas d'automatisation de combat

- Les deux fonctions que le plugin exécute lui-même, **Lancer la séquence** et **l'envoi direct**, refusent toute commande d'action (`/action`, `/ac`, `/pvpaction`, `/blueaction`, `/generalaction`, `/petaction`, `/buddyaction` et leurs alias, dans toutes les langues du client)
- **Indisponibles en combat** et **en instance** (donjons, raids, défis, JcJ, contenus de terrain…)
- **Restent autorisés** : villes et monde ouvert, housing, chambres d'auberge, île sanctuaire, Firmament et Gold Saucer
- Une exécution en cours s'arrête d'elle-même à l'entrée en combat ou en instance
- **Macros créées par MacroFolio** : en instance, celles qui utilisent une action de combat ou enchaînent la macro suivante (`/nextmacro`) ne se lancent pas, depuis la barre comme depuis le clic droit ; un message l'indique dans le chat. MacroFolio marque chaque macro qu'il écrit en jeu (création, modification, import, publication, application de planche)
- Les macros créées dans l'éditeur natif du jeu ne sont jamais concernées, ni une macro MacroFolio dont on garde la version modifiée en jeu
- Le **mode exploration** lève toutes ces restrictions

### Apparence

- **Effet de transparence** réglé d'un seul curseur à 41 crans, au milieu par défaut
- **Tout à gauche** : verre clair, fond très transparent, sans flou
- **Vers la droite** : le verre se teinte et se dépolit, le décor derrière les fenêtres est flouté
- **Tout à droite** : fond entièrement opaque, flou coupé
- Le texte, les boutons et les onglets restent opaques à toutes les positions
- Thème blanc et gris neutre

### Localisation

- **Français**
- **English**
- Langue du client par défaut, changement à chaud depuis l'onglet À propos

## Installation

Ajouter le dépôt personnalisé dans `/xlsettings` → **Experimental** → **Custom Plugin Repositories** :

```
https://repo.ashfall-codex.dev/plugins.json
```

## Architecture

| Composant | Technologie | Description |
|---|---|---|
| `MacroFolio/` (ce dépôt) | C# / .NET 10 / Dalamud API 15 | Plugin FFXIV (tourne dans le jeu) |

### Plugin (C#)

- **Point d'entrée** : `Plugin.cs` — enregistre `/macrofolio` (+ alias), les hooks UI, le tick framework et l'écoute de la fenêtre `/macro` native
- **Macros** : `MacroService` (lecture, écriture et exécution via `RaptureMacroModule` / `RaptureShellModule`), `MacroLibrary` (source de vérité : cache par personnage, réconciliation avec le jeu, conflits, application des planches, publication et lecture des brouillons)
- **Bibliothèques** : `PlancheLibrary`, `BrouillonLibrary` (découpage en macros chaînées) et `TexteLibrary`, un fichier JSON par entrée, partagées entre personnages
- **Chat** : `ChatSenderService` (découpage en messages de 500 octets et envoi via `UIModule.ProcessChatBoxEntry`)
- **Garde-fou** : `AutomationGuard` (commandes d'action lues dans la sheet `TextCommand` pour toutes les langues, combat via `ICondition`, type de zone via `GameMain`, mode exploration via `ContentsFinder`) et `MacroExecutionGuard` (hook sur `RaptureShellModule.ExecuteMacro` : bloque en instance les macros marquées `CreatedByMacroFolio` qui contiennent une action ou un `/nextmacro`, laisse passer en cas de doute ou d'erreur)
- **Émotes** : `EmoteRecordingService` (hook sur `EmoteManager.ExecuteEmote` pour l'enregistrement, appel direct pour la lecture)
- **Icônes** : `IconPickerIndex` (index des icônes du jeu construit en tâche de fond)
- **UI** : ImGui avec thème blanc ; fenêtre principale à barre latérale (Mes macros, WIP, Mes planches, Mes textes, À propos), assistant de création, fenêtre d'import, sélecteur d'icônes. Composants partagés dans `UI/Components/` (boutons, contrôle segmenté, interrupteur, champ de recherche, écran vide, curseur)
- **Effet de transparence** : `BackdropBlur` floute l'image du jeu fournie par Dalamud (`CreateFromImGuiViewportAsync`, prise avant le rendu ImGui) en dual Kawase sur D3D11 (TerraFX), une seule fois par image quel que soit le nombre de fenêtres, puis chaque fenêtre en affiche la portion qui lui correspond. Une fenêtre posée sur une autre rend opaque la zone recouverte. En cas d'erreur graphique, retour à la simple transparence. `GlassSlider` dessine le curseur
- **Localisation** : `Loc` + `Strings.resx` (anglais, repli) et `Strings.fr.resx`
- **Modèles** : `CachedMacro`, `MacroEntry`, `MacroBundle`, `Planche`, `Brouillon`, `TexteRP`
- **Persistance** : config Dalamud (langue, transparence) ; sous `pluginConfigs/MacroFolio/`, `Cache/<Personnage>/macros.json`, `Planches/`, `Brouillons/` et `Textes/` en JSON, écriture atomique (`.tmp` puis `Move`)

## Build

```bash
dotnet build MacroFolio/MacroFolio.csproj -c Release
```

Nécessite .NET 10.x SDK et Dalamud API 15 (via XIV on Mac ou équivalent). En ligne de commande, la sortie est dans `MacroFolio/bin/Release/` ; depuis l'IDE en plateforme x64, dans `MacroFolio/bin/x64/Release/`. Ajouter ce dossier dans `/xlsettings` → **Experimental** → **Dev Plugin Locations**, puis recharger depuis `/xlplugins` → **Dev Tools**.

## Commandes

| Commande | Description |
|---|---|
| `/macrofolio` | Ouvre / ferme la fenêtre principale |
| `/mfolio` | Alias court de `/macrofolio` |

## Licence

[GNU Affero General Public License v3.0](LICENSE). Tout fork ou réutilisation, y compris derrière un service réseau, doit rester sous la même licence et publier ses modifications.

---

<p align="center">Plugin développé par Keda, membre du collectif <a href="https://ashfall-codex.dev">Ashfall Codex</a></p>
