using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using CombatPrep.Audio;
using CombatPrep.Core;
using CombatPrep.FX;
using CombatPrep.Skins;
using CombatPrep.Targets;
using CombatPrep.UI;
using CombatPrep.Weapons;

namespace CombatPrep.Net
{
    /// <summary>
    /// One networked player. Netcode spawns this for every connected client from a
    /// generated, component-only prefab; everything visible is built in code here.
    ///
    /// The same object is built two different ways depending on who is looking at it:
    ///   - on its owner's machine it becomes the full first-person rig (camera, controller,
    ///     all five guns, grenades) via the same PlayerRigBuilder Practice mode uses;
    ///   - on everyone else's machine it becomes a third-person avatar with no input at all.
    ///
    /// Combat follows "favour the shooter": your client decides what your bullets hit, but
    /// only the server changes health. Each trigger pull becomes one ShotRpc carrying which
    /// gun fired, the pellet end points (so everyone else sees and hears the shot) and any
    /// player hits (which the server validates and turns into damage using that gun's numbers).
    /// The gun in hand is also the WeaponId variable, so other players see the switch.
    ///
    /// Grenades are the server's alone: a throw is sent to it, it flies an unseen copy of
    /// the grenade and, when the fuse runs out, damages every player the blast can see -
    /// judged from their positions, so it can hurt the host and the thrower too. Everyone's
    /// screen shows its own copy in flight and the explosion where the server's went off.
    /// </summary>
    public class NetPlayer : NetworkBehaviour
    {
        public const float MaxHealth = 100f;
        public const float RespawnSeconds = 3f;

        public static readonly List<NetPlayer> All = new();
        public static NetPlayer Local { get; private set; }

        /// <summary>Raised on the owning machine once its first-person rig exists.</summary>
        public static event Action<PlayerRig> LocalRigBuilt;

        // Chosen in the menus before connecting; the owner publishes them on spawn.
        public static string PendingName = "Player";
        public static int PendingWeapon, PendingSkin;

