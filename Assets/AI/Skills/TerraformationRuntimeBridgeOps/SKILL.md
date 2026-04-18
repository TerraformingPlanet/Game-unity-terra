---
name: terraformation-runtime-bridge-ops
description: Guide AI Assistant to use Terraformation's runtime debug bridge, smoke test script, and MCP adapter in a deterministic and tool-first workflow.
required_packages:
  com.unity.ai.assistant: 2.5.0-pre.2
enabled: true
---

# Terraformation Runtime Bridge Operations

Use this skill when the task is to query the running game, launch a preset, open a region, collect console state, capture screenshots, or drive the debug workflow through the runtime bridge and MCP adapter.

## Goal

Use the project's deterministic tooling first.

Prefer the runtime bridge, smoke test script, and MCP adapter over ad hoc manual inspection whenever the task needs runtime state.

## Runtime stack summary

- HTTP bridge base URL: `http://127.0.0.1:48621`
- smoke test script: `Tools/Invoke-TerraformationDebugSmokeTest.ps1`
- MCP adapter: `Tools/TerraformationDebugMcp.ps1`
- workspace MCP server id: `terraformation-debug`

## Preconditions

1. Unity must be in Play mode.
2. The debug HTTP server must be started.
3. `/debug/state` should respond before deeper actions.

## Preferred runtime order

1. query current state
2. launch preset if needed
3. read projection summary
4. open region if needed
5. read local summary
6. read console
7. capture screenshot only if useful

## Endpoints to rely on

- `/debug/state`
- `/debug/launch-preset`
- `/debug/projection`
- `/debug/open-region`
- `/debug/local`
- `/debug/console`
- `/debug/screenshot`

## Smoke test usage

Use the smoke test script when the task is to validate a preset end to end and collect artifacts.

Example flow:

- run the PowerShell smoke test with a named preset
- inspect the generated `verdict`
- use projection and local artifacts to explain the pass or fail

## MCP usage

Use the MCP adapter when a client needs structured tool calls instead of raw HTTP.

Currently exposed tools:

- `get_current_view_state`
- `launch_preset`
- `get_projection_summary`
- `open_region`
- `get_local_summary`
- `get_recent_console_errors`
- `capture_scene_screenshot`

## Avoid

- assuming runtime state without checking `/debug/state`
- calling a preset valid from visuals only
- treating bridge unavailability as a gameplay bug before verifying Play mode and server startup
