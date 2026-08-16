<div align="center">

# MacroFolio

[![Build Status](https://github.com/kedaewyn/MacroFolio/actions/workflows/build.yml/badge.svg)](https://github.com/kedaewyn/MacroFolio/actions/workflows/build.yml)
[![License: AGPL-v3](https://img.shields.io/badge/License-AGPL--v3-blue.svg)](https://github.com/kedaewyn/MacroFolio/blob/main/LICENSE)
[![Latest Release](https://img.shields.io/github/v/release/kedaewyn/MacroFolio?include_prereleases)](https://github.com/kedaewyn/MacroFolio/releases)

[![Quality gate](https://argus.arediss.fr/api/projects/kedaewyn-macrofolio/badge.svg)](https://argus.arediss.fr)
[![Fiabilité](https://argus.arediss.fr/api/projects/kedaewyn-macrofolio/badge.svg?metric=reliability)](https://argus.arediss.fr)
[![Sécurité](https://argus.arediss.fr/api/projects/kedaewyn-macrofolio/badge.svg?metric=security)](https://argus.arediss.fr)
[![Maintenabilité](https://argus.arediss.fr/api/projects/kedaewyn-macrofolio/badge.svg?metric=maintainability)](https://argus.arediss.fr)
[![Vulnérabilités](https://argus.arediss.fr/api/projects/kedaewyn-macrofolio/badge.svg?metric=vulnerabilities)](https://argus.arediss.fr)

**Gestionnaire de macros pour Final Fantasy XIV - plugin Dalamud.**

MacroFolio remplace la fenêtre `/macro` native par un éditeur plus complet : grille visuelle,
création assistée, sauvegarde par personnage, planches (blueprints de grilles), brouillons
illimités avec enregistrement live et prévisualisation, import / export JSON et TXT, et plus encore.

</div>

---

## Sommaire

- [Présentation](#présentation)
- [Fonctionnalités](#fonctionnalités)
- [Installation](#installation)
- [Utilisation](#utilisation)
  - [Commandes slash](#commandes-slash)
  - [Fenêtre principale et sidebar](#fenêtre-principale-et-sidebar)
  - [Onglet "Mes macros"](#onglet-mes-macros)
  - [Création / édition - wizard 3 étapes](#création--édition---wizard-3-étapes)
  - [Modes Classique et Simplifié](#modes-classique-et-simplifié)
  - [Timeline et timer bidirectionnel](#timeline-et-timer-bidirectionnel)
  - [Actions du clic droit](#actions-du-clic-droit)
  - [Import / Export](#import--export)
  - [Onglet "Brouillons"](#onglet-brouillons)
  - [Onglet "Mes planches"](#onglet-mes-planches)
  - [Onglet "À propos"](#onglet-à-propos)
- [Architecture technique](#architecture-technique)
  - [Stack](#stack)
  - [Arborescence](#arborescence)
  - [Composants](#composants)
  - [Modèle de données](#modèle-de-données)
  - [Stockage sur disque](#stockage-sur-disque)
  - [Formats d'échange](#formats-déchange)
- [Build et installation locale](#build-et-installation-locale)
- [Licence](#licence)

---

## Présentation

Le jeu stocke les macros dans un fichier binaire `char.dat` peu pratique à manipuler. MacroFolio propose :

- une **interface visuelle** miroir de la fenêtre `/macro` du jeu, mais étendue ;
- une **librairie locale persistante** par personnage qui dépasse la limite de 100 slots du jeu ;
- des **planches** (blueprints) pour capturer une configuration de grille et la réappliquer à volonté ;
- des **brouillons** sans limite de lignes, avec **enregistrement live des émotes** et **prévisualisation**, pensés pour construire des chorégraphies ;
- des **imports / exports** en JSON canonique ou en TXT paste-friendly pour partager une macro.

MacroFolio ne touche jamais directement à `char.dat`. Toutes les écritures passent par `RaptureMacroModule` (FFXIVClientStructs), exactement comme l'éditeur natif.

---

## Fonctionnalités

- Lister, créer, modifier, dupliquer, déplacer, supprimer des macros
- Onglets **Personnage actuel** (100 slots Individual) et **Communes** (100 slots Shared)
- Création / édition assistée en 3 étapes : icône, nom, actions
- Deux modes d'édition d'actions :
  - **Classique** : textarea brut comme dans le jeu
  - **Simplifié** : actions typées avec widgets dédiés (Emote, émote prédéfinie, action de classe, /say, /yell, /shout, /party, compte à rebours, ciblage, son `<se.N>`, Timer, /nextmacro, Brut)
- **Timeline** dans le mode Simplifié : timestamp cumulé par ligne, éditable dans les deux sens (wait → temps, ou temps cible → wait)
- Picker d'icônes complet (sheet Lumina `MacroIcon`, même liste que le picker natif), paginé
- Émotes prédéfinies filtrées par celles que **votre personnage a débloquées** (`UIState.IsEmoteUnlocked`)
- Actions de classe filtrées par le **job actuel + sa classe parente** (Paladin voit aussi les actions Gladiateur)
- Désactivation non-destructive : une macro retirée du jeu reste en cache pour réactivation
- **Planches** : capture / création vide / édition / application non-destructive
- **Brouillons** : éditeur sans limite de lignes, **enregistrement** des émotes faites en jeu (avec timing), **prévisualisation** de la séquence, **publication** auto-découpée en macros chaînées
- Import multi-fichiers `.txt`, export JSON bundle ou TXT
- Confirmations modales avant toute suppression définitive (macro, planche, brouillon)
- Sauvegarde **par personnage** + bibliothèques **cross-character** pour planches et brouillons
- Sync automatique avec l'éditeur natif

---

## Installation

### Pré-requis

- Final Fantasy XIV avec **XIVLauncher** (Dalamud)
- Pas sur le repo officiel : ajout via repo custom ou build local.

### Via repo custom

Ajoute l'URL du `repo.json` hébergé dans `/xlsettings` → onglet **Experimental** → **Custom Plugin Repositories**, puis cherche **MacroFolio** dans la liste des plugins.

### Build local (développeur)

Voir [Build et installation locale](#build-et-installation-locale).

---

## Utilisation

### Commandes slash

| Commande | Effet |
|---|---|
| `/macrofolio` | Ouvre / ferme la fenêtre principale |
| `/mfolio` | Alias court |

### Fenêtre principale et sidebar

La fenêtre est **redimensionnable** ; la grille de macros se reflow selon la largeur.

La sidebar gauche contient quatre boutons :

1. **Mes macros** (icône scroll) - la grille principale
2. **Brouillons** (icône stylo) - éditeur de chorégraphies sans limite
3. **Mes planches** (icône livre) - bibliothèque de blueprints
4. **À propos** (icône info, en bas) - informations plugin

### Onglet "Mes macros"

Deux gros boutons séparent les sets : **nom du personnage** (Individual) et **Communes** (Shared). En haut à droite : **Importer** / **Exporter**.

La grille affiche toutes les macros connues du set :

- **Bordure verte** : active en jeu
- **Bordure rouge** : désactivée (en cache, retirée du jeu)
- **Bordure blanche** : sélectionnée (footer affiche son contenu)
- Case **"+"** : ouvre le wizard de création

Footer : `X/100 activées · +N en cache`.

### Création / édition - wizard 3 étapes

Case **"+"** (création) ou clic droit → **Modifier...** (édition). Fenêtre redimensionnable.

1. **Icône** - aperçu + grille paginée (sheet `MacroIcon`)
2. **Nom** - champ texte (max 64)
3. **Actions** - sélecteur Classique / Simplifié + compteur `X / 15 lignes` + bouton **Créer**

À la création, la macro va dans le premier slot libre du set, ou en cache si plein.

### Modes Classique et Simplifié

**Classique** : textarea multi-ligne brute (une commande par ligne).

**Simplifié** : liste d'actions typées, chaque ligne sérialisée vers la syntaxe FFXIV.

| Type | Sérialisation |
|---|---|
| Emote (personnalisée) | `/em <texte>` |
| Émote prédéfinie | `/salut` (combo filtré par émotes débloquées + recherche + case "animation" → `/salut animation`) |
| Action de classe | `/ac "Lame rapide"` (combo filtré par job + classe parente + recherche) |
| Écrire (parler/crier/hurler/groupe) | `/say` `/yell` `/shout` `/party` |
| Compte à rebours | `/compteàrebours N` |
| Cibler (générique) / Cibler un PJ | `/cibler` / `/ciblerpj` (+ placeholders `<t>`, `<1>`, `<f>`...) |
| Ajouter un son | `/e <message> <se.N>` (N 1-16) |
| Timer | suffixe inline `<wait.N>` (waits décimaux : `<wait.3.5>`) |
| Macro suivante | `/nextmacro` (unique, épinglé en dernier) |
| Brut | la ligne telle quelle (fallback) |

Garde-fous : le Timer s'attache toujours à la ligne précédente (jamais en première position) ; `/nextmacro` reste unique et en dernier. Aller-retour Classique ↔ Simplifié non-destructif (le parser reconstruit les types, mode Brut en secours).

### Timeline et timer bidirectionnel

Dans le mode Simplifié, une colonne **timestamp** à gauche de chaque ligne affiche quand l'action se déclenche, calculé depuis le champ **"Lancement à :"** + le cumul des waits.

Édition dans les deux sens :

- modifier un `<wait.X>` → recalcule les timestamps suivants ;
- taper un **timestamp cible** (ex : `1:39`) dans la colonne → calcule le wait nécessaire de la ligne précédente pour tomber pile à ce moment (utile pour caler une action sur un beat de musique).

### Actions du clic droit

Sur une case macro :

- **Modifier...** - rouvre le wizard pré-rempli
- **Activer** / **Désactiver** selon l'état
- **Dupliquer vers [autre set]** - copie indépendante
- **Déplacer vers [autre set]** - copie + supprime l'original
- **Exporter en .txt**
- **Supprimer définitivement** (confirmation modale)

### Import / Export

**Import** : bouton **Importer** → sélecteur `.txt` multi-fichiers. Chaque fichier passe dans une fenêtre dédiée : set cible, récap des lignes, picker d'icône, nom (pré-rempli), boutons `Annuler tout` / `Ignorer` / `Importer`.

**Export** : bouton **Exporter** → modale avec format **JSON** (multi-macros, conserve tout) ou **TXT** (un fichier par macro, lignes brutes), checkboxes de sélection, puis save dialog.

### Onglet "Brouillons"

Un **brouillon** est un macro de travail **sans limite de lignes**, pensé pour construire des chorégraphies.

**Liste** : carte par brouillon (nom + `X lignes → N macros en jeu` + date) avec **Modifier** / **Publier** / **Supprimer**. Bouton **+ Nouveau brouillon**.

**Éditeur** :

- Nom + icône
- Bouton **● Enregistrer mes émotes** : lance la capture, fais tes gestes en jeu, ils s'ajoutent en bas avec le délai entre chaque converti en `<wait.X>`. Re-clic pour arrêter.
- Bouton **▶ Lancer la séquence** : prévisualise la chorégraphie en jeu (rejoue les émotes en respectant les waits), **■ Stop** pour interrompre.
- Textarea sans limite + compteur `X lignes → N macros une fois publié`

**Publier** : modale avec set cible, slot de départ (0-99, saisie directe), récap du découpage et détection des slots occupés. Le brouillon est découpé en macros de **14 lignes utiles + `/nextmacro`** chaînées, la dernière sans `/nextmacro`. Ex : 130 lignes → 10 slots consécutifs.

### Onglet "Mes planches"

Une **planche** est un blueprint de grille : quelle macro à quel slot (0-99), pour un set donné.

- **+ Planche vide** (ouvre l'éditeur) / **+ Capturer la grille actuelle**
- Carte par planche avec **Modifier** / **Appliquer** / **Supprimer**
- **Éditeur** : grille 10×10 cliquable, clic sur une case → popup de choix de macro (recherche + liste du set), bouton "Vider ce slot"
- **Application non-destructive** : les macros à des slots non couverts par la planche sont **désactivées** (pas supprimées), les slots ciblés reçoivent le contenu du blueprint

### Onglet "À propos"

Logo + nom + version + auteur + description (lus depuis le manifest), lien GitHub, slash commands, et bloc **Ashfall Codex**.

---

## Architecture technique

### Stack

- **Plateforme** : plugin Dalamud (FFXIV)
- **SDK** : `Dalamud.NET.Sdk/15.0.0` (API 15, FFXIV 7.x)
- **Runtime** : .NET 10 (hérité du SDK), x64, `ImplicitUsings` + `Nullable` activés
- **Binding ImGui** : `Dalamud.Bindings.ImGui`
- **Sérialisation** : `System.Text.Json` (camelCase, indented, écriture atomique `.tmp` + `Move`)
- **API FFXIV** :
  - `RaptureMacroModule` - lecture / écriture des slots
  - `UIState.IsEmoteUnlocked` - émotes débloquées
  - `EmoteManager.ExecuteEmote` - hooké pour l'enregistrement, appelé pour la prévisualisation
- **Sheets Lumina** : `MacroIcon`, `Emote` + `TextCommand`, `Action` + `ClassJob`

### Arborescence

```
MacroFolio/
├── MacroFolio.csproj          SDK Dalamud, version, flags
├── MacroFolio.json            Manifest
├── Plugin.cs                  Point d'entrée, services, hooks, Framework.Update
├── Configuration.cs           IPluginConfiguration
├── Constants.cs               Nom, commandes, alias, URL, version
├── Services/
│   ├── MacroService.cs        Wrapper unsafe sur RaptureMacroModule
│   ├── MacroLibrary.cs        Cache par perso + sync jeu (source de vérité)
│   ├── PlancheLibrary.cs      Blueprints cross-character
│   ├── BrouillonLibrary.cs    Brouillons cross-character + découpage publish
│   └── EmoteRecordingService.cs  Hook ExecuteEmote : enregistrement + playback
├── Models/
│   ├── MacroEntry.cs          DTO macro plate (Name, IconId, Lines)
│   ├── CachedMacro.cs         Macro tracked (Id GUID, GameSlot nullable)
│   ├── MacroBundle.cs         Format export JSON
│   ├── Planche.cs             Blueprint (Id, Name, TargetSet, Entries)
│   └── Brouillon.cs           Draft (Id, Name, IconId, Lines illimitées)
└── UI/
    ├── MacroFolioTheme.cs     Thème blanc (PushTheme / PopTheme)
    ├── MacroFolioWindowBase.cs Base abstraite (thème auto en PreDraw/PostDraw)
    ├── MainWindow.cs          Sidebar + onglets (Macros, Brouillons, Planches, About)
    ├── MacroCreateWizard.cs   Wizard création / édition 3 étapes
    └── MacroImportWindow.cs   Import multi-fichiers .txt

.github/workflows/build.yml    CI Ubuntu (dotnet 10) - build + artifact
repo.json                      Exemple de manifest repo Dalamud custom
```

### Composants

**Plugin.cs** : injecte les services Dalamud (`IDalamudPluginInterface`, `ICommandManager`, `IPluginLog`, `ITextureProvider`, `IObjectTable`, `IDataManager`, `IGameInteropProvider`, `IFramework`), crée le `WindowSystem`, instancie les services et fenêtres, enregistre les commandes, hooke `UiBuilder.Draw` + `Framework.Update` (pour le scheduling du playback).

**MacroService** (`unsafe`) : `Read` / `ReadSlot` / `Write` / `Clear` via `RaptureMacroModule` (décodage `Utf8String`, `SetSavePendingFlag`).

**MacroLibrary** : source de vérité, cache par perso (`Cache/<Char>/macros.json`), `SyncWithGame` réconcilie cache et jeu, opérations `Create` / `UpdateMacro` / `Disable` / `Enable` / `Delete` / `ReplaceAll` (apply planche) / `PublishChunks` (publish brouillon).

**PlancheLibrary** / **BrouillonLibrary** : un fichier JSON par entrée, CRUD, cross-character. `BrouillonLibrary.SplitForGame` découpe en chunks de 14 + `/nextmacro`.

**EmoteRecordingService** (`unsafe`) : hook sur `EmoteManager.ExecuteEmote` (adresse résolue par FFXIVClientStructs). En enregistrement, capture l'`emoteId` + le délai → commande via sheet `Emote`. En lecture, rejoue une liste d'émotes via `ExecuteEmote`, scheduling sur `Tick(dt)` appelé depuis `Framework.Update`.

### Modèle de données

```csharp
public sealed class CachedMacro          // source de vérité côté plugin
{
    public string Id { get; set; }        // GUID stable
    public int? GameSlot { get; set; }    // null = désactivée, 0-99 = active
    public string Name { get; set; }
    public uint IconId { get; set; }
    public List<string> Lines { get; set; }
    [JsonIgnore] public bool IsActive => GameSlot.HasValue;
}

public sealed class Planche               // blueprint d'une grille
{
    public string Id; public string Name;
    public MacroSet TargetSet;
    public DateTimeOffset CreatedAt, UpdatedAt;
    public List<PlancheEntry> Entries;     // { Slot, Name, IconId, Lines }
}

public sealed class Brouillon             // draft sans limite
{
    public string Id; public string Name; public uint IconId;
    public List<string> Lines;            // illimité
    public DateTimeOffset CreatedAt, UpdatedAt;
}

public enum MacroSet { Individual, Shared }
```

### Stockage sur disque

Sous `<XIVLauncher>/pluginConfigs/MacroFolio/` :

```
MacroFolio.json              Configuration plugin
Cache/<Personnage>/macros.json   Cache par personnage
Planches/<guid>.json         Une planche par fichier (cross-character)
Brouillons/<guid>.json       Un brouillon par fichier (cross-character)
```

Écritures atomiques (`*.tmp` + `File.Move(overwrite)`), caractères de fichier invalides remplacés par `_`.

### Formats d'échange

**JSON bundle** (export multi-macros) : `MacroBundle` avec `schemaVersion: 1`, `exportedBy`, `exportedAt`, et une liste d'entrées `{ set, slot, entry: { name, iconId, lines } }`.

**TXT** (export single-macro) : contenu brut des lignes (`\n`), sans métadonnée - paste-friendly pour Discord/forums.

---

## Build et installation locale

### Pré-requis

- **.NET 10 SDK**
- **Dalamud dev distribution** :
  - Linux / macOS : `~/.xlcore/dalamud/Hooks/dev`
  - Windows : `%AppData%\XIVLauncher\addon\Hooks\dev`

### Build

```bash
dotnet build MacroFolio/MacroFolio.csproj -c Release
```

Artefact dans `MacroFolio/bin/Release/` (zippé par la CI GitHub Actions).

### Installation en dev plugin

1. Build
2. `/xlsettings` → **Experimental** → **Dev Plugin Locations** → ajouter le dossier de sortie
3. `/xlplugins` → **Dev Tools** → reload MacroFolio
4. `/macrofolio`

Aucune dépendance NuGet manuelle : tout vient de `Dalamud.NET.Sdk`.

---

## Licence

[GNU Affero General Public License v3.0](LICENSE) - AGPL-3.0. Tout fork ou réutilisation (y compris derrière un service réseau) doit rester sous la même licence et publier ses modifications.

---

**Plugin développé par Keda** | partie du collectif [Ashfall Codex](https://ashfall-codex.dev)
