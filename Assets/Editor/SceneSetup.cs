using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CombatPrep.Core;

namespace CombatPrep.EditorTools
{
    /// <summary>
    /// One-click scene setup. The game builds itself at runtime from Bootstrap, so the
    /// scene only ever needs to contain a single empty GameObject - this just creates that
    /// scene, strips the template's default camera and light (which would otherwise fight
    /// the ones Bootstrap makes), and saves it.
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
            go.AddComponent<Bootstrap>();

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, ScenePath);

            AddSceneToBuildSettings();

            Selection.activeGameObject = go;
            Debug.Log("<b>CombatPrep</b>: Range scene built at " + ScenePath + ". Press Play.");
        }

        static void AddSceneToBuildSettings()
        {
            var existing = EditorBuildSettings.scenes;
            foreach (var s in existing)
                if (s.path == ScenePath) return;

            var list = new EditorBuildSettingsScene[existing.Length + 1];
            existing.CopyTo(list, 0);
            list[existing.Length] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = list;
        }
    }
}
