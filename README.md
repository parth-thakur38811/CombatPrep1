# CombatPrep

A first-person shooting range made in Unity. It started under one rule: **nothing is
downloaded or imported.**

No 3D models, no textures, no sound files, no fonts. Every gun, every target, every sand
dune and every gunshot is created by code while the game is running. The Unity scene file
contains a single empty object — everything else is built from scratch when you press Play.

The latest version turns the range into a rain-soaked war zone at dusk. For that I relaxed the
rule for surfaces: the ground, concrete, containers, sandbags, the sky and a few props now use
free, public-domain textures and models from [Poly Haven](https://polyhaven.com), the
soldiers you fight online are a rigged model shared under CC BY, animated with Mixamo, and the
gunshots are real recordings from a public-domain firearm library (all listed in
[CREDITS.md](CREDITS.md)). The guns, the other sounds and the lightning are still made in code,
and so were the rain, fire, muzzle-flash and impact effects - which now live as prefabs in
`Assets/Prefabs/FX`, so they can be tuned in the editor.

> **How this was made:** I planned the project, decided what to build, played each version
> and worked out what needed fixing. The code itself was written with the help of
> [Claude Code](https://claude.com/claude-code), an AI coding assistant. I've said so here
> because being straightforward about it matters more to me than appearing to have done it
> alone.

![The shooting range](docs/range-overview.png)

## The idea

Normally when you make a game you download models and sounds that other people made. I
wanted to see what happens if you can't do that — if every single thing has to be produced
by writing code instead.

It turns out you can get quite far:

- **Guns** are built from about 25 of Unity's basic shapes — cubes, cylinders and spheres —
  stacked into a rifle.
- **Textures** are drawn pixel by pixel in code: camouflage patterns, rusted metal, and the
  printed paper target sheets. (Sand and concrete were too, until the war-zone update.)
- **Sounds** are generated as raw audio. A gunshot was three layers mixed together — a sharp
  crack, a low thump, and the echo afterwards. (Since the war-zone update the crack and thump
  are real recordings; the echo is still synthesised under them.)
- **The range itself** — dunes, shipping containers, sandbags, barrels — is assembled by a
  script when the game starts.
- **The storm** is code as well: rain that stops at roofs and splashes where it lands,
  lightning that throws shadows across the arena, thunder that arrives a few seconds later,
  and fires and smoke burning on the horizon.

## What's in it

Five weapons — an assault rifle, submachine gun, marksman rifle, pistol and shotgun — each
with six colour schemes you choose before playing. The patterns are generated too, so the
camouflage and stripes are drawn by code rather than painted by hand.

![Choosing a weapon and finish](docs/loadout-carbine.png)

![The shotgun in the Redline finish](docs/loadout-shotgun.png)

Targets are paper silhouettes hanging in steel frames. They swing backwards when you hit
them and drop flat when you knock them down, then pop back up with a fresh sheet. Some
slide sideways, some bob up and down, and some jump to random positions, so you can
practise different kinds of aiming.

![Hitting a target, with damage numbers](docs/hit-feedback.png)

## Some things I wanted to get right

**Recoil you can learn.** Each gun kicks in the same pattern every time instead of
randomly, so with practice you can pull down against it — the way it works in competitive
shooters. Pulling against the recoil properly cancels it out rather than fighting the
game.

**An honest crosshair.** The crosshair opens up by exactly as much as your bullets actually
spread. It isn't a decoration; it's showing you the real number.

**Sights that work.** Each scope is a hollow ring you genuinely look through, rather than a
picture pasted over the screen. The red dot and the scope's crosshair are drawn on the sight's
glass, pinned to the exact point your bullets go - so they stay on target while the gun kicks -
and stay one pixel thin at any zoom.

![Looking through the marksman rifle scope](docs/scope-dmr.png)

## Running it

You'll need **Unity 6000.4.8f1** or newer.

1. Open the project in Unity.
2. In the menu bar, choose **CombatPrep → Build Range Scene**.
3. Press **Play**, pick a weapon, and deploy.

| Key | Does |
|---|---|
| `WASD` / Mouse | Move / look |
| Left click / Right click | Fire / aim |
| `R` | Reload |
| `Shift` / `Ctrl` / `Space` | Sprint / crouch / jump |
| `Esc` | Back to weapon selection |

### Soldier animations (online play)

Other players appear as an animated soldier. The animations come from Mixamo, whose terms don't
allow sharing the files, so they aren't in this repository - without them, online players are
drawn as simple block soldiers instead. To add them:

1. Sign in at [mixamo.com](https://www.mixamo.com) (free Adobe account).
2. Download rifle animations for idle, walking and running (forwards, backwards and to the
   sides), crouching (still and walking), firing and a death. Choose **FBX for Unity** and
   **Without Skin**, and leave **In Place** unticked - the game uses how far each clip travels
   to match its pace to the players' real speed.
3. Put the files in `Assets/Art/Characters/Mixamo/`. Unity sorts them by their names and builds
   the soldier's animation controller by itself.

## How the code is organised

```
Assets/Scripts/
  Core/      Startup, the range builder, and the shape/texture/material helpers
  Player/    Movement, mouse look, input
  Weapons/   The five weapons, recoil, spread, and how guns are assembled
  Skins/     Colour schemes and pattern generation
  Targets/   Targets, how they're built, and how they move
  Audio/     Sound generation
  FX/        Effect recipes and players (rain, fire, muzzle flash, tracers, impacts), storm, camera shake
  UI/        Heads-up display and the weapon selection menu
```

If you'd like the detailed technical reasoning behind the aiming and recoil systems, it's
written up in [COMBATPREP.md](COMBATPREP.md).

## Built with

Unity 6000.4.8f1, C#, Universal Render Pipeline
