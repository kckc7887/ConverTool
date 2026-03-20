# Plugin Workspace (Unified)

This folder is the single source location for writing plugins.

## Structure

- `plugins-src/<PluginProject>/`：插件源码（`.csproj`、`manifest.json`、`locales/` 等）。**本仓库内置插件**的 `manifest.json` 中 **`version` 与 Host 发行版一致**（与 `Host/Host.csproj` 的 `<Version>` 同号），见 **`docs/plugin-dev.md`** §3.1。
- `plugins-dist/`：构建产出的 **每插件一个 zip**（默认 `plugins-dist/<pluginId>.zip`）。
- 仓库中随 Host 发布的内置插件副本见 **`Host/plugins/<pluginId>/`**（由维护者同步，非本脚本默认输出目录）。

## One-command workflow

在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\plugins-src\build-and-sync.ps1
```

脚本行为：

1. 扫描 `plugins-src/` 下各插件的 `manifest.json`。
2. 以 **Release** 配置逐个 `dotnet build`。
3. 将产物同步到 **默认** 的 Host 输出目录 **`Host/bin/Debug/net8.0/plugins/<pluginId>/`**（可用参数 `-HostPluginsDir` 改为例如 `Host/bin/Release/net8.0/plugins`）。
4. 在 `plugins-dist/` 下生成（或覆盖）**`plugins-dist/<pluginId>.zip`**，并清理同插件的旧 zip 变体。
