# CombatPrep - notes for Claude

A first-person shooter in Unity 6000.4.8f1 (URP 17.4, Input System, Netcode for GameObjects
2.x): an offline shooting range (**Practice**) and a 4-player online free-for-all
deathmatch (**Play Online**, Unity Lobby + Relay, join by code) set in a rainy war zone at
dusk. It's the owner's public CV project - see README.md for the pitch and COMBATPREP.md for
the aiming/recoil design. The Unity project is this folder (`C:\Dev\CombatPrep1\CombatPrep1`);
the folder above it is only a container.

## Ground rules

- **Code first.** The scene holds one `Bootstrap` object; the arena, player, guns, HUD and
  most textures and sounds are built in code at runtime. Imported art is allowed, but goes
  through an editor builder that turns it into generated assets plus an entry in
  `Assets/Art/Resources/ArtLibrary.asset`. Every consumer falls back to the procedural
  version when an entry is missing, so a bare checkout still runs.
- **Licensing - the repo is public.** Prefer CC0, then CC-BY with a line in CREDITS.md.
  No Unity Asset Store content. Mixamo clips may ship inside the game but not as files in the
  repo: `Assets/Art/Characters/Mixamo/` is gitignored (the owner downloads them; see README).
- **Commits** are authored by the owner alone - no `Co-Authored-By` trailer. Use the
  summary they give; if none, follow their pattern `New updates DD/MM` (append ` (2)`, ` (3)`
  for more that day). Never commit build folders (`CombatPrep1/`, `cb4/`, `cbp3/`) or
  `Assets/_Recovery/`.
- **Downloads** need the owner's OK first (name, source, size). Sites that need a login
  (Sketchfab, Mixamo) are the owner's to download from.

## Where things are

