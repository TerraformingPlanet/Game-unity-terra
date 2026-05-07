#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Script éditeur — câble toutes les références de la scène Game via SerializedObject.
/// Menu : Tools > Terraformation > Wire Scene References
/// </summary>
public static class SceneSetupHelper
{
    // =========================================================
    // Helpers SerializedObject (remplacent GetField/SetValue)
    // =========================================================

    private static SerializedObject SO(Object target) => new SerializedObject(target);

    private static void SetRef(SerializedObject so, string prop, Object value)
    {
        var sp = so.FindProperty(prop);
        if (sp == null) { Debug.LogWarning($"[SceneSetupHelper] Propriété introuvable : {so.targetObject.GetType().Name}.{prop}"); return; }
        sp.objectReferenceValue = value;
    }

    private static void SetFloat(SerializedObject so, string prop, float value)
    {
        var sp = so.FindProperty(prop);
        if (sp == null) { Debug.LogWarning($"[SceneSetupHelper] Propriété introuvable : {so.targetObject.GetType().Name}.{prop}"); return; }
        sp.floatValue = value;
    }

    private static void SetBool(SerializedObject so, string prop, bool value)
    {
        var sp = so.FindProperty(prop);
        if (sp == null) { Debug.LogWarning($"[SceneSetupHelper] Propriété introuvable : {so.targetObject.GetType().Name}.{prop}"); return; }
        sp.boolValue = value;
    }

    // =========================================================
    // Entrée principale
    // =========================================================

