# AGENTS.md — Game-unity-terra

> Point d'entrée pour tout agent IA (Codex, Claude Code, Copilot, Cline).
> Vision globale et carte des repos : repo privé `TerraformingPlanet/docs`.

## Rôle de ce repo

Client **Unity 6 (URP)** du serveur de terraformation. C'est un **client de rendu et
d'interaction uniquement** : caméra, 3 vues (Système solaire → Planète → Local), HUD
UI Toolkit, visualisation des snapshots. **Aucune logique métier autoritaire nouvelle
ici** — elle appartient à `terraformation-server` (SimulationCore/DedicatedServer).

**Statut** : client de référence / dashboard pendant la construction du client
Satisfactory (`satisfactory-terraform-mod`). Sera archivé à terme. En attendant :

- La géométrie de la sphère de Goldberg (`Assets/Scripts/World/Hexasphere/`,
  `Generation/GoldbergSphereGenerator.cs`, `Rendering/PlanetSphereGoldberg*.cs`,
  `GoldbergFaceColorizer.cs`) est la **spec de référence du portage C++ UE** —
  ne pas la casser, documenter tout changement.

## Démarrage

1. Serveur d'abord : `docker compose up -d` dans le repo `terraformation-server`
   (DedicatedServer sur `http://localhost:8080`).
2. Ouvrir le projet avec Unity 6 LTS, scène `Assets/Scenes/Game.unity`, Play.

## Contrats de données

`SimulationContracts.cs` (C#) doit refléter fidèlement les modèles Pydantic du serveur.
Source de vérité : `terraformation-server/Documentation/SIMULATION_CONTRACTS.md`.
Procédure : skill `simulation-contract-sync` (dans `.github/skills/` du repo serveur).

## Références architecture

Toute l'architecture client (vues, Goldberg skirts, rendu eau, HUD, WebSocket) est
documentée dans `terraformation-server/Documentation/ARCHITECTURE.md` — la lire avant
tout refactor ici.

## Règles dépôt

- Repo LFS : gros binaires assets via LFS uniquement. **Jamais de fichier >100 Mo**
  (GitHub le refuse — incident `Packages/com.unity.ai.assistant/RelayApp~/`, package
  depuis supprimé : ne pas le réintroduire en version embarquée).
- `Library/`, `Temp/`, `Logs/` ne se commitent jamais (déjà dans `.gitignore`).
