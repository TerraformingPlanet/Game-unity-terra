#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Script éditeur temporaire — câble toutes les références de la scène Game.
/// Menu : Tools > Terraformation > Wire Scene References
/// À supprimer après avoir vérifié que tout est OK.
/// </summary>
public static class SceneSetupHelper
{
    [MenuItem("Tools/Terraformation/Wire Scene References")]
    public static void WireSceneReferences()
    {
        var scene = SceneManager.GetActiveScene();
        var allGOs = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                allGOs.Add(t.gameObject);

        GameObject Find(string n) {
            foreach (var go in allGOs) if (go.name == n) return go;
            Debug.LogError($"[SceneSetupHelper] GO introuvable : {n}");
            return null;
        }

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var managers  = Find("Managers");
        var canvas    = Find("Canvas");
        var hexGridGO = Find("HexGrid");

        // ====================================================
        // Créer SolarSystemData asset si manquant
        // ====================================================
        string solarSystemPath = "Assets/ScriptableObjects/Worlds/Kepler-442-System.asset";
        SolarSystemData solarSystem = AssetDatabase.LoadAssetAtPath<SolarSystemData>(solarSystemPath);
        if (solarSystem == null)
        {
            // Créer l'asset
            solarSystem = ScriptableObject.CreateInstance<SolarSystemData>();
            solarSystem.systemName = "Kepler-442";
            solarSystem.distanceLightYears = 1115f;

            // Créer l'étoile principale comme StarBody (ScriptableObject)
            const string starPath = "Assets/ScriptableObjects/Worlds/Kepler-442-Star.asset";
            StarBody star = AssetDatabase.LoadAssetAtPath<StarBody>(starPath);
            if (star == null)
            {
                star = ScriptableObject.CreateInstance<StarBody>();
                star.bodyName = "Kepler-442";
                star.spectralType = StarType.K;
                star.luminosity = 0.36f;
                star.mass = 0.61f;
                AssetDatabase.CreateAsset(star, starPath);
            }
            solarSystem.primaryStar = star;

            // Charger Kepler-442b
            OrbitalBody kepler442b = AssetDatabase.LoadAssetAtPath<OrbitalBody>("Assets/ScriptableObjects/Worlds/Kepler-442b.asset");
            if (kepler442b != null)
            {
                kepler442b.radius = 0.9f; // Rayon terrestre
                kepler442b.displayColor = new Color(0.4f, 0.6f, 0.8f); // Bleu-vert

                solarSystem.orbitalSlots = new OrbitalSlot[] {
                    new OrbitalSlot {
                        body = kepler442b,
                        orbit = new OrbitalParameters {
                            semiMajorAxis = 0.409f,
                            eccentricity = 0.04f,
                            orbitalPeriodDays = 112.3f,
                            orbitalInclination = 0f,
                            currentOrbitalPosition = 0f
                        }
                    }
                };
            }

            AssetDatabase.CreateAsset(solarSystem, solarSystemPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[SceneSetupHelper] SolarSystemData créé : " + solarSystemPath);
        }

        // ====================================================
        // Câbler SolarSystemView
        // ====================================================
        var solarSystemView = Find("SolarSystemRoot")?.GetComponent<SolarSystemView>();
        if (solarSystemView != null)
        {
            var solarType = typeof(SolarSystemView);
            solarType.GetField("solarSystem", flags).SetValue(solarSystemView, solarSystem);
            solarType.GetField("orbitScale", flags).SetValue(solarSystemView, 12f);
            solarType.GetField("defaultPlanetRadius", flags).SetValue(solarSystemView, 1f);
            solarType.GetField("planetRadiusScale", flags).SetValue(solarSystemView, 1.25f);
            solarType.GetField("minPlanetRadius", flags).SetValue(solarSystemView, 0.9f);
            solarType.GetField("maxPlanetRadius", flags).SetValue(solarSystemView, 3f);
            EditorUtility.SetDirty(Find("SolarSystemRoot"));
            Debug.Log("[SceneSetupHelper] SolarSystemView câblé");
        }

        // ====================================================
        // ViewManager
        // ====================================================
        var vm = managers.GetComponent<ViewManager>();
        if (vm != null)
        {
            var vmType = typeof(ViewManager);
            vmType.GetField("solarSystemRoot",  flags).SetValue(vm, Find("SolarSystemRoot"));
            vmType.GetField("planetRoot",        flags).SetValue(vm, Find("PlanetRoot"));
            vmType.GetField("hexGridRoot",       flags).SetValue(vm, Find("HexGridRoot"));
            vmType.GetField("cameraController",  flags).SetValue(vm, Find("Main Camera")?.GetComponent<CameraController>());
            vmType.GetField("solarSystemView",   flags).SetValue(vm, solarSystemView);
            vmType.GetField("planetSphere",      flags).SetValue(vm, Find("PlanetSphere")?.GetComponent<PlanetSphere>());
            vmType.GetField("hexGrid",           flags).SetValue(vm, hexGridGO?.GetComponent<HexGrid>());
            vmType.GetField("terraformHUD",      flags).SetValue(vm, canvas?.GetComponent<TerraformHUD>());
            vmType.GetField("terraformSystem",   flags).SetValue(vm, managers.GetComponent<TerraformSystem>());
            vmType.GetField("progressTracker",   flags).SetValue(vm, managers.GetComponent<TerraformProgressTracker>());
            vmType.GetField("solarMinZoom",      flags).SetValue(vm, 8f);
            vmType.GetField("solarMaxZoom",      flags).SetValue(vm, 60f);
            vmType.GetField("solarStartZoom",    flags).SetValue(vm, 24f);
            vmType.GetField("localMinZoom",      flags).SetValue(vm, 6f);
            vmType.GetField("localMaxZoom",      flags).SetValue(vm, 1000f);
            vmType.GetField("localStartZoom",    flags).SetValue(vm, 360f);
            vmType.GetField("planetMinZoom",     flags).SetValue(vm, 18f);
            vmType.GetField("planetMaxZoom",     flags).SetValue(vm, 80f);
            vmType.GetField("planetStartZoom",   flags).SetValue(vm, 30f);
            vmType.GetField("directPlanetClickToLocal", flags).SetValue(vm, true);
            EditorUtility.SetDirty(managers);
            Debug.Log("[SceneSetupHelper] ViewManager ✓");
        }

        // ====================================================
        // TerraformSystem
        // ====================================================
        var ts = managers.GetComponent<TerraformSystem>();
        if (ts != null)
        {
            typeof(TerraformSystem).GetField("hexGrid", flags).SetValue(ts, hexGridGO?.GetComponent<HexGrid>());
            EditorUtility.SetDirty(managers);
            Debug.Log("[SceneSetupHelper] TerraformSystem ✓");
        }

        // ====================================================
        // TerraformProgressTracker
        // ====================================================
        var tracker = managers.GetComponent<TerraformProgressTracker>();
        if (tracker != null)
        {
            typeof(TerraformProgressTracker).GetField("hexGrid", flags).SetValue(tracker, hexGridGO?.GetComponent<HexGrid>());
            EditorUtility.SetDirty(managers);
            Debug.Log("[SceneSetupHelper] TerraformProgressTracker ✓");
        }

        // ====================================================
        // HexInput (sur Main Camera)
        // ====================================================
        var hexInput = Find("Main Camera")?.GetComponent<HexInput>();
        if (hexInput != null)
        {
            typeof(HexInput).GetField("viewManager", flags).SetValue(hexInput, vm);
            EditorUtility.SetDirty(Find("Main Camera"));
            Debug.Log("[SceneSetupHelper] HexInput.viewManager ✓");
        }

        // ====================================================
        // TerraformHUD (sur Canvas)
        // ====================================================
        var hud = canvas?.GetComponent<TerraformHUD>();
        if (hud != null)
        {
            var hudType = typeof(TerraformHUD);
            hudType.GetField("progressSlider",   flags).SetValue(hud, Find("ProgressSlider")?.GetComponent<UnityEngine.UI.Slider>());
            hudType.GetField("progressLabel",    flags).SetValue(hud, Find("ProgressLabel")?.GetComponent<TMPro.TextMeshProUGUI>());
            hudType.GetField("selectedHexPanel", flags).SetValue(hud, Find("SelectedHexPanel"));
            hudType.GetField("hexInfoLabel",     flags).SetValue(hud, Find("HexInfoLabel")?.GetComponent<TMPro.TextMeshProUGUI>());
            hudType.GetField("progressTracker",  flags).SetValue(hud, tracker);
            hudType.GetField("terraformSystem",  flags).SetValue(hud, ts);
            EditorUtility.SetDirty(canvas);
            Debug.Log("[SceneSetupHelper] TerraformHUD ✓");
        }

        // ====================================================
        // Positionner la caméra pour voir le système solaire
        // ====================================================
        var camGO = Find("Main Camera");
        if (camGO != null)
        {
            var cameraController = camGO.GetComponent<CameraController>();
            if (cameraController != null)
            {
                var camType = typeof(CameraController);
                camType.GetField("zoomSpeed", flags).SetValue(cameraController, 10f);
                camType.GetField("zoomScaleFactor", flags).SetValue(cameraController, 0.45f);
                camType.GetField("orbitSensitivity", flags).SetValue(cameraController, 0.3f);
                camType.GetField("orbitMinDistance", flags).SetValue(cameraController, 8f);
                camType.GetField("orbitMaxDistance", flags).SetValue(cameraController, 40f);
                camType.GetField("orbitScrollSpeed", flags).SetValue(cameraController, 12f);
                EditorUtility.SetDirty(cameraController);
            }

            var camera = camGO.GetComponent<Camera>();
            if (camera != null)
            {
                camera.orthographic = true;
                camera.orthographicSize = 24f;
                EditorUtility.SetDirty(camera);
            }

            camGO.transform.position = new Vector3(0f, 50f, 0f);
            camGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            EditorUtility.SetDirty(camGO);
            Debug.Log("[SceneSetupHelper] Caméra positionnée pour vue solaire");
        }

        // Sauvegarde la scène
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneSetupHelper] Scène sauvegardée — toutes les références câblées !");
        EditorUtility.DisplayDialog("Setup terminé", "Toutes les références ont été câblées et la scène sauvegardée.", "OK");
    }

