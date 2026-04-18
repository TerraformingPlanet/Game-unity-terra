---
name: terraformation-docker-smoke-validation
description: Guide AI Assistant through dedicated-server Docker rebuild and automated generation/smoke validation with explicit pass-fail outcomes.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation Docker Smoke Validation

Use this skill when the task is to validate a dedicated-server change end to end after editing generation, MCP, or simulation code.

## Goal

Treat Docker validation as a repeatable gate, not an informal check.

## Required references

- `docker-compose.yml`
- `.vscode/tasks.json`
- `.github/workflows/generation-smoke.yml`
- `.github/workflows/mcp-http-probe.yml`
- `DedicatedServer/Dockerfile`
- `DedicatedServer/app/generation_smoke.py`
- `DedicatedServer/app/compare_generation_runs.py`
- `Mcp/ci_mcp_http_probe.py`
- `Tools/Test-GenerationQuality.ps1`
- `Tools/Invoke-DedicatedServerGenerationSmoke.ps1`
- `Tools/Invoke-TerraformationDebugSmokeTest.ps1`
- `Documentation/MCP_TOOLS_ARCHITECTURE.md`

## Preferred order

1. Rebuild `terraformation-dedicated-server` with Docker Compose.
2. Run `run_generation_quality_suite` or `Tools/Test-GenerationQuality.ps1`.
3. Prefer the Compose smoke profile or CI workflow when you need a Linux-native, PowerShell-free gate.
4. Use the MCP HTTP probe workflow when you need to validate the real FastMCP session flow, not just Python imports.
5. When comparing two runs, use the diffable JSON outputs and `compare_generation_runs.py` instead of eyeballing tables.
6. Only if needed, run Unity-side smoke checks for preset visuals and console state.
7. Report exact failing checks, not vague regression summaries.

## Working rules

1. Keep the dedicated-server checks separate from Unity Play Mode checks.
2. If the container is restarting or unavailable, fix infra first before analyzing preset behavior.
3. Prefer explicit exit-code-based validation.
4. Preserve deterministic seeds when comparing runs.

## Avoid

- mixing Docker failures with gameplay conclusions
- claiming a fix without rerunning the server validation suite
- launching Unity just to compensate for a missing dedicated-server check