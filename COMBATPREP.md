# CombatPrep — asset-free FPS range

Unity 6000.4.8f1 · URP 17.4 · Input System 1.19

Everything in this project is generated in code. There are no meshes, textures, materials,
audio files, sprites, fonts, prefabs or authored scenes. The scene contains exactly one
empty GameObject with `Bootstrap` on it; the range, the targets, the player, the gun, every
texture and every sound are built at runtime.

## Running it

1. In the Unity menu bar: **CombatPrep → Build Range Scene** (or `Ctrl+Shift+R`).
2. Press **Play**.
3. Pick a weapon and a finish, then **DEPLOY**.

## Controls

| Input | Action |
|---|---|
| `WASD` | Move |
| `Mouse` | Look |
| `LMB` | Fire |
| `RMB` | Aim down sights |
| `R` | Reload |
| `Shift` | Sprint |
| `Ctrl` | Crouch |
| `Space` | Jump |
| `Esc` | Back to loadout |

## The loadout screen

Five weapons, six finishes, on a live rotating model. The preview is built by the same
`WeaponModelBuilder` the game uses and painted by the same `SkinApplier`, on a small stage
parked 500 m below the range with its own camera — so what you see is literally the gun you
spawn with, and there is no preview art that can drift out of sync.

| Weapon | Role | Notes |
|---|---|---|
| MK-4 CARBINE | Assault rifle | The baseline. Red dot, 640 rpm, learnable climb. |
| WASP-9 | SMG | 950 rpm, low per-shot kick that stacks fast, forgiving on the move. |
| LONGBOW DMR | Marksman | Semi-auto, magnified scope, heavy kick, punishing spread per shot. |
| P-11 SIDEARM | Sidearm | Iron sights, snappy ADS, steep damage falloff. |
| BREACH-12 | Shotgun | Nine pellets per trigger pull through one wide cone. |

Skins are data, not art: a colour per part group plus an optional generated pattern
(`Camo`, `Digital`, `Stripes`, `Hex`, `Weathered`) drawn pixel by pixel in `Tex`. Because
the weapon is ~25 individually tagged primitives, a skin can recolour the receiver,
furniture, barrel, magazine, optic and accents independently. The reticle and the objective
glass are deliberately **not** registered as skinnable parts — a skin must never be able to
paint over the sight.

## How the feel is built

**Recoil is two independent layers.** `PlayerLook` owns the *aim* layer — the part that
actually moves your bullets. `WeaponAnimator` owns the *visual* layer — kick, roll, sway,
bob — which is cosmetic and never affects point of impact. Mixing these is the single most
common reason a shooter feels bad.

**Recoil compensation.** `PlayerLook` keeps recoil in its own accumulator, separate from
your aim. Mouse input that opposes the recoil is spent shrinking that accumulator before it
reaches your base aim, so pulling down cancels the climb rather than fighting the recovery.
On release the gun springs back only by the amount you did *not* compensate.

**The spray pattern is deterministic.** `RecoilSystem` generates each shot's offset from
seeded Perlin noise indexed by shot number, not from `Random`. Same weapon, same shot
index, same offset — so the pattern is learnable like CS or Valorant, but authoring a new
one is a single `PatternSeed` instead of thirty hand-placed keyframes.

**Spread is separate from recoil.** Recoil is the predictable part, spread is the random
part. The crosshair gap in `Hud.SetSpread` is derived from the real cone half-angle
projected through the camera FOV into pixels — it is not a fudge factor, so the UI blooms
exactly as much as your bullets actually scatter.

**Springs, not lerps.** Kick, sway, camera shake and the target swing all run through damped
spring integrators, substepped for low-framerate stability. Springs overshoot and settle;
lerps slide. That difference is most of what "punchy" means.

**Shots trace from the camera, drawn from the muzzle.** The raycast starts at the eye, then
the tracer is drawn from the muzzle to wherever that trace landed. Firing from the muzzle
directly is the classic FP bug where shots taken while hugging cover hit the wall you are
leaning past.