        // --- owner-written identity and aim ---
        public readonly NetworkVariable<FixedString32Bytes> DisplayName = new(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>The gun in hand (WeaponLibrary index) - the menu's pick at first, then keys 1-5.</summary>
        public readonly NetworkVariable<int> WeaponId = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public readonly NetworkVariable<int> SkinId = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Look pitch in tenths of a degree - precise enough to aim by, cheap to send.</summary>
        public readonly NetworkVariable<short> PitchDeci = new(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Owner's stance, so other players see (and can only hit) a crouched body.</summary>
        public readonly NetworkVariable<bool> Crouched = new(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        // --- server-written combat state ---
        public readonly NetworkVariable<float> Health = new(MaxHealth);
        public readonly NetworkVariable<int> Kills = new();
        public readonly NetworkVariable<int> Deaths = new();

        PlayerRig _rig;
        bool _hasRig;
        AvatarView _avatar;
        bool _built;

        // Hits collected during one trigger pull (owner only).
        readonly List<ulong> _hitVictims = new();
        readonly List<byte> _hitZones = new();

        // Server-side fire-rate bucket for this shooter.
        float _shotBudget = ShotBurstAllowance;
        float _budgetStamp;
        const float ShotBurstAllowance = 4f;

        // Grenades: the owner numbers its throws; the server keeps its own count per life.
        const int GrenadesPerLife = 4;
        int _grenadeSeq;
        int _grenadesLeft = GrenadesPerLife;
        float _nextGrenadeAt;

        /// <summary>Heights checked for a blast to reach, as fractions of standing height.</summary>
        static readonly float[] BlastHeights = { 0.25f, 0.95f, 1.55f };

        public string PlayerName => DisplayName.Value.ToString();
        public PlayerRig Rig => _rig;
        public bool IsAlive => Health.Value > 0f;

        public WeaponEntry Weapon =>
            WeaponLibrary.All[Mathf.Clamp(WeaponId.Value, 0, WeaponLibrary.All.Length - 1)];

        public SkinDefinition Skin =>
            SkinLibrary.All[Mathf.Clamp(SkinId.Value, 0, SkinLibrary.All.Length - 1)];

        // ----------------------------------------------------------------- lifecycle

        public override void OnNetworkSpawn()
        {
            All.Add(this);
            gameObject.name = $"NetPlayer_{OwnerClientId}";
            WeaponId.OnValueChanged += OnWeaponChanged;
            Health.OnValueChanged += OnHealthChanged;

            if (IsOwner)
            {
                Local = this;
                DisplayName.Value = new FixedString32Bytes(Clip(PendingName, 16));
                WeaponId.Value = PendingWeapon;
                SkinId.Value = PendingSkin;
            }
        }

        public override void OnNetworkDespawn()
        {
            WeaponId.OnValueChanged -= OnWeaponChanged;
            Health.OnValueChanged -= OnHealthChanged;
            if (_hasRig && _rig.Loadout != null)
            {
                _rig.Loadout.Switched -= OnWeaponSwitched;
                foreach (var w in _rig.Loadout.Weapons)
                {
                    w.ShotStarting -= OnShotStarting;
                    w.ShotFired -= OnShotFired;
                }
            }
            if (_hasRig && _rig.Thrower != null) _rig.Thrower.Thrown -= OnGrenadeThrown;
            if (RemoteHitProxy.Collector == this) RemoteHitProxy.Collector = null;

            All.Remove(this);
            if (Local == this) Local = null;
        }

        /// <summary>Everyone else: the gun in this player's hands changes to match.</summary>
        void OnWeaponChanged(int _, int now)
        {
            if (_avatar != null) _avatar.ShowWeapon(now);
        }

        void OnHealthChanged(float before, float now)
        {
            if (!IsOwner || !_hasRig) return;
            Hud.I.SetHealth(now);
            if (now < before && now > 0f)
            {
                Hud.I.DamageFlash();
                GameAudio.I.Play(GameAudio.I.ImpactSoft, 0.8f, 0.1f);
            }
        }

        void Update()
        {
            if (!IsSpawned) return;

            // Bodies appear when the match begins; in the lobby everyone is just a name.
            if (!_built && MatchManager.I != null && MatchManager.I.IsPlaying)
                BuildBody();

            if (!_built) return;

            if (IsOwner && _hasRig)
            {
                PublishPitch();
                PublishStance();
            }
            else if (_avatar != null)
            {
                _avatar.SetPitch(PitchDeci.Value / 10f);
                _avatar.SetCrouch(Crouched.Value);
            }
        }

        /// <summary>Like pitch, only sent when it changes - a key press, not a stream.</summary>
        void PublishStance()
        {
            bool crouched = _rig.Motor != null && _rig.Motor.IsCrouching;
            if (crouched != Crouched.Value) Crouched.Value = crouched;
        }

        // ------------------------------------------------------------------- building

        void BuildBody()
        {
            _built = true;
            if (IsOwner) BuildLocal();
            else BuildRemote();
        }

        void BuildLocal()
        {
            // Move to our spawn point *before* the CharacterController exists, since it would
            // otherwise override the move. Teleporting through the NetworkTransform makes other
            // players see us appear there, rather than interpolate in from the origin.
            var (pos, rot) = RangeBuilder.SpawnPoint((int)OwnerClientId);
            GetComponent<NetworkTransform>().Teleport(pos, rot, transform.localScale);

            _rig = PlayerRigBuilder.Build(gameObject, Weapon, Skin);
            _hasRig = true;

            foreach (var w in _rig.Loadout.Weapons)
            {
                w.ShotStarting += OnShotStarting;
                w.ShotFired += OnShotFired;
            }
            _rig.Loadout.Switched += OnWeaponSwitched;

            _rig.Thrower.Networked = true;
            _rig.Thrower.Thrown += OnGrenadeThrown;

            Hud.I.SetHealthVisible(true);
            Hud.I.SetHealth(Health.Value);

            LocalRigBuilt?.Invoke(_rig);
        }

        void BuildRemote()
        {
            var proxy = gameObject.AddComponent<RemoteHitProxy>();
            proxy.Player = this;
            _avatar = AvatarBuilder.Build(transform, proxy, (int)OwnerClientId, WeaponId.Value, Skin);
            _avatar.SetVisible(IsAlive);
        }

        void PublishPitch()
        {
            if (_rig.Cam == null) return;

            float p = _rig.Cam.transform.localEulerAngles.x;
            if (p > 180f) p -= 360f;

            // The variable only transmits when the quantized value actually changes.
            short q = (short)Mathf.RoundToInt(p * 10f);
            if (q != PitchDeci.Value) PitchDeci.Value = q;
        }

        // ------------------------------------------------------------ shooting (owner)

        void OnShotStarting()
        {
            _hitVictims.Clear();
            _hitZones.Clear();
            RemoteHitProxy.Collector = this;
        }

        /// <summary>Called by a RemoteHitProxy while one of our bullets is being traced.</summary>
        public void QueueHit(NetPlayer victim, Zone zone, float distance)
        {
            _hitVictims.Add(victim.NetworkObjectId);
            _hitZones.Add((byte)zone);
        }

        void OnShotFired(Vector3 muzzle, Vector3[] ends)
        {
            RemoteHitProxy.Collector = null;
            // The shot names its gun rather than leaving it to WeaponId, which travels
            // separately and could arrive after a shot fired just after a switch.
            ShotRpc((byte)_rig.Loadout.Index, ends, _hitVictims.ToArray(), _hitZones.ToArray());
        }

        /// <summary>Owner: a different gun is in hand - everyone else's copy of us holds it too.</summary>
        void OnWeaponSwitched(int index) => WeaponId.Value = index;

        // ----------------------------------------------------------- shooting (server)

        [Rpc(SendTo.Server)]
        void ShotRpc(byte weapon, Vector3[] ends, ulong[] victims, byte[] zones, RpcParams rpcParams = default)
        {
            // Any client may invoke an RPC on any object, so check the sender really owns
            // this gun - otherwise one player could fire "as" another.
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (!IsAlive || MatchManager.I == null || !MatchManager.I.IsPlaying) return;
            if (weapon >= WeaponLibrary.All.Length) return;

            // Every player carries every gun, so any of them may fire; its own rate and
            // damage apply.
            var def = WeaponLibrary.All[weapon].Def;
            if (!SpendShot(def)) return;

            // Everyone but the shooter sees and hears it.
            ShotVisualRpc(weapon, ends);

            int n = Mathf.Min(Mathf.Min(victims.Length, zones.Length), Mathf.Max(1, def.PelletsPerShot));
            for (int i = 0; i < n; i++)
            {
                if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(victims[i], out var obj)) continue;
                if (!obj.TryGetComponent(out NetPlayer victim)) continue;
                if (victim == this || !victim.IsAlive) continue;

                // Positions are the players' own (owner-authoritative), so distance is a
                // sanity bound rather than proof - but it does cap range and damage falloff.
                float dist = Vector3.Distance(transform.position, victim.transform.position);
                if (dist > def.MaxRange + 8f) continue;

                var zone = zones[i] <= (byte)Zone.Limb ? (Zone)zones[i] : Zone.Body;
                victim.TakeDamageServer(ServerDamage(def, zone, dist), this);
            }
        }

        /// <summary>
        /// Leaky bucket: sustained fire can't beat the weapon's rate, but a few shots arriving
        /// bunched together (normal network jitter) are still accepted.
        /// </summary>
        bool SpendShot(WeaponDefinition def)
        {
            float now = Time.time;
            _shotBudget = Mathf.Min(ShotBurstAllowance, _shotBudget + (now - _budgetStamp) / def.ShotInterval);
            _budgetStamp = now;
            if (_shotBudget < 1f) return false;
            _shotBudget -= 1f;
            return true;
        }

        /// <summary>The server's own damage figure - the shooter's client never supplies one.</summary>
        static float ServerDamage(WeaponDefinition def, Zone zone, float distance)
        {
            float falloff = Mathf.InverseLerp(def.DamageFalloffStart, def.DamageFalloffEnd, distance);
            float dmg = def.Damage * Mathf.Lerp(1f, def.MinDamageFraction, falloff);
            return zone switch
            {
                Zone.Head => dmg * def.HeadshotMultiplier,
                Zone.Limb => dmg * def.LimbMultiplier,
                _ => dmg
            };
        }

        public void TakeDamageServer(float damage, NetPlayer attacker)
        {
            if (!IsServer || !IsAlive || damage <= 0f) return;

            Health.Value = Mathf.Max(0f, Health.Value - damage);
            if (Health.Value > 0f) return;

            Deaths.Value++;
            if (attacker != null && attacker != this) attacker.Kills.Value++;

            DiedRpc(attacker != null ? attacker.NetworkObjectId : NetworkObjectId);
            StartCoroutine(RespawnAfterDelay());
        }

        IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(RespawnSeconds);
            if (!IsSpawned) yield break;

            int spawn = MatchManager.I != null ? MatchManager.I.ChooseSpawn(this) : (int)OwnerClientId;
            Health.Value = MaxHealth;
            _grenadesLeft = GrenadesPerLife;      // the thrower refills to match
            RespawnRpc(spawn);
        }

        // ----------------------------------------------------------------- grenades

        /// <summary>Owner: show our grenade at once, and ask the server to throw the real one.</summary>
        void OnGrenadeThrown(Vector3 origin, Vector3 velocity)
        {
            int id = ++_grenadeSeq;
            Grenade.SpawnVisual(NetworkObjectId, id, origin, velocity);
            ThrowGrenadeRpc(id, origin, velocity);
        }

        [Rpc(SendTo.Server)]
        void ThrowGrenadeRpc(int id, Vector3 origin, Vector3 velocity, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (!IsAlive || MatchManager.I == null || !MatchManager.I.IsPlaying) return;

            // The throw has to be one this player could make: grenades left, not faster than
            // the throw cooldown, from roughly where they stand, at no more than arm's speed.
            if (_grenadesLeft <= 0 || Time.time < _nextGrenadeAt) return;
            if ((origin - (transform.position + Vector3.up * 1.3f)).sqrMagnitude > 3f * 3f) return;
            if (velocity.sqrMagnitude > 22f * 22f) return;
            _grenadesLeft--;
            _nextGrenadeAt = Time.time + 0.4f;

            GrenadeFlightRpc(id, origin, velocity);
            var real = Grenade.Spawn(origin, velocity, GrenadeRole.Authority, 0);
            real.Detonated = centre => GrenadeBlastServer(id, centre);
        }

        /// <summary>Everyone else: the thrower's arm going over, and the grenade flying.</summary>
        [Rpc(SendTo.NotOwner)]
        void GrenadeFlightRpc(int id, Vector3 origin, Vector3 velocity)
        {
            if (_avatar != null) _avatar.OnThrow();
            Grenade.SpawnVisual(NetworkObjectId, id, origin, velocity);
        }

        /// <summary>
        /// Server, when the real grenade's fuse runs out: an explosion on every screen, then
        /// damage to each player the blast can reach. A player counts as reached if the blast
        /// has a clear line to their feet, chest or head, and the nearest such point sets the
        /// damage band. Walls and cover block it; other players don't.
        /// </summary>
        void GrenadeBlastServer(int id, Vector3 centre)
        {
            // The thrower may have left while it was in the air.
            if (this == null || !IsSpawned || MatchManager.I == null || !MatchManager.I.IsPlaying) return;
            GrenadeExplodedRpc(id, centre);

            // Lifted off the ground a little, so the lines out don't graze the dirt.
            Vector3 origin = centre + Vector3.up * 0.2f;
            bool hitSomeone = false, killed = false;

            foreach (var player in All.ToArray())
            {
                if (player == null || !player.IsAlive) continue;

                float crouch = player.Crouched.Value ? 0.62f : 1f;
                float nearest = float.MaxValue;
                foreach (float h in BlastHeights)
                {
                    Vector3 point = player.transform.position + Vector3.up * (1.8f * h * crouch);
                    float d = Vector3.Distance(origin, point);
                    if (d >= nearest || d > Grenade.DefaultOuter) continue;
                    if (Physics.Linecast(origin, point, Weather.WorldMask, QueryTriggerInteraction.Ignore)) continue;
                    nearest = d;
                }
                if (nearest == float.MaxValue) continue;

                float damage = Grenade.DefaultDamage * Grenade.DefaultFalloff(nearest);
                if (damage <= 0f) continue;

                player.TakeDamageServer(damage, this);
                if (player != this)
                {
                    hitSomeone = true;
                    killed |= !player.IsAlive;
                }
            }

            if (hitSomeone) BlastHitRpc(killed);
        }

        /// <summary>Everyone: the explosion, where the server's grenade actually went off.</summary>
        [Rpc(SendTo.Everyone)]
        void GrenadeExplodedRpc(int id, Vector3 centre)
        {
            Grenade.RemoveVisual(NetworkObjectId, id);
            FxSystem.I.Explosion(centre, Grenade.DefaultOuter);
            GameAudio.I.PlayAt(GameAudio.I.Explosion, centre, 1f, 0.05f, maxDistance: 220f);
        }

        /// <summary>The thrower: a hit marker for a blast that caught someone.</summary>
        [Rpc(SendTo.Owner)]
        void BlastHitRpc(bool killed)
        {
            if (!_hasRig) return;
            Hud.I.ReportHit(0f, false, killed, Vector3.zero);
            GameAudio.I.Play(killed ? GameAudio.I.KillMarker : GameAudio.I.HitMarker, 0.9f, 0.02f);
        }

        // --------------------------------------------------------- everyone's view

        [Rpc(SendTo.NotOwner)]
        void ShotVisualRpc(byte weapon, Vector3[] ends)
        {
            if (_avatar == null || weapon >= WeaponLibrary.All.Length) return;

            // The gun that fired is the one they're holding, even if the switch itself is
            // still on its way.
            _avatar.ShowWeapon(weapon);
            _avatar.OnShot();
            Vector3 muzzle = _avatar.MuzzlePosition;
            var def = WeaponLibrary.All[weapon].Def;

            // Gunshots carry across the whole arena, not the 60 m used for impact sounds.
            GameAudio.I.PlayShotAt(def, muzzle, maxDistance: 220f);

            foreach (var end in ends)
            {
                FxSystem.I.Tracer(muzzle, end);

                // A pellet that flew to full range hit nothing - no spark in thin air.
                Vector3 delta = end - muzzle;
                if (delta.sqrMagnitude < def.MaxRange * def.MaxRange * 0.9f)
                    FxSystem.I.Impact(end, -delta.normalized, new Color(0.55f, 0.52f, 0.48f),
                                      null, 2, spark: true, hole: false);
            }
        }

        [Rpc(SendTo.Everyone)]
        void DiedRpc(ulong killerObjectId)
        {
            string killer = NameOf(killerObjectId);
            Hud.I.AddKillFeed(killer, PlayerName);

            if (IsOwner && _hasRig)
            {
                _rig.Thrower.Holster();
                SetControls(false);
                Hud.I.ShowDeath(killer, RespawnSeconds);
            }
            else if (_avatar != null)
            {
                _avatar.OnDied();
            }
        }

        [Rpc(SendTo.Everyone)]
        void RespawnRpc(int spawnIndex)
        {
            if (IsOwner && _hasRig)
            {
                var (pos, rot) = RangeBuilder.SpawnPoint(spawnIndex);

                // The CharacterController keeps its own copy of the position; disable it across
                // the teleport or it snaps the player straight back to where they died.
                var cc = GetComponent<CharacterController>();
                if (cc != null) cc.enabled = false;
                GetComponent<NetworkTransform>().Teleport(pos, rot, transform.localScale);
                Physics.SyncTransforms();
                if (cc != null) cc.enabled = true;

                _rig.Look.ResetView(rot.eulerAngles.y);
                _rig.Loadout.ResetAmmo();
                _rig.Thrower.Refill();
                SetControls(true);
                Hud.I.HideDeath();
                Hud.I.SetHealth(MaxHealth);
            }
            else if (_avatar != null)
            {
                _avatar.OnRespawned();
            }
        }

        void SetControls(bool on)
        {
            _rig.Motor.enabled = on;
            _rig.Look.enabled = on;
            _rig.Loadout.SetControls(on);    // disabling also cancels any aim-down-sights
            _rig.Thrower.enabled = on;
        }

        string NameOf(ulong objectId)
        {
            if (NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(objectId, out var obj) &&
                obj.TryGetComponent(out NetPlayer p))
                return p.PlayerName;
            return "?";
        }

        /// <summary>
        /// Names travel in a 29-byte FixedString, which throws if overfilled. Keeping to
        /// printable ASCII makes the byte count equal the character count, so the 16-char
        /// cap can never overflow it - emoji or accented letters could otherwise do so.
        /// </summary>
        static string Clip(string s, int max)
        {
            var sb = new System.Text.StringBuilder(max);
            if (s != null)
                foreach (char c in s.Trim())
                {
                    if (c >= 32 && c < 127) sb.Append(c);
                    if (sb.Length == max) break;
                }
            return sb.Length > 0 ? sb.ToString() : "Player";
        }
    }
}
