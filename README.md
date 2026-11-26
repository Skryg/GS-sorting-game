# Last Shift — VR Sorting Arcade on Gaussian Splatting

A VR arcade game where every object you interact with is a real **Gaussian Splatting** asset. You stand at a sorting station, segregating incoming items into the right baskets while fending off spiders that swarm your workstation.

Built on top of the **[GS-Verse](https://github.com/Anastasiya999/GS-Verse)** engine (mesh-based Gaussian Splatting for physics-aware VR interaction), which itself builds on the [Unity Gaussian Splatting](https://github.com/aras-p/UnityGaussianSplatting) playground.

- **Engine:** Unity 6000.3 (URP, single pass stereo)
- **VR stack:** OpenXR 1.16.1 + XR Interaction Toolkit 3.3.2
- **Target platform:** Meta Quest 3 (standalone Android, Vulkan, IL2CPP).

---

## Gameplay

The game splits your attention between two channels: **calm-but-precise sorting** on the conveyor belt and **reflex-driven combat** against attacking enemies.

- **Conveyor belt** — items of different categories ride toward you. Grab each one and drop it into the matching basket.
  - Correct sort → **+ points**.
  - Wrong basket, or item falls off the belt → **− points and some health**.
  - Special items: ice melts in your hand, a bomb has to go into any bin before it explodes, fakes crumble.
- **Spiders (enemies)** — spiders spawn and move to attack your station. Shoot them with the plasma gun.
  - A spider reaching you and attacking → **you lose health**.
- **Endless mode** — no timer. Health slowly regenerates; the run continues **until it drops to zero**, then it's game over. Survive as long as you can and beat your high score.

As the run goes on, difficulty scales up: faster belt, denser spawns, more spiders at once.

> Full design (mechanics, scoring balance, difficulty curve, Unity architecture, performance budget) lives in the **[Game Design Document](docs/GDD.md)**.

---

## Project structure

```
.
├── Assets/
│   ├── RoomScenes/      # StartMenuScene (menu) and MainGame (the game), the only build scenes
│   ├── Scripts/         # GameScripts, MenuScripts, UIScripts; GSVerse/ = scripts taken over from GS-Verse
│   ├── Resources/       # loaded by path only: sortable item prefabs, their catalog and GaMeS meshes
│   ├── GaussianAssets/  # splat data
│   ├── Audio/, Icons/, Materials/, Models/, Prefabs/, Settings/ ...
│   └── third-party: Spiders, SciFi Warehouse Kit (Asset Store, not in the repo), PlyImporter-master, Samples
├── Packages/            # UPM manifest (the GS renderer is referenced as file:../package)
├── ProjectSettings/     # Unity project settings
├── package/             # Gaussian Splatting renderer (org.nesnausk.gaussian-splatting, MIT), modified
├── scripts/             # Python helpers for GS asset creation (import_params.py, ...)
├── docs/                # GDD + reference images
├── LICENSE.md           # MIT (see Licensing)
└── README.md
```

`package/` is the Gaussian Splatting renderer package (`org.nesnausk.gaussian-splatting`) taken from GS-Verse
at commit [`a70c39f`](https://github.com/Anastasiya999/GS-Verse/tree/a70c39f/package), which extends
Aras Pranckevičius' Unity Gaussian Splatting. `Packages/manifest.json` references it as `file:../package`,
so the project opens without fetching it. It is **not** an unmodified copy: the game depends on the changes
listed in [Changes to GS-Verse code](#changes-to-gs-verse-code), so do not replace it with the upstream package.

---

## Getting started

```bash
git clone <this-repo-url>
cd GS-sorting-game
```

1. Open the project in **Unity 6000.3.28f1** (other Unity 6000.3.x may work).
2. Unity loads the GS renderer package from `package/` automatically.
3. Import the two free Unity Asset Store packs (their license does not allow publishing them here).
   A window with links opens by itself while they are missing (also under **Tools > Asset Store Packages**):
   - [Fantasy Spider](https://assetstore.unity.com/packages/3d/characters/animals/insects/fantasy-spider-236418)
   - [Sci-Fi Construction Kit (Modular)](https://assetstore.unity.com/packages/3d/environments/sci-fi/sci-fi-construction-kit-modular-159280)

   For each: **Add to My Assets** on the page, then in Unity **Window > Package Manager > My Assets**, select it,
   **Download** and **Import** with all files. Do not move the imported folders (`Assets/Spiders`,
   `Assets/SciFi Warehouse Kit`): the scenes find the assets by their IDs. The materials are converted to URP
   automatically after the import. Our changes (the spider's scale, animator, collider and AI) live in
   `Assets/Prefabs/Enemies/`, so the packs stay unmodified. The game was made with the versions downloaded
   in August (spider) and October 2026 (kit).
4. Gaussian Splatting assets live in `Assets/GaussianAssets/`, their GaMeS meshes (`*.obj`) directly in `Assets/Resources/`.
5. New sortable items go to `Assets/Resources/Prefabs/GameObjects/` (Tools > Gaussian Splats > Create Game Object Prefab).
   The catalog the game reads (`Resources/GameObjectCatalog.asset`) is rebuilt automatically.

For creating your own GS assets (COLMAP / single image → GaMeS → Unity), see the
GS-Verse [asset-creation guide](https://github.com/Anastasiya999/GS-Verse#asset-creation)
and the helper scripts in `scripts/`.

### VR / simulator

- Nothing has to be switched between the headset and the simulator. In Play mode without a headset the
  XR Device Simulator is added automatically, and it is removed as soon as a headset (e.g. Quest Link) is
  detected (`Assets/Scripts/EditorXRSimulatorBootstrap.cs`). It exists only in the Editor, never in a build.
- Input is set up with XR Interaction Toolkit + Input System over OpenXR. Enabled interaction profiles:
  - Android (Quest build): Meta Quest support, Oculus Touch, Meta Quest Touch Plus.
  - PC (Play mode): Oculus Touch, Valve Index, HTC Vive, KHR Simple, hand interaction.

---

## Changes to GS-Verse code

Compared with GS-Verse [`a70c39f`](https://github.com/Anastasiya999/GS-Verse/tree/a70c39f), the version this project started from.

**Renderer (`package/`)**
- Standalone Quest rendering: splats are sorted and projected before URP's main pass and drawn straight into
  the camera target, so URP keeps a single native render pass on the tile-based GPU. Both eyes are drawn in
  one pass (single-pass stereo). Splats that are opaque enough write depth (`Depth Alpha Cutoff` on the
  renderer feature), so particles behind them are hidden.
- All splat objects share GPU buffers and are sorted together into one draw, so overlapping objects blend
  correctly. The data of an asset is uploaded once and shared by all its objects, also across respawns.
- Sorting and view dependent colors are only refreshed when the view direction changes by
  `Sort Angle` / `SH Refresh Angle` degrees; objects outside the view are skipped.
- GaMeS deformation runs on the GPU from the imported asset, the source `.ply` is no longer loaded at runtime.
  **Tools > Gaussian Splats > Bake GaMeS Splat Order** links the imported splats back to the mesh faces.
- Per-object effects used by the game: disintegration, melting and tint.
- **Tools > Gaussian Splats > Merge Splats** saves a copy of an asset with fewer splats.
- Fixes: mesh leaks on every spawn, Editor-only code that broke player builds, deprecated Unity APIs.

**Scripts (`Assets/Scripts/GSVerse/`)**
- `BaseGSVerse`: uses the GPU deformation above, frees its per-rebuild data and runtime meshes
  (`GSVerseSegmented` still builds from the `.ply`).
- `GpuInflateAndReadback`: an unused field removed. All other GS-Verse scripts and PlyImporter are unchanged.

---

## Licensing & attribution

The engine code is **MIT-licensed** — see [`LICENSE.md`](LICENSE.md) (© 2023 Aras Pranckevičius)
and [`package/LICENSE.md`](package/LICENSE.md). This game reuses:

- **[Unity Gaussian Splatting](https://github.com/aras-p/UnityGaussianSplatting)** — the GS renderer (`package/`), MIT.
- **[GS-Verse](https://github.com/Anastasiya999/GS-Verse)** — mesh-based GS interaction layer this project builds on.
- **[PlyImporter](https://github.com/3DBear/PlyImporter)** (© 2019 3DBear), MIT.

> **Note on GS models:** assets generated through the INRIA
> [gaussian-splatting](https://github.com/graphdeco-inria/gaussian-splatting) /
> [GaMeS](https://github.com/waczjoan/gaussian-mesh-splatting) pipeline carry a
> **non-commercial research license**. That's fine for research/demo use, but relevant
> if you ever intend to commercialize. Third-party art assets bundled under `Assets/`
> keep their own licenses.

The MIT copyright notices above are retained as required by the license.

### Third-party assets (credits)

The same list is shown in the game under **Credits** (`Assets/Scripts/MenuScripts/CreditsText.cs`).

**3D models** — Sketchfab, [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/). Converted into Gaussian splats (GaMeS) for this game.

| Model | Author |
|---|---|
| [Apple](https://sketchfab.com/3d-models/apple-da0cfd4c6a4e41c5aaf06797b463235b) | Moshe Caine |
| [Orange fruit](https://sketchfab.com/3d-models/orange-fruit-cbdf758f21924c168c1c3da1afed9754) | polyplant3d |
| [Tools / Monkey Tool](https://sketchfab.com/3d-models/tools-monkey-tool-589a23fe7b2c4e91b86b5f6581c9a7e5) | ROY |
| [Digital Radio Clock](https://sketchfab.com/3d-models/digital-radio-clock-23b2d0e81d414836877c555e5b5fc18d) | NoMoreFeelings |
| [Electronic Calculator Citizen](https://sketchfab.com/3d-models/electronic-calculator-citizen-gameready-free-0e325a2314b34282b208343509007f1a) | Dokazvo |
| [Decretive Medieval Plate](https://sketchfab.com/3d-models/decretive-medieval-plate-free-cfd91b3b51c34f08a6c29651586d3194) | wolfgar74 |
| [Sci-Fi Fusion Plasma Gun](https://sketchfab.com/3d-models/sci-fi-fusion-plasma-gun-45f39027cac3435da3361d901a2aaef9) | Abdooelsabry |

**Unity Asset Store** (Standard Unity Asset Store EULA, not included in this repository, see Getting started)
- [Fantasy Spider](https://assetstore.unity.com/packages/3d/characters/animals/insects/fantasy-spider-236418)
- [Sci-Fi Construction Kit (Modular)](https://assetstore.unity.com/packages/3d/environments/sci-fi/sci-fi-construction-kit-modular-159280) — the warehouse room

**Sounds and music** — all [CC0](https://creativecommons.org/publicdomain/zero/1.0/)
- Kenney: [Interface Sounds](https://kenney.nl/assets/interface-sounds), [Sci-fi Sounds](https://kenney.nl/assets/sci-fi-sounds), [Music Jingles](https://kenney.nl/assets/music-jingles)
- [Conveyor belt](https://freesound.org/people/TiesWijnen/sounds/478247/) — TiesWijnen (Freesound)
- [80 CC0 creature SFX](https://opengameart.org/content/80-cc0-creature-sfx) and [#2](https://opengameart.org/content/80-cc0-creture-sfx-2) — rubberduck (OpenGameArt)
- [Monster Sound Effects Pack](https://opengameart.org/content/monster-sound-effects-pack) — Ogrebane (OpenGameArt)
- [Dark Sci-Fi Audio Pack](https://opengameart.org/content/dark-sci-fi-audio-pack) — SRG774 (OpenGameArt)
- [37 hits/punches](https://opengameart.org/content/37-hitspunches) — Independent.nu (OpenGameArt)
- Crumble, bomb beep and explosion sounds — synthesized for this game

**Icons** — [OpenMoji](https://openmoji.org/), [CC BY-SA 4.0](https://creativecommons.org/licenses/by-sa/4.0/)

**Tools** — [TRELLIS](https://huggingface.co/spaces/trellis-community/TRELLIS) (image-to-3D generation)
