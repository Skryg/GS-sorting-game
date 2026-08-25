# Sortownia — VR Sorting Arcade on Gaussian Splatting

A VR arcade game where every object you interact with is a real **Gaussian Splatting** asset. You stand at a sorting station, segregating incoming items into the right baskets while fending off spiders that swarm your workstation.

Built on top of the **[GS-Verse](https://github.com/Anastasiya999/GS-Verse)** engine (mesh-based Gaussian Splatting for physics-aware VR interaction), which itself builds on the [Unity Gaussian Splatting](https://github.com/aras-p/UnityGaussianSplatting) playground.

- **Engine:** Unity 2022.3.47
- **VR stack:** OpenXR 1.14.3 + XR Interaction Toolkit 3.0.8
- **Target headset:** HTC Vive Focus 3 (standalone, Android build)

---

## Gameplay

The game splits your attention between two channels: **calm-but-precise sorting** on the conveyor belt and **reflex-driven combat** against attacking enemies.

- **Conveyor belt** — items of different categories ride toward you. Grab each one and drop it into the matching basket.
  - Correct sort → **+ points** (builds combo).
  - Wrong basket, or item falls off the belt → **− points and you lose a life**.
- **Spiders (enemies)** — spiders spawn and move to attack your station. Shoot them with your controller.
  - Killing a spider → **+ points**.
  - A spider reaching you and attacking → **you lose life (and points)**.
- **Endless mode** — no timer. The run continues **until your life drops to zero**, then it's game over. Survive as long as you can and beat your high score.

As the run goes on, difficulty scales up: faster belt, denser spawns, more spiders at once.

> Full design (mechanics, scoring balance, difficulty curve, Unity architecture, performance budget for Focus 3) lives in the **[Game Design Document](docs/GDD.md)**.

---

## Project structure

```
.
├── Assets/              # Game scenes, scripts, art, prefabs
├── Packages/            # UPM manifest (GS renderer vendored via file:../package)
├── ProjectSettings/     # Unity project settings
├── package/             # Gaussian Splatting renderer (org.nesnausk.gaussian-splatting, MIT)
├── scripts/             # Python helpers for GS asset creation (import_params.py, ...)
├── docs/                # GDD + reference images
├── LICENSE.md           # MIT (see Licensing)
└── README.md
```

The Gaussian Splatting renderer is **vendored** in `package/` and referenced locally from
`Packages/manifest.json` as `"org.nesnausk.gaussian-splatting": "file:../package"`, so the
project resolves out of the box after cloning — no external package fetch required.

---

## Getting started

```bash
git clone <this-repo-url>
cd GS-sorting-game
```

1. Open the project in **Unity 2022.3.47** (other Unity 2022.3.x may work).
2. Unity resolves the vendored GS package from `package/` automatically.
3. The large Gaussian Splatting model assets are **not committed** (see `.gitignore`).
   Place your GS assets under `Assets/GaussianAssets/` and raw inputs under `Assets/RawData/`.

For creating your own GS assets (COLMAP / single image → GaMeS → Unity), see the
GS-Verse [asset-creation guide](https://github.com/Anastasiya999/GS-Verse#asset-creation)
and the helper scripts in `scripts/`.

### VR / simulator

- On a **real device**, disable the XR Device Simulator in the scene.
- Input is set up with XR Interaction Toolkit + Input System over OpenXR; Focus 3
  controllers are mapped via the OpenXR controller profile.

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
