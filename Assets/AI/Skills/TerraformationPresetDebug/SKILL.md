---
name: terraformation-preset-debug
description: Guide AI Assistant through Terraformation preset validation using the project's debug bridge, smoke test script, and expected preset behaviors.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation Preset Debug

Use this skill when the task is to validate or diagnose a Terraformation preset such as Ocean, Arid, Frozen, Coast, Basin, or ProjectionOnly.

## Goal

Keep preset debugging deterministic.

Prefer the project's runtime debug bridge, smoke test script, and documented preset expectations over vague visual guesses.

## Required project references

- `Documentation/TEST_PRESETS_CHECKLIST.md`
- `Documentation/AI_DEBUG_WORKFLOW.md`
- `Documentation/MCP_TOOLS_ARCHITECTURE.md`
- `Tools/Invoke-TerraformationDebugSmokeTest.ps1`

## Working rules

1. Start from an explicit preset name.
2. Prefer collected facts over intuition.
3. Compare projection, local summary, console, and screenshot before concluding.
4. If Unity is not in Play mode, say so explicitly.
5. If the bridge or MCP path is unavailable, fall back to manual in-Editor inspection but state the reduced confidence.

## Recommended order

1. Check current state.
2. Launch the target preset.
3. Read projection summary.
4. Open a representative region if needed.
5. Read local summary.
6. Read console warnings and errors.
7. Capture a screenshot when visual confirmation matters.
8. Compare results against `Documentation/TEST_PRESETS_CHECKLIST.md`.

## Expected preset heuristics

### Ocean

- Projection should be dominated by `OpenOcean`.
- Local average water should stay high.

### Arid

- `Dry` should dominate projection and local summaries.
- Rivers and inland water should stay limited.

### Frozen

- Frozen water or strongly cold average temperature should appear.
- Vegetation should not dominate without evidence.

### Coast

- `Coast` cells must appear in projection and preferably local summaries.
- The result must not collapse into fully brown or fully blue output.

### Basin

- Inland water and basin-like accumulation should appear.
- The result must stay distinct from Ocean.

## Output style

When reporting findings, structure the answer as:

1. Current runtime state.
2. Projection findings.
3. Local findings.
4. Console findings.
5. Verdict against checklist expectations.
6. Suspected files or systems if the preset fails.

## Avoid

- Saying a preset is fixed from visuals alone.
- Mixing several presets in one diagnosis unless explicitly comparing them.
- Changing hydrology, biome, and UI flow at the same time.