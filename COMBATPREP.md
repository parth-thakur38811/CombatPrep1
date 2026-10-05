# CombatPrep — how it works

Unity 6000.4.8f1 · URP 17.4 · Input System 1.19 · Netcode for GameObjects 2

Most of this project is generated in code. The scene holds two GameObjects: one with
`Bootstrap` on it, which builds the arena, the targets, the player, the guns, the HUD, every
procedural texture and every synthesised sound at runtime, and the network manager for online
play. Since the war-zone update some surfaces, the sky, a few props, the online soldier and the
gunshot recordings are imported art (listed in [CREDITS.md](CREDITS.md)). Each goes through an
editor builder, and the game falls back to its procedural version wherever one is missing.

## Running it

1. In the Unity menu bar: **CombatPrep → Build Range Scene** (or `Ctrl+Shift+R`).
2. Press **Play**.
3. Pick a weapon and a finish, then **PRACTICE**, or **PLAY ONLINE** to host or join a match.

## Controls

| Input | Action |
|---|---|
| `WASD` | Move |
| `Mouse` | Look |
| `LMB` | Fire |
| `RMB` | Aim down sights |
| `R` | Reload |
| `1`–`5` | Switch weapon |
| `G` | Grenade (hold `LMB` to aim, release to throw) |
| `Shift` | Sprint |
| `Ctrl` | Crouch |
| `Space` | Jump |
| `Esc` | Back to the menu (online, leaves the match) |

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
the bore is genuinely clear and you aim *through* the sight. The ADS pose is solved to put
`SightPoint` dead centre for every weapon, and the red dot and scope reticle are drawn in
screen space on the sight's glass (`Reticle.shader`), anchored to the exact point of impact —
so they stay on target while the gun kicks and stay one pixel thin at any zoom. Magnified
optics align from just behind the rear ring so the eye sits at a realistic eye relief instead
of inside the tube.

**Per trigger pull, not per bullet.** A shotgun resolves nine independent rays but reports
one hit and one hit marker, so accuracy stats stay meaningful.

**Sound is synthesised, except the gunshots.** `Synth` builds every other clip as a float
buffer. A gunshot used to be three synthesised voices — a high-passed noise *crack*, a
downward-sweeping sine *body*, and low-passed noise *tail* — summed and soft-clipped so the
transient saturates instead of digitally clipping. The crack and body are now real recordings,
several takes per gun played in turn so repeated shots don't sound identical; the synthesised
tail still plays under them, built from each weapon's own parameters, so the shotgun booms and
the SMG snaps.

**Arms that reach, not animations.** The first-person arms are the online soldier's arms, cut
out of its mesh (`FirstPersonArms`). Every frame each one is a two-joint reach — shoulder to
elbow to wrist — for the grip and the fore-end of the gun in hand, so the hands follow kick,
sway, sprint and reload without a single authored animation. A pistol is held in both hands
by the grip, the off hand mirroring the firing hand.

## Online

Up to four players, every one for themselves, joined with a short code through Unity's Lobby
and Relay services. The host is also the server.

- **Movement is the owner's.** Each player moves their own body and the others see it
  smoothed, so moving feels exactly as it does offline.
- **"Favour the shooter".** The shooter's own machine traces the bullets, so shooting feels
  instant despite lag. What it sends is which gun fired and what each pellet hit; the server
  checks the shot (whose player it is, the gun's fire rate, its range) and works out the
  damage itself from that gun, never from a number the client sends.
- **Grenades are the server's.** The thrower's grenade appears at once on their screen, while
  the server flies its own copy and, at the fuse, damages every player with a clear line from
  the blast to their feet, chest or head — the thrower and the host included.
- **The arena is never sent.** It's built from a fixed random seed, so every machine builds
  the same one.
- **Other players** are the rigged soldier with Mixamo animations driven by what the network
  already carries — speed and direction, crouch, aim pitch, shots — holding a copy of their gun
  in their finish. Hit boxes ride the animated bones.

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
  Core/      Bootstrap (flow + lighting + post-fx), RangeBuilder (the seeded arena),
             PlayerRigBuilder, Prim, Mat, Tex, Spring
  Player/    GameInput, PlayerMotor, PlayerLook (aim recoil), FirstPersonArms
  Weapons/   WeaponLibrary (the roster), WeaponDefinition, Weapon, WeaponLoadout, RecoilSystem,
             SpreadSystem, WeaponModelBuilder (+ optics), WeaponAnimator (visual recoil),
             Grenade, GrenadeThrower
  Skins/     SkinLibrary, SkinApplier
  Targets/   Target, TargetBuilder (paper + frame), TargetMover
  Net/       SessionService (Lobby + Relay), NetPlayer (combat RPCs), MatchManager,
             AvatarBuilder + SoldierView (other players), RemoteHitProxy
  Audio/     Synth, GameAudio
  FX/        FxRecipes + FxSystem (effects), Weather (rain), Storm (sky, lightning, thunder),
             FireFx, VolumetricLight, CameraShake
  UI/        Hud, MainMenu, LobbyMenu
Assets/Editor/
  SceneSetup.cs       CombatPrep → Build Range Scene
  NetworkSetup.cs     the network manager and network prefabs
  ArtBuilder.cs       imported surfaces, props, sky and gunshots → materials and the ArtLibrary
  SoldierBuilder.cs   the soldier model and its Mixamo animation controller
  FxPrefabBuilder.cs  effect prefabs from FxRecipes
  RenderingSetup.cs   the light-in-the-air pass and shader keep-alives
Assets/Shaders/   StormSky, VolumetricLight, Reticle
```

## Known limits

- **Hitscan only.** `WeaponDefinition.Hitscan` is `true`; travel time, drop and drag are
  still to come. `MuzzleVelocity` is already wired through every weapon for it.
- **The gun can clip into walls.** Fixing it properly needs a URP overlay camera stack
  rendering the weapon on its own layer. Near clip is at 0.012 as a stopgap.
- **No match end.** Practice is free play; stats accumulate until you go back to the menu.
  Online matches are open-ended too: kills and deaths are counted, but there's no time or
  score limit and no scoreboard yet.
- **Built for friends, not strangers.** Shooters decide their own hits (checked by the host),
  which feels right among friends; a public competitive game would need the server to rewind
  time and check hits itself. And there's no host migration — if the host leaves, the match
  ends for everyone.
