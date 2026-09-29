using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CombatPrep.Core;

namespace CombatPrep.EditorTools
{
    /// <summary>
    /// One-click scene setup. The game builds itself at runtime from Bootstrap, so the scene
    /// holds only two objects, both generated here: Bootstrap, and a configured
    /// NetworkManager for online play (Netcode requires one to exist in the scene). Starting
    /// from an empty scene also drops the template's default camera and light, which would
    /// otherwise fight the ones Bootstrap makes.
    /// </summary>
    public static class SceneSetup
    {
        const string ScenePath = "Assets/Scenes/Range.unity";

        [MenuItem("CombatPrep/Build Range Scene %#r")]
        public static void BuildRangeScene()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var go = new GameObject("Bootstrap");
            var boot = go.AddComponent<Bootstrap>();

            // Referenced by the scene so a build keeps the shaders and variants the code uses.
            boot.ShaderKeepAlive = RenderingSetup.BuildKeepAliveMaterials();

            NetworkSetup.BuildPrefabs(out var player, out var match, out var prefabList);
            NetworkSetup.CreateNetworkManager(player, match, prefabList);

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddSceneToBuildSettings();

            Selection.activeGameObject = go;
            Debug.Log("<b>CombatPrep</b>: Range scene built at " + ScenePath + ". Press Play.");
        }

        /// <summary>
        /// Makes Range the startup scene. A build always opens scene 0, and the project
        /// template had put its empty SampleScene there - so a build showed nothing but sky.
        /// Range goes first; the template scene is dropped from the build list (the file on
        /// disk is untouched); any other scenes keep their order after it.
        /// </summary>
        static void AddSceneToBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath && !s.path.EndsWith("/SampleScene.unity"))
                    scenes.Add(s);
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