    [MenuItem("Tools/Terraformation/Wire Scene References")]
    public static void WireSceneReferences()
    {
        var scene  = SceneManager.GetActiveScene();
        var allGOs = CollectAllGameObjects(scene);
        GameObject Find(string n) => FindGO(allGOs, n);

        SolarSystemData solarSystem = EnsureSolarSystemAsset();
        SolarSystemView ssView      = WireSolarSystemView(Find, solarSystem);
        var             vm          = WireViewManager(Find, ssView);
        WireComponents(Find, vm);
        WireCameraSetup(Find);

        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneSetupHelper] Scène sauvegardée — toutes les références câblées !");
        EditorUtility.DisplayDialog("Setup terminé", "Toutes les références ont été câblées et la scène sauvegardée.", "OK");
    }

    private static SolarSystemData EnsureSolarSystemAsset()
    {
        string solarSystemPath = "Assets/ScriptableObjects/Worlds/Kepler-442-System.asset";
        SolarSystemData solarSystem = AssetDatabase.LoadAssetAtPath<SolarSystemData>(solarSystemPath);
        if (solarSystem != null) return solarSystem;
        solarSystem = ScriptableObject.CreateInstance<SolarSystemData>();
        solarSystem.systemName = "Kepler-442";
        solarSystem.distanceLightYears = 1115f;
        const string starPath = "Assets/ScriptableObjects/Worlds/Kepler-442-Star.asset";
        StarBody star = AssetDatabase.LoadAssetAtPath<StarBody>(starPath);
        if (star == null)
        {
            star = ScriptableObject.CreateInstance<StarBody>();
            star.bodyName = "Kepler-442"; star.spectralType = StarType.K;
            star.luminosity = 0.36f; star.mass = 0.61f;
            AssetDatabase.CreateAsset(star, starPath);
        }
        solarSystem.primaryStar = star;
        OrbitalBody kepler442b = AssetDatabase.LoadAssetAtPath<OrbitalBody>("Assets/ScriptableObjects/Worlds/Kepler-442b.asset");
        if (kepler442b != null)
        {
            kepler442b.radius = 0.9f; kepler442b.displayColor = new Color(0.4f, 0.6f, 0.8f);
            solarSystem.orbitalSlots = new OrbitalSlot[] {
                new OrbitalSlot { body = kepler442b, orbit = new OrbitalParameters {
                    semiMajorAxis = 0.409f, eccentricity = 0.04f, orbitalPeriodDays = 112.3f }}
            };
        }
        AssetDatabase.CreateAsset(solarSystem, solarSystemPath);
        AssetDatabase.SaveAssets();
        Debug.Log("[SceneSetupHelper] SolarSystemData créé : " + solarSystemPath);
        return solarSystem;
    }

    private static SolarSystemView WireSolarSystemView(System.Func<string, GameObject> Find, SolarSystemData solarSystem)
    {
        var ssv = Find("SolarSystemRoot")?.GetComponent<SolarSystemView>();
        if (ssv == null) return null;

        var so = SO(ssv);
        SetRef  (so, "solarSystem",        solarSystem);
        SetFloat(so, "orbitScale",          12f);
        SetFloat(so, "defaultPlanetRadius", 1f);
        SetFloat(so, "planetRadiusScale",   1.25f);
        SetFloat(so, "minPlanetRadius",     0.9f);
        SetFloat(so, "maxPlanetRadius",     3f);
        so.ApplyModifiedProperties();

        Debug.Log("[SceneSetupHelper] SolarSystemView ✓");
        return ssv;
    }

    private static ViewManager WireViewManager(System.Func<string, GameObject> Find, SolarSystemView ssView)
    {
        var managers = Find("Managers");
        var vm = managers?.GetComponent<ViewManager>();
        if (vm == null) { Debug.LogError("[SceneSetupHelper] ViewManager introuvable sur Managers."); return null; }

        var so = SO(vm);
        SetRef  (so, "solarSystemRoot",          Find("SolarSystemRoot"));
        SetRef  (so, "planetRoot",               Find("PlanetRoot"));
        SetRef  (so, "hexGridRoot",              Find("HexGridRoot"));
        SetRef  (so, "cameraController",         Find("Main Camera")?.GetComponent<CameraController>());
        SetRef  (so, "solarSystemView",          ssView);
        SetRef  (so, "planetSphere",             Find("PlanetSphere")?.GetComponent<PlanetSphere>());
        SetRef  (so, "hexGrid",                  Find("HexGrid")?.GetComponent<HexGrid>());
        SetRef  (so, "terraformHUD",             Find("Canvas")?.GetComponent<TerraformHUD>());
        SetRef  (so, "terraformSystem",          managers.GetComponent<TerraformSystem>());
        SetRef  (so, "progressTracker",          managers.GetComponent<TerraformProgressTracker>());
        SetFloat(so, "solarOrbitMinDistance",    8f);
        SetFloat(so, "solarOrbitMaxDistance",   60f);
        SetFloat(so, "solarOrbitStartDistance", 24f);
        SetFloat(so, "localMinZoom",             6f);
        SetFloat(so, "localMaxZoom",          1000f);
        SetFloat(so, "planetOrbitMinDistance",  18f);
        SetFloat(so, "planetOrbitMaxDistance",  80f);
        SetFloat(so, "planetOrbitStartDistance",30f);
        SetBool (so, "directPlanetClickToLocal", false);
        so.ApplyModifiedProperties();

        Debug.Log("[SceneSetupHelper] ViewManager ✓");
        return vm;
    }

    private static void WireComponents(System.Func<string, GameObject> Find, ViewManager vm)
    {
        var managers = Find("Managers");
        var canvas   = Find("Canvas");
        var hexGrid  = Find("HexGrid")?.GetComponent<HexGrid>();

        var ts = managers?.GetComponent<TerraformSystem>();
        if (ts != null)
        {
            var so = SO(ts);
            SetRef(so, "hexGrid", hexGrid);
            so.ApplyModifiedProperties();
            Debug.Log("[SceneSetupHelper] TerraformSystem ✓");
        }

        var tracker = managers?.GetComponent<TerraformProgressTracker>();
        if (tracker != null)
        {
            var so = SO(tracker);
            SetRef(so, "hexGrid", hexGrid);
            so.ApplyModifiedProperties();
            Debug.Log("[SceneSetupHelper] TerraformProgressTracker ✓");
        }

        var hexInput = Find("Main Camera")?.GetComponent<HexInput>();
        if (hexInput != null)
        {
            var so = SO(hexInput);
            SetRef(so, "viewManager", vm);
            so.ApplyModifiedProperties();
            Debug.Log("[SceneSetupHelper] HexInput.viewManager ✓");
        }

        var hud = canvas?.GetComponent<TerraformHUD>();
        if (hud != null)
        {
            var so = SO(hud);
            SetRef(so, "progressSlider",   Find("ProgressSlider")?.GetComponent<UnityEngine.UI.Slider>());
            SetRef(so, "progressLabel",    Find("ProgressLabel")?.GetComponent<TMPro.TextMeshProUGUI>());
            SetRef(so, "selectedHexPanel", Find("SelectedHexPanel"));
            SetRef(so, "hexInfoLabel",     Find("HexInfoLabel")?.GetComponent<TMPro.TextMeshProUGUI>());
            SetRef(so, "progressTracker",  tracker);
            SetRef(so, "terraformSystem",  ts);
            so.ApplyModifiedProperties();
            Debug.Log("[SceneSetupHelper] TerraformHUD ✓");
        }
    }

    private static void WireCameraSetup(System.Func<string, GameObject> Find)
    {
        var camGO = Find("Main Camera");
        if (camGO == null) return;

        var cc = camGO.GetComponent<CameraController>();
        if (cc != null)
        {
            var so = SO(cc);
            SetFloat(so, "zoomSpeed",        10f);
            SetFloat(so, "zoomScaleFactor",  0.45f);
            SetFloat(so, "orbitSensitivity", 0.3f);
            SetFloat(so, "orbitMinDistance", 8f);
            SetFloat(so, "orbitMaxDistance", 40f);
            SetFloat(so, "orbitScrollSpeed", 12f);
            so.ApplyModifiedProperties();
        }

        var camera = camGO.GetComponent<Camera>();
        if (camera != null)
        {
            camera.orthographic     = true;
            camera.orthographicSize = 24f;
            EditorUtility.SetDirty(camera);
        }

        camGO.transform.position = new Vector3(0f, 50f, 0f);
        camGO.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        EditorUtility.SetDirty(camGO.transform);
        Debug.Log("[SceneSetupHelper] Caméra positionnée pour vue solaire ✓");
    }

    [MenuItem("Tools/Terraformation/Clean Old Canvas UI")]
    public static void CleanOldCanvasUI()
    {
        if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("Play Mode actif", "Arrête le Play Mode avant de nettoyer la scène.", "OK"); return; }

        var scene  = SceneManager.GetActiveScene();
        var allGOs = CollectAllGameObjects(scene);
        GameObject Find(string n) => FindGO(allGOs, n);

        int disabled = DisableLegacyObjects(Find);
        NullifyLegacyHudRefs(Find("Canvas")?.GetComponent<TerraformHUD>());

        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        string msg = $"{disabled} élément(s) désactivé(s), refs TerraformHUD nullifiées.";
        Debug.Log("[SceneSetupHelper] Clean Old Canvas : " + msg);
        EditorUtility.DisplayDialog("Nettoyage terminé", msg + "\n\nLa scène a été sauvegardée.", "OK");
    }

    private static int DisableLegacyObjects(System.Func<string, GameObject> Find)
    {
        int disabled = 0;
        foreach (var name in new[] { "ProgressSlider", "ProgressLabel", "SelectedHexPanel" })
        {
            var go = Find(name);
            if (go != null && go.activeSelf) { go.SetActive(false); disabled++; EditorUtility.SetDirty(go); }
        }
        foreach (var name in new[] { "ClaimTileMenuButton", "PlanetViewToggleButton", "DebugTileToggleButton", "ButtonPlanetViewToggle" })
        {
            var go = Find(name);
            if (go != null && go.activeSelf) { go.SetActive(false); disabled++; EditorUtility.SetDirty(go); }
        }
        return disabled;
    }

    private static void NullifyLegacyHudRefs(TerraformHUD hud)
    {
        if (hud == null) return;
        var so = SO(hud);
        SetRef(so, "progressSlider",   null);
        SetRef(so, "progressLabel",    null);
        SetRef(so, "selectedHexPanel", null);
        SetRef(so, "hexInfoLabel",     null);
        SetRef(so, "openLocalButton",  null);
        SetRef(so, "closeLocalButton", null);
        so.ApplyModifiedProperties();
        Debug.Log("[SceneSetupHelper] TerraformHUD : refs UI legacy nullifiées ✓");
    }

    [MenuItem("Tools/Terraformation/Add GameHUDController to Scene")]
    public static void AddGameHUDController()
    {
        if (EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Play Mode actif",
                "Arrête le Play Mode avant d'ajouter GameHUDController à la scène.", "OK");
            return;
        }

        var scene = SceneManager.GetActiveScene();

        // Vérifie si déjà présent
        foreach (var root in scene.GetRootGameObjects())
            foreach (var existing in root.GetComponentsInChildren<GameHUDController>(true))
            {
                Debug.LogWarning("[SceneSetupHelper] GameHUDController déjà présent dans la scène.");
                Selection.activeGameObject = existing.gameObject;
                return;
            }

        var go = new GameObject("GameHUDController");
        go.AddComponent<UnityEngine.UIElements.UIDocument>();
        go.AddComponent<GameHUDController>();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = go;
        Debug.Log("[SceneSetupHelper] GameHUDController créé dans la scène !");
        EditorUtility.DisplayDialog("GameHUDController ajouté",
            "GameObject 'GameHUDController' créé avec UIDocument + GameHUDController.\n" +
            "Assigne les UXML templates et StyleSheets dans l'Inspector.",
            "OK");
    }

    // =========================================================
    // Utilitaires de scène
    // =========================================================

    private static List<GameObject> CollectAllGameObjects(Scene scene)
    {
        var list = new List<GameObject>();
        foreach (var root in scene.GetRootGameObjects())
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                list.Add(t.gameObject);
        return list;
    }

    private static GameObject FindGO(List<GameObject> all, string name)
    {
        foreach (var go in all)
            if (go.name == name) return go;
        Debug.LogError($"[SceneSetupHelper] GameObject introuvable : '{name}'");
        return null;
    }
}
#endif
