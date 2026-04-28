using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Editor utility — assigns all HUD UXML templates and USS stylesheets
/// to the [SerializeField] fields on HUDController and its sub-controllers.
/// Run once via menu: Terraformation / Setup HUD Assets
/// </summary>
public static class HUDAssetsSetup
{
    [MenuItem("Terraformation/Setup HUD Assets")]
    public static void SetupHUDAssets()
    {
        var hud = Object.FindAnyObjectByType<GameHUDController>(FindObjectsInactive.Include);
        if (hud == null)
        {
            Debug.LogError("[HUDAssetsSetup] GameHUDController not found in scene. Open the Game scene first.");
            return;
        }

        int assigned = 0;

        // ── GameHUDController (all templates centralized here) ────────────
        var soHud = new SerializedObject(hud);
        assigned += AssignAsset(soHud, "tooltipTemplate",          "Assets/UI/Templates/Tooltip.uxml");
        assigned += AssignAsset(soHud, "eventPopupTemplate",       "Assets/UI/Templates/EventPopup.uxml");
        assigned += AssignAsset(soHud, "tileInspectorTemplate",    "Assets/UI/Templates/TileInspector.uxml");
        assigned += AssignAsset(soHud, "eventFeedTemplate",        "Assets/UI/Templates/EventFeed.uxml");
        assigned += AssignAsset(soHud, "debugDrawerTemplate",      "Assets/UI/Templates/DebugDrawer.uxml");
        assigned += AssignAsset(soHud, "bottomActionBarTemplate",  "Assets/UI/Templates/BottomActionBar.uxml");
        assigned += AssignAsset(soHud, "variablesStyleSheet",      "Assets/UI/Styles/variables.uss");
        assigned += AssignAsset(soHud, "baseStyleSheet",           "Assets/UI/Styles/base.uss");
        assigned += AssignAsset(soHud, "debugDrawerStyleSheet",    "Assets/UI/Styles/DebugDrawer.uss");
        soHud.ApplyModifiedProperties();

        // ── Save scene ─────────────────────────────────────────────────────
        EditorSceneManager.MarkSceneDirty(hud.gameObject.scene);
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        Debug.Log($"[HUDAssetsSetup] Done — {assigned} assets assigned and scene saved.");
    }

    private static int AssignAsset(SerializedObject so, string fieldName, string assetPath)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogWarning($"[HUDAssetsSetup] Field '{fieldName}' not found on {so.targetObject.GetType().Name}.");
            return 0;
        }

        var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
        if (asset == null)
        {
            Debug.LogWarning($"[HUDAssetsSetup] Asset not found: '{assetPath}'.");
            return 0;
        }

        if (prop.objectReferenceValue == asset) return 0; // already assigned, no change

        prop.objectReferenceValue = asset;
        Debug.Log($"[HUDAssetsSetup]   {so.targetObject.GetType().Name}.{fieldName} \u2190 {assetPath}");
        return 1;
    }
}