| Area | Files |
|---|---|
| Startup, lighting, post-processing | `Scripts/Core/Bootstrap.cs` |
| Rendering | `Settings/PC_RPAsset` + `PC_Renderer` (TAA, FSR, 64-bit HDR, shadows, SSAO), `Core/GraphicsSetup` (render scale, texture filtering), `Shaders/VolumetricLight` + `FX/VolumetricLight` (light in the air), `Core/EnvironmentCapture` (reflections) |
| Arena (seeded, deterministic) | `Scripts/Core/RangeBuilder*.cs` - `.Warzone` (ruins, craters, puddles, fires), `.Online` (extra cover shown only online), `.Ground` (painted terrain), `.Reflections` (probes) |
| First-person rig | `Scripts/Core/PlayerRigBuilder.cs`, `Scripts/Player/*` - `FirstPersonArms` (the soldier's forearms, reaching for the gun) |
| Guns | `Scripts/Weapons/*` - `WeaponLibrary` (the five guns), `WeaponLoadout` (all five carried, keys 1-5), `WeaponModelBuilder` (primitive models, optics), `Weapon` (firing), `WeaponAnimator` (ADS pose, visual kick, draw) |
| Sights | `Assets/Shaders/Resources/Reticle.shader` - screen-anchored reticle on the sight glass |
| Grenades | `Scripts/Weapons/Grenade.cs`, `GrenadeThrower.cs` |
| Effects | `Scripts/FX/FxRecipes.cs` (how each effect is built), `FxSystem`, `FlashFx`, `TracerFx`, `PuddleFx`, `FireFx`, `Weather` (rain), `Storm` (sky, lightning, thunder) |
| Audio | `Scripts/Audio/GameAudio.cs` (voice pool, per-gun shot sets), `Synth.cs` (generated sounds) |
| Online | `Scripts/Net/*` - `NetPlayer` (combat RPCs), `AvatarBuilder` + `SoldierView` (remote bodies), `MatchManager`, `SessionService` |
| HUD, menus | `Scripts/UI/*` |
| Editor builders | `Assets/Editor/ArtBuilder.cs`, `SoldierBuilder.cs`, `FxPrefabBuilder.cs`, `RenderingSetup.cs`, `SceneSetup.cs`, `NetworkSetup.cs` |

## Pipelines (editor)

- **ArtBuilder** rebuilds `ArtLibrary` by itself whenever a source file under `Assets/Art`
  is added, changed or removed, after script reloads, and when an FX prefab is missing. It
  fingerprints sources by name and size; bump its `Version` constant to force every machine
  to rebuild after changing builder code. Steps: Poly Haven surfaces and props (ARM repacked
  into a wet mask), sky, soldier, gunshots, FX prefabs, the ground's terrain material. It takes
  the largest resolution present (`_8k` > `_4k` > `_2k` > `_1k`), imports up to 4096 and makes
  masks up to 2048, so a sharper download only needs dropping in beside the old one.
- **SoldierBuilder** reads `Art/Characters/Soldier/russian_soldier.glb` (Unity can't import
  glTF, so it parses it) into a mesh, URP materials and `Art/Generated/Soldier/Soldier.prefab`.
  Mixamo FBXs are imported as Humanoid clips, sorted by file name (idle / walk / run / sprint
  / crouch / fire / death / throw, with directions) into
  `Art/Characters/Mixamo/Generated/SoldierAnimator.controller`. Grenade throws go on their own
  full-body layer; which is standing, crouched or running is read from the clips (a "run" in
  the name, then the lowest hips), and each records the moment the grenade leaves the hand.
  It logs the throw sorting and a self-test pose (hips, head and hand positions) to the
  Console.
- **FxPrefabBuilder** creates the effect prefabs in `Assets/Prefabs/FX` from `FxRecipes`
  **only where missing**, saving recipe textures as PNGs and materials as assets. After that
  the prefabs are the source of truth and hand edits stick. `CombatPrep > Rebuild FX Prefabs`
  resets them; deleting one regenerates it.
- **RenderingSetup / Build Range Scene**: code-made materials leave shaders unreferenced, so
  the scene carries keep-alive materials; anything referenced from `ArtLibrary` (Resources)
  or placed in a Resources folder ships automatically. On every script load it also makes sure
  the PC renderer has the `VolumetricLight` full-screen pass (before transparents, reads depth).

## Online model

- Owner-authoritative movement (NetworkTransform); look pitch and crouch are owner-written
  NetworkVariables.
- "Favour the shooter": the shooter's client traces bullets; `ShotRpc` sends which gun fired,
  pellet end points and hits; the server validates (ownership, fire-rate bucket, range) and
  computes the damage itself from that gun.
- The gun in hand is the owner-written `WeaponId`; remote bodies build each gun the first time
  it's shown and keep it. Shots name their gun too, since the variable can arrive after them.
- Grenades are server-authoritative: the owner shows its grenade at once and sends
  `ThrowGrenadeRpc`; the server validates (count per life, cooldown, origin, speed), flies an
  unseen Authority copy, and at the fuse damages every player with a clear line from the blast
  to their feet, chest or head (position-based, so it hurts the host and the thrower too).
  `GrenadeExplodedRpc` shows the explosion everywhere; `BlastHitRpc` gives the thrower a hit
  marker.
- Remote bodies: the rigged soldier when `ArtLibrary.Soldier` has a controller, otherwise
  the primitive soldier. The soldier's Humanoid avatar is built at runtime
  (`Core/HumanoidRig`), because an avatar saved from an editor script dropped out of player
  builds. Hitboxes ride the bones; the gun is placed between the hands each frame. No names
  over heads - they gave players away from across the map.

## Checking work

- Unity only recompiles when its window has focus. Compile outside Unity with its bundled
  Roslyn: take `Library/Bee/artifacts/1900b0aE.dag/Assembly-CSharp.rsp`, drop its `-out:` and
  source-file lines, add a new `-out:` plus every `Assets/Scripts/**/*.cs`, and run
  `"<Unity>/Editor/Data/NetCoreRuntime/dotnet.exe" exec "<Unity>/Editor/Data/DotNetSdkRoslyn/csc.dll" -noconfig @file.rsp`
  (with `MSYS_NO_PATHCONV=1` in Git Bash). Do the same for `Assembly-CSharp-Editor.rsp`
  against the freshly built game DLL. Pass `csc.dll` as a Windows path (`cygpath -w`): given
  `/c/...` dotnet says "the application to execute does not exist", a grep for `error` finds
  nothing, and a broken build reads as clean - check the output DLL's timestamp moved.
- Editor log: `%LOCALAPPDATA%\Unity\Editor\Editor.log` (builder messages start with
  `CombatPrep`). Built game: `%USERPROFILE%\AppData\LocalLow\DefaultCompany\CombatPrep1\Player.log`.
- Seeing the game without the editor: copy the project with its Library to a short path
  outside the repo (Library paths break Windows' 260-character limit under deeper folders),
  add a batch-mode `-executeMethod` script that enters play mode, and render cameras to PNG
  with `RenderPipeline.SubmitRenderRequest` (a few frames each, for TAA). The owner's editor
  holds the project lock, hence the copy. Realtime reflection probes never render that way.
  For what only breaks in builds, build the player from the copy (`BuildPipeline.BuildPlayer`
  in batch mode) and run the exe with `-batchmode`: it still renders, and a
  `[RuntimeInitializeOnLoadMethod]` hook reading the command line can take the same shots.

## Things that bite

- `RangeBuilder` runs inside a fixed `Random` seed shared by every machine: any new
  `Random` draw during the build shifts every later prop online. Keep draws in order, use
  `ParticleKit.Hash01/Seed` for variety, and light fires after the seeded pass.
- MonoBehaviours that live on prefabs need their own file named after the class.
- `Tex` textures stay readable (`Apply(true)`), which the FX builder relies on to save PNGs.
- The first-person rig holds all five guns but only the one in hand is active. Anything that
  follows the gun in hand (HUD, zoom, aim sensitivity) belongs in `Weapon.Draw`, not `Init`.
- `Mathf.SmoothStep(a, b, t)` blends from a to b - it is not shader `smoothstep(edge0, edge1, x)`.
  Using it as one left every puddle, smoke puff, scorch and bullet hole a half-transparent
  square. Soft edges use `TexFx.Edge` / `RangeBuilder.Smooth`.
- URP re-validates a material when it's saved as an asset: transparent Lit with "preserve
  specular" becomes premultiplied, and its reflection then covers the whole quad, shape or
  not. Decals set `_BlendModePreserveSpecular` to 0.
- The environment reflection Unity bakes from the storm sky comes out a clear blue day, and
  realtime probes don't render in batch mode, so reflections are captured from the scene
  itself (`EnvironmentCapture`) into custom probes and the default reflection.
- URP's STP upscaler posterised the dark sky and fog into bands; it's TAA plus FSR instead.
- `Prim.Box` builds a bevelled mesh of the given size at unit scale - resize a box by
  rebuilding it, never by scaling its transform.
- The editor compiles any shader variant on demand; a build keeps only the variants its
  materials and scenes ask for. Whatever needs one nobody asks for works in the editor and
  breaks in the build: the ground terrain, made at runtime, drew nothing at all in a build
  while instanced (its instancing variants were stripped), so it's drawn plain. Check look
  changes in a build too.
- A bare `new RenderTextureDescriptor { ... }` defaults `shadowSamplingMode` to
  `CompareDepths`: give it a depth buffer and a camera renders into it pure black. Set
  `ShadowSamplingMode.None` (as `EnvironmentCapture` does).
- Pushing needs the owner's GitHub sign-in (Git Credential Manager); if a push fails, hand
  them `git push origin main`.

## Recent changes (September 2026)

- **War-zone look:** Poly Haven surfaces and props, HDRI storm sky with lightning, thunder,
  rain that stops at roofs, fires and burning wrecks, ruins, craters, wire, puddles; brightened
  twice since.
- **Sights:** red dot and scope reticle drawn in screen space on the sight glass - 1 px lines,
  locked to the point of impact through recoil.
- **Soldier:** "Russian Soldier" (doctortex, CC BY 4.0) as the remote player, animated with
  Mixamo rifle locomotion, crouch, firing and death; spine follows aim pitch; per-slot camo
  tint and armband.
- **Gunshots:** real recordings (Free Firearm Sound Library, CC0), three takes per gun, over a
  synthesised echo tail.
- **Effects as prefabs:** muzzle flash (star, flame, sparks, smoke, light), travelling tracer,
  surface-coloured impacts with sparks, cracked bullet holes, explosion (fireball, smoke,
  sparks, debris, scorch), rain, fire, burning wreck, rippling puddles.
- **Hit feedback:** hit markers only - white for body hits, red for headshots, bigger and
  longer on a kill. Damage numbers removed.
- **Grenades online:** now server-authoritative and damaging (they did nothing before).
- **Throw animations:** remote soldiers throw grenades standing, crouched or on the run
  (Mixamo), starting just before the release so the grenade leaves on cue; the gun is put away
  until the arm comes back down.
- **Weapon switching:** every player carries all five guns; keys 1-5 switch (the menu pick is
  the starting gun). Each gun keeps its own ammo, a switch drops a reload in progress, and the
  new gun has a short draw before it can fire. A key with a grenade out puts it back. The HUD
  lists the guns against their keys for a moment on spawn and on each switch.
- **No nameplates:** names no longer float over other players' heads.
- **Ruins inside the arena:** four walk-in shelled buildings in mirrored pairs - the flank pair
  ablaze under smoke columns, the far-corner pair smouldering - with breaches, windows and a
  surviving upper floor, solid to players, bullets and blasts. Their ground is claimed before
  the clutter is scattered. The skyline ring moved out to 100-160 m and no longer burns.
- **Graphics pass:** temporal AA (FSR upscaling above 1660 lines), 64-bit HDR with HDR
  grading, shadows to 120 m on a 4096 map, stronger SSAO, 16x texture filtering; volumetric
  light (storm-light shafts, glow round fires); reflections captured from the scene; fire
  lights that cast shadows; screen-space lens flares. Every box bevelled; sandbags as sacks;
  ruins with broken tops and rebar; the ground a terrain painted mud / burnt earth / grit.
  Puddles, smoke, scorches and bullet holes fixed (they were half-transparent squares); decal
  reflections fixed; berms no longer frosty. The storm light now comes from behind the firing
  line, a little brighter overall. First-person forearms hold the gun; a fill light on the
  viewmodel keeps it from reading as a black cut-out.
