# Plugin Workspace (Unified)

This folder is the single source location for writing plugins.

## Structure

- `plugins-src/<PluginProject>/` : plugin source code (`.csproj`, `manifest.json`, `locales/`, code).
- `plugins-dist/` : built plugin zip outputs (one zip per plugin).
- `plugins/` : Host runtime plugin folders (synced automatically after build).

## One-command workflow

From repo root:

`powershell -ExecutionPolicy Bypass -File .\plugins-src\build-and-sync.ps1`

What it does:

1. Scans all plugin `manifest.json` files under `plugins-src/`.
2. Builds each plugin in `Release`.
3. Syncs each plugin to `plugins/<pluginId>/` (for direct Host testing).
4. Recreates a single zip file per plugin in `plugins-dist/`:
   - `plugins-dist/<pluginId>.zip`
5. Removes older zip variants of the same plugin automatically.
