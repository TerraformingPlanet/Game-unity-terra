---
name: terraformation-hydrology-diagnosis
description: Help AI Assistant diagnose Terraformation local hydrology, basin behavior, rivers, coherence bias, and biome mismatches using project summaries and runtime debug tooling.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation Hydrology Diagnosis

Use this skill when the problem concerns local water distribution, basin plausibility, river creation, water classification, biome mismatch, or projection-to-local coherence.

## Goal

Diagnose hydrology regressions with structured evidence before proposing code changes.

## Priority evidence

Prefer these sources in order:

1. dedicated-server generation stats / quality suite if the bug may originate from projection tuning
2. local summary from the runtime debug bridge
3. projection summary if the bug may originate from macro coherence
4. console warnings and errors
5. selected cell information and HUD state
6. screenshot only as supporting evidence

## Main systems to inspect

- `WaterSystem`
- `HydrologySystem`
- `WaterClassificationSystem`
- `RiverSystem`
- `BiomeSystem`
- `CoherenceValidationSystem`
- `MapGenParameters`

## Expected reasoning pattern

1. Identify whether the bug is projection-side, local-side, or transition-side.
2. Compare `projectedWaterRatio` with local `averageWaterRatio`.
3. Check whether `Dry`, `Coast`, `InlandWater`, `OpenOcean`, and `FrozenWater` counts fit the expected preset.
4. Check river, basin, channel, downstream, and overflow counts.
5. Only then suggest which system or threshold is most likely responsible.

## Key diagnostic questions

- Is the local region too dry compared with the projected water ratio?
- Are basins present but not retaining enough water?
- Are rivers spawning in arid contexts where they should not?
- Are coast or basin presets collapsing into generic brown terrain?
- Are biome results contradicting water and temperature summaries?

## Healthy signals

- Ocean-like regions keep high local water average.
- Basin-like regions show basin or inland water evidence.
- Coast scenarios show real transition cells instead of all-dry or all-open-water output.
- Frozen scenarios keep cold temperatures or frozen water evidence.
- Basin tuning should converge toward connectivity/outlet logic, not only a flat moisture bonus.

## Avoid

- Fixing hydrology from screenshots alone.
- Changing `WaterSystem`, `RiverSystem`, and `BiomeSystem` together without isolating the mismatch.
- Treating a projection-only issue as a local generation issue without evidence.

## Reporting pattern

When concluding, structure the answer as:

1. observed mismatch
2. strongest evidence
3. likely subsystem
4. likely threshold or rule involved
5. safest next edit or verification
