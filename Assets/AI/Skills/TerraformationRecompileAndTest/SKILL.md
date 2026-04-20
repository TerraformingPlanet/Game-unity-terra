---
name: terraformation-recompile-and-test
description: Stop Play Mode, wait for recompilation, restart Play Mode, then rerun preset smoke tests. Use whenever a C# fix needs to take effect before testing.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation — Recompile and Test Cycle

Use this skill whenever a C# change has been made but Unity is in Play Mode (which blocks recompilation). The fix requires stopping Play Mode, letting Unity recompile, then re-entering Play Mode and rerunning tests.

## When to use

- A fix to a MonoBehaviour was applied while Unity was in Play Mode
- `launch-preset` or `open-region` bridge endpoints return 400 (failed) after a code fix
- Any test failure that is clearly caused by stale compiled code

## Workflow — execute in order

### Step 1 — Stop Play Mode

```tool
mcp_unity-mcp_Unity_ManageEditor  Action=Stop  WaitForCompletion=true
```

### Step 2 — Force script recompilation

```tool
mcp_unity-mcp_Unity_RunCommand
// Trigger recompile check — wait for AssetDatabase refresh
UnityEditor.AssetDatabase.Refresh();
UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
```

Or via menu item:
```tool
mcp_unity-mcp_Unity_ManageMenuItem  Action=Execute  MenuPath="Assets/Refresh"
```

### Step 3 — Wait for compilation

Poll `mcp_unity-mcp_Unity_ManageEditor  Action=GetState` until `isCompiling=false`.

### Step 4 — Restart Play Mode

```tool
mcp_unity-mcp_Unity_ManageEditor  Action=Play  WaitForCompletion=true
```

### Step 5 — Rerun smoke tests (via terraformation MCP)

For each preset in [Ocean, Arid, Frozen, Coast, Basin]:
```tool
mcp_terraformatio_run_preset_smoke_test  preset_name=<Preset>  capture_screenshot=true
```

## Known issue — TestLaunchMenu inactive GO

`TestLaunchMenu` GO is **inactive** in the scene by default.  
`RuntimeDebugFacade.ResolveReferences()` calls `FindFirstObjectByType<TestLaunchMenu>()` which **ignores inactive objects** → `launch-preset` returns 400.

**Permanent fix** (already applied to `RuntimeDebugFacade.cs`):
```csharp
// line ~338 in ResolveReferences()
if (testLaunchMenu == null)
    testLaunchMenu = FindFirstObjectByType<TestLaunchMenu>(FindObjectsInactive.Include);
```

**Runtime workaround** (no recompile needed — use when still in Play Mode):
```tool
mcp_unity-mcp_Unity_RunCommand
// Activate the GO so FindFirstObjectByType can discover it
TestLaunchMenu[] menus = Resources.FindObjectsOfTypeAll<TestLaunchMenu>();
if (menus.Length > 0) menus[0].gameObject.SetActive(true);
```

Apply the workaround first, then verify with `facade.LaunchPresetByName("Ocean")` before running full smoke tests.

## Validation criteria per preset

| Preset | Expected dominant class | Key threshold |
|--------|------------------------|---------------|
| Ocean | OpenOcean | `waterRatio >= 0.92` most cells |
| Arid | Dry | max 8% wet cells (`isExtremeArid=true`) |
| Frozen | FrozenWater | ≥ 60% frozen cells (`isExtremeFrozen=true`) |
| Coast | Coast | coastCells dominant, mix Dry + OpenOcean neighbors |
| Basin | InlandWater | `basinCells > 0`, `lakeWaterThreshold` met |
