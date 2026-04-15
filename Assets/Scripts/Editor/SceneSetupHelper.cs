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
            vmType.GetField("solarSystemView",   flags).SetValue(vm, Find("SolarSystemRoot")?.GetComponent<SolarSystemView>());
            vmType.GetField("planetSphere",      flags).SetValue(vm, Find("PlanetSphere")?.GetComponent<PlanetSphere>());
            vmType.GetField("hexGrid",           flags).SetValue(vm, hexGridGO?.GetComponent<HexGrid>());
            vmType.GetField("terraformHUD",      flags).SetValue(vm, canvas?.GetComponent<TerraformHUD>());
            vmType.GetField("terraformSystem",   flags).SetValue(vm, managers.GetComponent<TerraformSystem>());
            vmType.GetField("progressTracker",   flags).SetValue(vm, managers.GetComponent<TerraformProgressTracker>());
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

        // Sauvegarde la scène
        UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene);
        Debug.Log("[SceneSetupHelper] Scène sauvegardée — toutes les références câblées !");
        EditorUtility.DisplayDialog("Setup terminé", "Toutes les références ont été câblées et la scène sauvegardée.", "OK");
    }
}
#endif
