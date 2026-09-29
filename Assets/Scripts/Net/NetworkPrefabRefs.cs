using UnityEngine;

namespace CombatPrep.Net
{
    /// <summary>
    /// Serialized references to the generated network prefabs, sitting next to the
    /// NetworkManager in the scene. Written by the editor generator (NetworkSetup), so the
    /// runtime never has to search the project or use Resources to find them.
    /// </summary>
    public class NetworkPrefabRefs : MonoBehaviour
    {
        public static NetworkPrefabRefs I { get; private set; }

        public GameObject Player;
        public GameObject Match;

        void Awake() => I = this;
    }
}
