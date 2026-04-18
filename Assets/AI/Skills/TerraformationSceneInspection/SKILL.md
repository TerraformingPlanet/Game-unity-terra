---
name: terraformation-scene-inspection
description: Guide AI Assistant to safely inspect and modify the Terraformation Unity scene using unity-mcp tools without causing crashes or stack overflows.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation Scene Inspection & Wiring

Use this skill when the task is to inspect scene objects, read component lists, find GameObjects, check field assignments, or verify the scene hierarchy structure.

## Safe tool patterns (validated April 2026, exhaustively tested)

### Golden RunCommand template ✅
```csharp
using UnityEngine;
using UnityEditor;

internal class CommandScript : IRunCommand
{
    public void Execute(ExecutionResult result)
    {
        // Your code here
        result.RegisterObjectModification(go); // BEFORE changing properties
        result.RegisterObjectCreation(go);     // AFTER creating objects
        result.Log("message {0}", obj);
        result.LogError("error");
    }
}
```
**Rules:**
- Class MUST be named `CommandScript`, MUST be `internal`
- Use `result.RegisterObjectModification(go)` BEFORE modifying any component
- Use `result.RegisterObjectCreation(go)` AFTER creating GOs
- `using UnityEngine.UI` causes `Image` ambiguity — use `UnityEngine.UI.Image` fully qualified
- `GameObject.Find()` does NOT find inactive GOs — use `Resources.FindObjectsOfTypeAll<T>()` instead
- To assign Inspector fields: `new SerializedObject(component); FindProperty("fieldName"); ApplyModifiedProperties()`
- Save scene at end: `UnityEditor.SceneManagement.EditorSceneManager.SaveOpenScenes()`



### Inspect a GameObject by name
```
ManageGameObject action=find target=<name>
```
Returns: instanceID, parentInstanceID, componentNames[], activeSelf, activeInHierarchy, transform.
**This is the ONLY safe way to read component lists.** `get_components` causes crashes.

### Browse the hierarchy tree
```
ManageScene Action=GetHierarchy Depth=2
```
Use Depth=2 for a compact overview. Depth=-1 returns the full tree (large payload, written to file).

### Search text in a script
```
FindInFile Pattern=<text> Uri=Assets/Scripts/<folder>/<File>.cs
```
Returns line/col matches. Use for verifying that a field or method exists in a script.

### Script capabilities (what edits are possible)
```
ManageScript_capabilities
```
Returns: replace_class, replace_method, insert_method, anchor_insert, regex_replace, etc.

### Create a raw GameObject (3D only, no parent support)
```
ManageGameObject action=create name=<name>
```
WARNING: `parentInstanceId` parameter is ignored — GO lands at scene root with Transform only.
For UI GameObjects (RectTransform inside Canvas): create manually in Unity Editor.

### Delete a GameObject by name
```
ManageGameObject action=delete target=<name>
```

### Get console logs (Play mode only)
```
GetConsoleLogs
```

## NEVER use these — they crash or overflow Unity

| Call | Effect |
|---|---|
| `ManageGameObject action=get_components instanceId=<id>` | Stack overflow → Unity crash |
| `ManageGameObject action=set_parent` | Unknown action error |
| `RunCommand` returning string | CS0029 compile error (must return int) |
| `RunCommand` with top-level using | CS8805 error; use `//using` inline or rely on default imports |

## Safe RunCommand template (if needed)
```csharp
// Return type must be int
var go = UnityEditor.EditorUtility.InstanceIDToObject(<id>) as UnityEngine.GameObject;
UnityEngine.Debug.Log(go != null ? go.name : "not found");
return 0;
```

## Workflow: verify Inspector wiring before asking user to do it manually

1. `ManageScene Action=GetHierarchy Depth=2` — get instanceIDs of key GOs
2. `ManageGameObject action=find target=<GO>` — confirm componentNames
3. `FindInFile Pattern=<field> Uri=Assets/Scripts/...` — confirm the field exists in script
4. If all three match → wiring is structurally ready, user just assigns in Inspector
5. If a GO is missing → guide user to create it manually (UI) or use `create` (3D)

## UI hierarchy (Terraformation Canvas structure)

```
Scene root
└── GameObject
    └── Canvas (instanceID 59784)          ← main Screen Space Overlay canvas
        ├── SolarSystemRoot
        ├── TooltipPanel / SelectedHexPanel / etc.
        ├── ButtonPlanetViewToggle
        └── PlanetFlatOverlayCanvas (id 60114)  ← nested canvas for flat view UI
            └── [MinimapPanel, DebugButton — create manually]

Scene root
└── PlanetFlatView (instanceID -28976)
    └── MinimapCamera (id 60174)  ← Camera + MinimapController + URP data
```

## MinimapCamera current state (confirmed)
- componentNames: Transform, Camera, UniversalAdditionalCameraData, MinimapController
- activeSelf=true, activeInHierarchy=false (normal — PlanetFlatView is inactive at start)
- parentInstanceID = PlanetFlatView

## PlanetFlatOverlayCanvas current state (confirmed)
- componentNames: RectTransform, Canvas, CanvasScaler, GraphicRaycaster
- layer=5 (UI), activeSelf=true
- Children still needed: MinimapPanel > RawImage (+ MinimapClickHandler) + ViewportIndicator image
