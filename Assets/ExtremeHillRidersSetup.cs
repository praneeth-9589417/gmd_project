#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif

/// <summary>
/// =========================================================================================
/// EXTREME HILL RIDERS - One-Click Scene & Project Builder for Unity 6
/// =========================================================================================
/// Menu Item: Tools > Extreme Hill Riders > Build Game
///
/// Features:
/// - Generates clean, pristine ExtremeHillRiders scene
/// - Zero missing scripts, zero pink shaders (universal MaterialHelper)
/// - Modern Unity 6 New Input System integration (InputSystemUIInputModule)
/// =========================================================================================
/// </summary>
public static class ExtremeHillRidersSetup
{
    private const string SCENE_DIR = "Assets/Scenes";
    private const string SCENE_PATH = "Assets/Scenes/ExtremeHillRiders.unity";

    [MenuItem("Tools/Extreme Hill Riders/Build Game", false, 1)]
    public static void BuildCompleteGameScene()
    {
        bool proceed = EditorUtility.DisplayDialog(
            "Extreme Hill Riders - Build Game",
            "This will create a new, fully configured Extreme Hill Riders game scene at:\n" +
            SCENE_PATH + "\n\nDo you want to proceed?",
            "Build Now",
            "Cancel"
        );

        if (!proceed) return;

        // 1. Create brand new clean scene
        UnityEngine.SceneManagement.Scene newScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // 2. Setup Directional Light
        GameObject lightGo = new GameObject("Directional Light");
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1.0f, 0.98f, 0.88f);
        light.intensity = 1.35f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
        lightGo.transform.position = new Vector3(0f, 15f, -10f);

        // 3. Setup Main Camera
        GameObject camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        Camera cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.35f, 0.68f, 0.98f);
        cam.fieldOfView = 55f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 300f;
        camGo.transform.position = new Vector3(3.5f, 5.5f, -13.5f);
        camGo.transform.rotation = Quaternion.Euler(10f, 0f, 0f);
        camGo.AddComponent<AudioListener>();

        // 4. Setup Game Manager Root
        GameObject managerGo = new GameObject("ExtremeHillRiders_Manager");
        ExtremeHillRiders manager = managerGo.AddComponent<ExtremeHillRiders>();

        SoundFX soundFX = managerGo.AddComponent<SoundFX>();
        TerrainGenerator terrainGen = managerGo.AddComponent<TerrainGenerator>();

        GameObject vehicleContainer = new GameObject("Vehicles");
        vehicleContainer.transform.SetParent(managerGo.transform);

        GameObject terrainContainer = new GameObject("Terrain");
        terrainContainer.transform.SetParent(managerGo.transform);

        GameObject propsContainer = new GameObject("Props");
        propsContainer.transform.SetParent(managerGo.transform);

        manager.mainCamera = cam;
        manager.dirLight = light;
        manager.vehicleContainer = vehicleContainer.transform;
        manager.terrainContainer = terrainContainer.transform;
        manager.propsContainer = propsContainer.transform;

        // 5. Setup Complete Canvas & UI Hierarchy
        GameObject uiManagerGo = new GameObject("UIManager");
        UIManager uiManager = uiManagerGo.AddComponent<UIManager>();
        uiManager.transform.SetParent(managerGo.transform);
        manager.uiManager = uiManager;

        // 6. Ensure EventSystem has the modern Input Module for Unity 6
        ExtremeHillRiders.FixEventSystemInputModule();

        // 7. Ensure Scenes Folder Exists
        if (!AssetDatabase.IsValidFolder("Assets/Scenes"))
        {
            if (!AssetDatabase.IsValidFolder("Assets"))
            {
                AssetDatabase.CreateFolder("", "Assets");
            }
            AssetDatabase.CreateFolder("Assets", "Scenes");
        }

        // 8. Save Scene
        EditorSceneManager.SaveScene(newScene, SCENE_PATH);
        AssetDatabase.Refresh();

        // 9. Add to Build Settings
        AddSceneToBuildSettings(SCENE_PATH);

        // 10. Ping and Select
        Selection.activeGameObject = managerGo;

        Debug.Log("<color=#40FF40><b>[Extreme Hill Riders]</b> Game scene built and saved successfully to: " + SCENE_PATH + "</color>");

        EditorUtility.DisplayDialog(
            "Build Complete!",
            "Extreme Hill Riders has been successfully generated!\n\n" +
            "Scene Location: " + SCENE_PATH + "\n\n" +
            "Press PLAY to experience all 4 themed worlds!",
            "Let's Play!"
        );
    }

    private static void AddSceneToBuildSettings(string scenePath)
    {
        EditorBuildSettingsScene[] existingScenes = EditorBuildSettings.scenes;

        foreach (var scene in existingScenes)
        {
            if (scene.path == scenePath)
            {
                scene.enabled = true;
                return;
            }
        }

        EditorBuildSettingsScene[] newScenes = new EditorBuildSettingsScene[existingScenes.Length + 1];
        newScenes[0] = new EditorBuildSettingsScene(scenePath, true);
        for (int i = 0; i < existingScenes.Length; i++)
        {
            newScenes[i + 1] = existingScenes[i];
        }

        EditorBuildSettings.scenes = newScenes;
    }
}
#endif
