---
name: terraformation-basin-connectivity
description: Help AI Assistant improve Basin and hydrology connectivity on the H3 projection by focusing on runoff, outlets, inland water retention, and coast-vs-lake distinction.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation Basin Connectivity

Use this skill when the issue concerns Basin behavior, inland water retention, outlet logic, or confusion between coast, lake, and generic wet tiles on the H3 projection.

## Goal

Move Basin away from threshold hacks toward structural hydrology.

## Main files

- `SimulationCore/terraformation_sim/logic.py`
- `Documentation/ROADMAP.md`
- `Documentation/MapGeneration_rule.md`
- `Documentation/MCP_TOOLS_ARCHITECTURE.md`

## Preferred reasoning pattern

1. Confirm the failure through `run_generation_quality_suite` or `get_generation_stats`.
2. Inspect Basin-specific signals: `InlandWater`, `Basin`, `Coast`, `Dry`, average water ratio.
3. Distinguish a missing accumulation problem from a missing connectivity problem.
4. Prefer neighbor-aware or outlet-aware rules over adding another flat bonus.
5. Re-run the server suite before discussing Unity visuals.

## Healthy Basin signals

- inland water exists without collapsing into Ocean
- basin terrain class remains visible
- coast and inland water stay distinct
- drying or flooding does not become globally uniform

## Avoid

- solving Basin only by lowering a single threshold repeatedly
- introducing Ocean-like behavior to make Basin pass
- changing local Unity hydrology first when the failure is visible in projection stats