**Sights are open, not solid.** Optics are built as rings of segment boxes (`Prim.Ring`) so
the bore is genuinely clear and you aim *through* the sight. The reticle is an emissive
object on the sight axis; since the ADS pose is solved to put `SightPoint` dead centre, it
lands exactly on the screen centre for every weapon. Magnified optics align from just
behind the rear ring so the eye sits at a realistic eye relief instead of inside the tube.

**Per trigger pull, not per bullet.** A shotgun resolves nine independent rays but reports
one hit, one damage number and one hitmarker, so accuracy stats stay meaningful.

**Sound is synthesised.** `Synth` builds every clip as a float buffer. A gunshot is three
voices — a high-passed noise *crack*, a downward-sweeping sine *body*, and low-passed noise
*tail* — summed and soft-clipped so the transient saturates instead of digitally clipping.
Each weapon regenerates the report from its own synth parameters, so the shotgun genuinely
booms and the SMG genuinely snaps.

## Targets

Paper silhouettes hung in steel frames, the shooting-range kind. The printed sheet is a
generated 512×768 texture (`Tex.TargetSheet`): aged card stock, a printed torso silhouette,
and a concentric scoring bullseye over centre mass, drawn at 2:3 so the rings stay circular
on a board of the same proportions. Head and centre-mass colliders are positioned to line
up with what is actually printed.

Rounds rock the board on its top pivot; enough damage swings it flat downrange, where it
hangs for a beat before popping back up with a fresh sheet. The swing is the feedback —
legible from 70 m, which a colour flash is not. Bullet holes are parented to the board, so
they travel with a moving target.

Four movement styles, each training something different: `Strafe` for lead and tracking,
`Bob` for vertical correction, `Jitter` for reactive micro-adjustment, `Static` for zeroing.

## Where to tune

Weapon feel lives in `WeaponLibrary` — one block per weapon pairing tuning
(`WeaponDefinition`) with proportions (`WeaponShape`). Adding a sixth weapon is one method
there, not a new model. To tune live in the Inspector instead, right-click in the Project
window → **Create → CombatPrep → Weapon**.

The knobs worth reaching for first:

- `RecoilVertical` / `RecoilHorizontal` — how hard the gun climbs and walks
- `PatternRampShots` — how many shots before the climb settles
- `SpreadPerShot` / `MaxSpread` — how fast accuracy degrades under sustained fire
- `KickBack` / `KickPitch` / `KickStiffness` — the visual punch only
- `ShotDecay` / `ShotBodyHz` — tighter vs boomier report
- `OpticBore` / `SightDistance` in `WeaponShape` — how much you see through the sight

## Layout

```
Assets/Scripts/
  Core/      Bootstrap (flow + lighting + post-fx), RangeBuilder, Prim, Mat, Tex, Spring
  Player/    GameInput, PlayerMotor, PlayerLook (aim recoil)
  Weapons/   WeaponLibrary (the roster), WeaponDefinition, Weapon, RecoilSystem,
             SpreadSystem, WeaponModelBuilder (+ optics), WeaponAnimator (visual recoil)
  Skins/     SkinLibrary, SkinApplier
  Targets/   Target, TargetBuilder (paper + frame), TargetMover
  Audio/     Synth, GameAudio
  FX/        FxSystem (tracers, impacts, holes), CameraShake
  UI/        Hud, MainMenu
Assets/Editor/
  SceneSetup.cs   CombatPrep → Build Range Scene
```

## Known limits

- **Hitscan only.** `WeaponDefinition.Hitscan` is `true`; travel time, drop and drag are
  still to come. `MuzzleVelocity` is already wired through every weapon for it.
- **The gun can clip into walls.** Fixing it properly needs a URP overlay camera stack
  rendering the weapon on its own layer. Near clip is at 0.012 as a stopgap.
- **No scope vignette.** A magnified optic currently shows the world through the tube with
  no black surround outside the objective, which is technically honest but reads less like
  a scope than the PUBG reference does.
- **No round timer or score run.** The range is free-play; stats accumulate until you go
  back to the loadout screen.
