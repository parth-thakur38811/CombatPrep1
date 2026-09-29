using System.Collections.Generic;
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;
using CombatPrep.Net;

namespace CombatPrep.EditorTools
{
    /// <summary>
    /// Generates everything Netcode needs to identify networked objects across machines,
    /// entirely from code - the one place the project emits prefab assets.
    ///
    /// Netcode spawns objects by a hash baked into a prefab at edit time, so a purely
    /// runtime-built object can't be networked. These prefabs therefore exist, but hold
    /// only script components: no meshes, materials, textures or sounds. All visuals are
    /// still built in code when each object spawns. Regenerating is safe - prefabs are
    /// saved over the same paths, so their GUIDs and network hashes stay stable.
    /// </summary>
    public static class NetworkSetup
    {
        const string Dir = "Assets/Network/Generated";
        const string PlayerPath = Dir + "/NetPlayer.prefab";
        const string MatchPath = Dir + "/NetMatch.prefab";
        const string ListPath = Dir + "/NetworkPrefabs.asset";

        [MenuItem("CombatPrep/Rebuild Network Prefabs")]
        public static void RebuildMenu()
        {
            BuildPrefabs(out _, out _, out _);
            Debug.Log("<b>CombatPrep</b>: network prefabs regenerated in " + Dir +
                      ". Run Build Range Scene to rewire the scene's NetworkManager.");
        }

        public static void BuildPrefabs(out GameObject player, out GameObject match, out NetworkPrefabsList list)
        {
            Directory.CreateDirectory(Dir);

            player = SavePrefab(PlayerPath, "NetPlayer", go =>
            {
                go.AddComponent<NetworkObject>();

                // Owner authority: each player moves themselves with no input lag. Only
                // position and yaw are synced - pitch goes through NetPlayer, and scale never
                // changes.
                var nt = go.AddComponent<NetworkTransform>();
                nt.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
                nt.SyncRotAngleX = false;
                nt.SyncRotAngleZ = false;
                nt.SyncScaleX = nt.SyncScaleY = nt.SyncScaleZ = false;
                nt.Interpolate = true;

                go.AddComponent<NetPlayer>();
            });

            match = SavePrefab(MatchPath, "NetMatch", go =>
            {
                go.AddComponent<NetworkObject>();
                go.AddComponent<MatchManager>();
            });

            list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(ListPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, ListPath);
            }

            foreach (var prefab in new[] { player, match })
                if (!list.Contains(prefab))
                    list.Add(new NetworkPrefab { Prefab = prefab });

            EditorUtility.SetDirty(list);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// Adds a configured NetworkManager to the currently open scene. Scene management is
        /// off on purpose: every machine builds the identical arena locally from a fixed seed,
        /// so Netcode only has to handle objects spawned at runtime.
        /// </summary>
        public static GameObject CreateNetworkManager(GameObject player, GameObject match, NetworkPrefabsList list)
        {
            var go = new GameObject("Network");
            var nm = go.AddComponent<NetworkManager>();
            var transport = go.AddComponent<UnityTransport>();

            nm.NetworkConfig ??= new NetworkConfig();
            nm.NetworkConfig.NetworkTransport = transport;
            nm.NetworkConfig.PlayerPrefab = player;
            nm.NetworkConfig.Prefabs.NetworkPrefabsLists = new List<NetworkPrefabsList> { list };
            nm.NetworkConfig.EnableSceneManagement = false;
            nm.NetworkConfig.ConnectionApproval = true;

            var refs = go.AddComponent<NetworkPrefabRefs>();
            refs.Player = player;
            refs.Match = match;

            return go;
        }

        static GameObject SavePrefab(string path, string name, System.Action<GameObject> build)
        {
            var go = new GameObject(name);
            try
            {
                build(go);
                return PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