    [MenuItem("Tools/Terraformation/Clean Old Canvas UI")]
    public static void CleanOldCanvasUI()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Play Mode actif",
                "Arrête le Play Mode avant de nettoyer la scène.", "OK");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        var allGOs = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                allGOs.Add(t.gameObject);

        GameObject Find(string n) {
            foreach (var go in allGOs) if (go.name == n) return go;
            return null;
        }

        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var canvas = Find("Canvas");
        int disabled = 0;

        // GOs à désactiver (remplacés par GameHUD)
        string[] toDisable = {
            "ProgressSlider", "ProgressLabel",
            "SelectedHexPanel",
        };
        foreach (var name in toDisable)
        {
            var go = Find(name);
            if (go != null && go.activeSelf) { go.SetActive(false); disabled++; EditorUtility.SetDirty(go); }
        }

        // Désactiver les anciens boutons toggle (ClaimTileMenu, PlanetViewToggleButton, DebugTileToggleButton)
        string[] oldButtonNames = { "ClaimTileMenuButton", "PlanetViewToggleButton", "DebugTileToggleButton", "ButtonPlanetViewToggle" };
        foreach (var name in oldButtonNames)
        {
            var go = Find(name);
            if (go != null && go.activeSelf) { go.SetActive(false); disabled++; EditorUtility.SetDirty(go); }
        }

        // Nuller les refs Inspector de TerraformHUD vers les GOs désactivés
        var hud = canvas?.GetComponent<TerraformHUD>();
        if (hud != null)
        {
            var hudType = typeof(TerraformHUD);
            hudType.GetField("progressSlider",   flags).SetValue(hud, null);
            hudType.GetField("progressLabel",    flags).SetValue(hud, null);
            hudType.GetField("selectedHexPanel", flags).SetValue(hud, null);
            hudType.GetField("hexInfoLabel",     flags).SetValue(hud, null);
            hudType.GetField("openLocalButton",  flags).SetValue(hud, null);
            hudType.GetField("closeLocalButton", flags).SetValue(hud, null);
            EditorUtility.SetDirty(canvas);
            Debug.Log("[SceneSetupHelper] TerraformHUD : refs UI legacy nullifiées ✓");
        }

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);

        string msg = $"{disabled} élément(s) désactivé(s), refs TerraformHUD nullifiées.";
        Debug.Log("[SceneSetupHelper] Clean Old Canvas : " + msg);
        EditorUtility.DisplayDialog("Nettoyage terminé", msg + "\n\nLa scène a été sauvegardée.", "OK");
    }

    [MenuItem("Tools/Terraformation/Add GameHUD to Scene")]
    public static void AddGameHUD()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Play Mode actif",
                "Arrête le Play Mode avant d'ajouter GameHUD à la scène.", "OK");
            return;
        }

        var scene = SceneManager.GetActiveScene();

        // Vérifie si déjà présent
        foreach (var root in scene.GetRootGameObjects())
            foreach (var existing in root.GetComponentsInChildren<GameHUD>(true))
            {
                Debug.LogWarning("[SceneSetupHelper] GameHUD déjà présent dans la scène.");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

        var go = new GameObject("GameHUD");
        go.AddComponent<GameHUD>();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = go;
        Debug.Log("[SceneSetupHelper] GameHUD créé dans la scène !");
        EditorUtility.DisplayDialog("GameHUD ajouté",
            "GameObject 'GameHUD' créé avec le composant GameHUD.\n" +
            "Les références (ViewManager, TerraformHUD, PlanetSphereGoldberg) seront auto-trouvées au Start().",
            "OK");
    }
}
#endif
