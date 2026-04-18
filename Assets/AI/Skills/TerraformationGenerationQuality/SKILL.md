---
name: terraformation-generation-quality
description: Help AI Assistant tune and validate Terraformation dedicated-server generation using generation stats, the quality suite, and preset-specific thresholds before opening Unity.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation Generation Quality

Use this skill when the task concerns H3 projection tuning, preset balance, generation regressions, or validation of `logic.py` changes on the dedicated server.

## Goal

Keep server-side generation tuning deterministic and measurable.

Prefer MCP tools and dedicated-server debug metrics over visual inspection first.

## Required references

- `SimulationCore/terraformation_sim/logic.py`
- `DedicatedServer/app/server.py`
- `Mcp/server.py`
- `Documentation/ROADMAP.md`
- `Documentation/MCP_TOOLS_ARCHITECTURE.md`
- `Tools/Test-GenerationQuality.ps1`

## Preferred tools

- `get_generation_stats`
- `get_generation_noise_distribution`
- `run_generation_quality_suite`
- `compare_generation_profiles`
- `compare_presets`
- `diagnose_hydrology_mismatch`

## Recommended order

1. Run `run_generation_quality_suite` after any generation change.
2. If a preset fails, inspect `get_generation_stats` for the failing preset.
3. Use `get_generation_noise_distribution` only if the failure looks threshold-driven or hash-distribution-driven.
4. Use `compare_generation_profiles` if two presets seem too close at the server level.
5. Open Unity only after the dedicated-server metrics are credible.

## Healthy preset heuristics

### Coast

- coast must remain present
- vegetation must not collapse to zero

### Ocean

- open ocean should dominate
- dry tiles should stay low

### Arid

- dry should dominate clearly
- vegetation should remain limited

### Frozen

- cold or frozen signatures must remain obvious

### Basin

- inland water and basin signatures must remain distinguishable from Ocean

## Avoid

- accepting a generation change from screenshots alone
- retuning several presets blindly without re-running the quality suite
- using Unity smoke tests as the first validation layer for server-only tuning