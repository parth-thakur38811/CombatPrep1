using Unity.Netcode;

namespace CombatPrep.Net
{
    public enum MatchPhase : byte { Lobby, Playing }

    /// <summary>
    /// Server-owned match state, spawned once by the host after the session starts.
    /// Everyone reads the phase; only the server writes it.
    ///
    /// Phase 1 carries just Lobby -> Playing. Countdown, timer, scoring and results are
    /// layered on in Phase 4.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager I { get; private set; }

        /// <summary>Two players minimum: a deathmatch against nobody is not a match.</summary>
        public const int MinPlayersToStart = 2;

        public readonly NetworkVariable<MatchPhase> Phase = new(MatchPhase.Lobby);

        public bool IsPlaying => Phase.Value == MatchPhase.Playing;

        public override void OnNetworkSpawn() => I = this;

        public override void OnNetworkDespawn()
        {
            if (I == this) I = null;
        }

        public bool CanStart =>
            IsServer && Phase.Value == MatchPhase.Lobby
                     && NetworkManager.ConnectedClientsIds.Count >= MinPlayersToStart;

        /// <summary>Host only - the host is the server, so no RPC is needed.</summary>
        public void StartMatch()
        {
            if (!CanStart) return;
            Phase.Value = MatchPhase.Playing;
        }

        /// <summary>
        /// Server-side respawn choice: the spawn point whose nearest living opponent is
        /// furthest away, so nobody reappears in someone's sights. A little random jitter
        /// breaks ties, so a lone player doesn't always get spawn 0.
        /// </summary>
        public int ChooseSpawn(NetPlayer forPlayer)
        {
            int best = 0;
            float bestScore = float.MinValue;

            for (int i = 0; i < Core.RangeBuilder.SpawnCount; i++)
            {
                var (pos, _) = Core.RangeBuilder.SpawnPoint(i);

                float nearest = 1000f;
                foreach (var other in NetPlayer.All)
                {
                    if (other == forPlayer || !other.IsAlive) continue;
                    nearest = UnityEngine.Mathf.Min(nearest, UnityEngine.Vector3.Distance(pos, other.transform.position));
                }

                float score = nearest + UnityEngine.Random.Range(0f, 3f);
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>
        /// Server-side gate for every connection. The session service already caps the lobby
        /// at 4; this is the netcode-level backstop, and it also turns away anyone trying to
        /// join a match already in progress. Static because it runs for the host's own
        /// connection too, before any MatchManager has been spawned.
        /// </summary>
        public static void Approve(NetworkManager.ConnectionApprovalRequest request,
                                   NetworkManager.ConnectionApprovalResponse response)
        {
            var nm = NetworkManager.Singleton;
            bool full = nm.ConnectedClientsIds.Count >= SessionService.MaxPlayers;
            bool inProgress = I != null && I.IsPlaying;

            response.Approved = !full && !inProgress;
            response.CreatePlayerObject = response.Approved;
            response.Reason = full ? "The lobby is full."
                            : inProgress ? "That match is already in progress."
                            : null;
        }
    }
}
