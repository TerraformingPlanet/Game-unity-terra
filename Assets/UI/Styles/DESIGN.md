---
name: Terraformation HUD
version: "alpha"
description: "Design system for the Terraformation in-game HUD. Dark space-themed interface. All tokens map 1:1 to variables.uss — update both files in sync. Semi-transparent colors store opaque base hex here; opacity is applied in variables.uss."

colors:
  # Panels — opaque base. Opacity applied in USS: panel-bg 92%, panel-bg-light 88%, topbar-bg 95%
  panel-bg:          "#0c0c12"
  panel-bg-light:    "#1c1c28"
  panel-border:      "#ffffff"
  panel-border-acc:  "#64b4ff"
  separator:         "#ffffff"
  topbar-bg:         "#08080e"
  # Text
  text-primary:      "#dcdcdc"
  text-secondary:    "#8c8c8c"
  text-accent:       "#64b4ff"
  text-warning:      "#ffc83c"
  text-danger:       "#f05a50"
  # Buttons — neutral uses white base; opacity 10%/18% applied in USS
  btn-claim:         "#2e9e47"
  btn-claim-hover:   "#38bc56"
  btn-unclaim:       "#b23828"
  btn-unclaim-hover: "#d24637"
  btn-build:         "#4073b2"
  btn-build-hover:   "#508cd2"
  btn-neutral:       "#ffffff"
  btn-neutral-hover: "#ffffff"
  # Domain
  construction:      "#ff8c00"
  energy:            "#ffd732"
  eco:               "#50c878"
  research:          "#64a0ff"
  diplomacy:         "#c880ff"
  # Terrain tags
  terrain-ocean:     "#1e5ab4"
  terrain-coast:     "#3c8cc8"
  terrain-plains:    "#78b450"
  terrain-arid:      "#d2aa50"
  terrain-frozen:    "#c8dcf0"
  terrain-volcano:   "#dc501e"
  terrain-default:   "#a0a0a0"

typography:
  title:
    fontSize: 14px
    fontWeight: 700
  body:
    fontSize: 12px
    fontWeight: 400
  caption:
    fontSize: 10px
    fontWeight: 400
    letterSpacing: 1.2px
  icon:
    fontSize: 16px
    fontWeight: 400

spacing:
  xs:  4px
  sm:  8px
  md:  12px
  lg:  20px

rounded:
  card:   4px
  panel:  6px
  pill:   20px

components:
  topbar:
    height: 40px
    backgroundColor: "{colors.topbar-bg}"
  panel:
    backgroundColor: "{colors.panel-bg}"
    rounded: "{rounded.panel}"
    padding: "{spacing.md}"
  tile-inspector:
    width: 260px
    backgroundColor: "{colors.panel-bg}"
  btn-neutral:
    backgroundColor: "{colors.btn-neutral}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.card}"
  btn-claim:
    backgroundColor: "{colors.btn-claim}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.card}"
  btn-unclaim:
    backgroundColor: "{colors.btn-unclaim}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.card}"
  btn-build:
    backgroundColor: "{colors.btn-build}"
    textColor: "{colors.text-primary}"
    rounded: "{rounded.card}"
  bottom-action-bar:
    height: 52px
    backgroundColor: "{colors.topbar-bg}"
---

## Overview

Interface sombre spatiale, ton "terminal militaire" — faible contraste de fond pour ne pas distraire
du rendu 3D du monde. Lisibilité assurée uniquement par la hiérarchie typographique et les couleurs
sémantiques (accent bleu, warning jaune, danger rouge).

L'opacité des panneaux (`0.88–0.95`) est intentionnelle : le monde derrière doit rester perceptible.
Ne jamais utiliser un fond opaque plein sur les panneaux flottants.

## Colors

**Panels** : fonds quasi-noirs semi-transparents. `panel-bg` pour les panneaux principaux,
`panel-bg-light` pour les cartes ou sous-sections imbriquées.

**Text** : trois niveaux de hiérarchie.
- `text-primary` (220, 220, 220) — contenu principal, titres, valeurs
- `text-secondary` (140, 140, 140) — labels, métadonnées, captions
- `text-accent` (100, 180, 255) — bleu espace, liens actifs, valeurs positives remarquables

