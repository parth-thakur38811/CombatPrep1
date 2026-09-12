# CombatPrep

A first-person shooting range built in Unity under one hard constraint: **no imported
assets of any kind.**

No meshes, textures, materials, audio files, sprites, fonts, prefabs or authored scenes.
Every rifle, every paper target, every sand dune, every gunshot and the entire interface is
generated in code at run time. The Unity scene contains exactly one empty GameObject.

<img width="1102" height="598" alt="Screenshot 2026-09-12 172240" src="https://github.com/user-attachments/assets/23daa8ec-e2c6-4a5a-bfe6-d1eaba95db4b" />
<img width="1107" height="587" alt="Screenshot 2026-09-12 172036" src="https://github.com/user-attachments/assets/34f5d327-6862-4c0e-8122-7737c2ff4871" />
<img width="1095" height="592" alt="Screenshot 2026-09-12 172141" src="https://github.com/user-attachments/assets/5c802f5c-a851-4ea8-825c-3edc62ca7ba1" />
<img width="1106" height="602" alt="Screenshot 2026-09-12 171011" src="https://github.com/user-attachments/assets/c990b279-3452-401c-a4bd-2e226981aa71" />


## Why the constraint is interesting

Removing imported assets removes the usual answer to every problem, so each one has to be
solved rather than bought:

| Normally an asset | Here |
|---|---|
| Weapon and target models | ~25 scaled Unity primitives per weapon, assembled from a `WeaponShape` spec |
| Textures | `Texture2D` drawn pixel by pixel — camouflage, hex, stripes, concrete, sand, and the printed target sheet |
| Gunshot audio | Three synthesised voices — a high-passed noise *crack*, a downward-sweeping sine *body*, and a low-passed *tail* — summed and soft-clipped |
| Level geometry | Procedurally assembled desert range, deterministically seeded so the layout is identical every run |
| Reticles | Emissive geometry on the sight axis, inside a ring-built optic with a genuinely open bore |

## Running it

Requires **Unity 6000.4.8f1** or newer with the Universal Render Pipeline.

1. Open the project in Unity.
2. Menu bar: **CombatPrep → Build Range Scene** (`Ctrl+Shift+R`).
3. Press **Play**, choose a weapon and finish, and deploy.

| Input | Action |
|---|---|
| `WASD` / `Mouse` | Move / look |
| `LMB` / `RMB` | Fire / aim down sights |
| `R` | Reload |
| `Shift` / `Ctrl` / `Space` | Sprint / crouch / jump |
| `Esc` | Return to loadout |

## Selected engineering details

**Recoil is two decoupled layers.** [`PlayerLook`](Assets/Scripts/Player/PlayerLook.cs)
owns the layer that actually moves bullets; [`WeaponAnimator`](Assets/Scripts/Weapons/WeaponAnimator.cs)
owns the purely cosmetic kick and sway. 

**Recoil compensation.** Recoil accumulates in its own value, separate from player aim.
Mouse input opposing the recoil is spent shrinking that accumulator *before* it reaches the
base aim, so pulling down cancels the climb instead of fighting the recovery — and on
release the weapon returns only by the amount the player did not compensate.

**Learnable spray patterns.** [`RecoilSystem`](Assets/Scripts/Weapons/RecoilSystem.cs)
derives each shot's offset from seeded Perlin noise indexed by shot number rather than from
`Random`. 

**The crosshair is honest.** Its gap is computed from the real spread cone, projected
through the camera FOV into screen pixels — so the interface blooms by exactly as much as
the bullets actually scatter.

**Sight alignment is solved, not tuned.** The aim-down-sights pose is derived by solving
for the transform that places the optic's sight point on the camera axis at the weapon's
own eye relief, so a compact red dot and a 200 mm scope both align correctly with no
hand-placed offsets.

**Everything is built at run time.** [`Bootstrap`](Assets/Scripts/Core/Bootstrap.cs)
constructs lighting, post-processing, the range, the player, the weapon and the targets on
start. Nothing lives in the scene file, which means no binary scene assets and no merge
conflicts.

## Structure

```
Assets/Scripts/
  Core/      Bootstrap, RangeBuilder, Prim (primitives), Mat (materials),
             Tex (procedural textures), Spring
  Player/    GameInput, PlayerMotor, PlayerLook
  Weapons/   WeaponLibrary, WeaponDefinition, Weapon, RecoilSystem,
             SpreadSystem, WeaponModelBuilder, WeaponAnimator
  Skins/     SkinLibrary, SkinApplier
  Targets/   Target, TargetBuilder, TargetMover
  Audio/     Synth, GameAudio, ListenerRig
  FX/        FxSystem, CameraShake
  UI/        Hud, MainMenu
```

Five weapons spanning the handling space — assault rifle, SMG, marksman rifle, sidearm and
a nine-pellet shotgun — each with six interchangeable finishes.

## Design notes

[COMBATPREP.md](COMBATPREP.md) documents the reasoning behind the feel systems in more
depth, along with the current known limitations.

## Built with

Unity 6000.4.8f1 · Universal Render Pipeline 17.4 · Input System 1.19 · C#