**Semantic states** :
- `text-warning` jaune — attention (ressource faible, délai proche)
- `text-danger` rouge — erreur ou blocage (tuile inaccessible, contrat échoué)

**Buttons** — couleur = intention :
- `btn-claim` vert — action positive, appropriation
- `btn-unclaim` rouge — action destructive, libération
- `btn-build` bleu — construction, investissement
- `btn-neutral` blanc semi-transparent — action neutre (navigation, fermer)

**Domain colors** — associées aux types de bâtiments :
- Construction : orange
- Énergie : jaune
- Éco/bio : vert
- Recherche : bleu
- Diplomatie : violet (`diplomacy`) — contrats avec les États, relations inter-entités

**Terrain tags** — palette de lecture rapide des types de tuiles. Ne pas réutiliser ces couleurs
hors du contexte terrain (badges tuile, minimap).

## Typography

Trois tailles seulement — respecter la hiérarchie :
- `title` (14px bold) — nom de la tuile, titre de section principale
- `body` (12px) — valeurs, descriptions, texte de bouton
- `caption` (10px, letter-spacing 1.2px) — labels de section en majuscules, métadonnées

Les captions de section suivent une convention `ALL CAPS` + `letter-spacing: 1.2px`
pour les distinguer des labels body. Ne pas appliquer bold sur les captions sauf exception.

## Layout

Le HUD est composé de zones fixes :
- **TopBar** — barre haute fixe, `height: 40px`, largeur 100%
- **TileInspector** — panneau gauche fixe, `width: 260px`, de `topbar-height` à fond
- **RightPanel** — panneau droit flottant (position absolue, droit)

Les panneaux flottants n'ont pas de largeur fixe — s'adaptent au contenu avec un `min-width`.

## Shapes

Deux rayons seulement — ne pas en introduire d'autres :
- `rounded.card` (4px) — boutons, badges, petits éléments
- `rounded.panel` (6px) — panneaux, cartes de bâtiment

Les séparateurs sont des lignes 1px sans rayon.

## Components

**Panel** (`hud-panel`) — conteneur de base. Variante `hud-panel--light` pour les niveaux
imbriqués (ex: carte bâtiment dans un panneau).

**Buttons** — toujours une couleur de fond sémantique. Le hover est la variante éclaircie
(+~15% luminosité). L'état `:active` utilise `scale: 0.97` pour le feedback tactile.
Ne jamais utiliser de `border-width: 0` sur les boutons — la bordure `panel-border` est requise
pour la visibilité sur fond sombre.

**Section headers** (`.tile-inspector__section-title`) — style caption, all-caps, `text-secondary`.
Espacement : `margin-bottom: 6px`.

**Bottom Action Bar** (`.bottom-action-bar`) — barre fixe en bas d'écran, `height: 52px`.
Contient 5 onglets pill (`.bottom-action-bar__tab`) représentant les axes de progression
de la corporation : Territoire, Construction, Marché, Contrats, Terraform. Chaque axe a sa
couleur d'accent domain au survol et à l'état actif. Fond semi-transparent identique à la
TopBar. L'onglet actif affiche un `border` 1px dans la couleur de son axe et un fond teinté
à 12% d'opacité.

**Terrain badge** — fond coloré `terrain-*`, rayon `card`, texte `text-primary` 10px.
Utiliser uniquement les couleurs `terrain-*` pour ces badges.

## Do's and Don'ts

- **DO** : utiliser exclusivement les variables CSS de `variables.uss` — jamais de valeur hardcodée
- **DO** : maintenir `variables.uss` et ce fichier en sync à chaque ajout de token
- **DON'T** : ajouter de nouvelles couleurs sans les déclarer ici et dans `variables.uss`
- **DON'T** : utiliser fond opaque sur les panneaux (rupture du lien visuel avec le monde 3D)
- **DON'T** : introduire un 3e niveau de rayon (ex: 8px, 12px) sans modifier la section Shapes
- **DON'T** : réutiliser `terrain-*` hors des badges terrain
- **DON'T** : mettre plus de 3 niveaux typographiques dans un même panneau
