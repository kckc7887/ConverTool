# ConverTool Installer (维护者 / 从源码构建)

> **给最终用户：** 正式版的 `setup.exe` 已随 **[GitHub Releases](https://github.com/kckc7887/ConverTool/releases)** 发布，**无需**克隆仓库或运行本目录下的脚本即可安装。  
> 只有在你需要**本地修改安装器**或**自行重打安装包**时，才需要按下文构建。

本目录为 Inno Setup 工程（版本号见 `ConverTool.iss` / `build-installer.ps1` 中的参数，当前与主版本一致）。**契约 NuGet** `ConverTool.PluginAbstractions` 的版本以 **`docs/repositories.md`** 与根目录 **`PluginAbstractions/PluginAbstractions.csproj`** 为准（当前 **1.1.0**），与安装包版本号可独立演进。

## Prerequisites

- Windows
- .NET SDK 8
- Inno Setup 6 (`ISCC.exe`)
- Python 3 + Pillow (`pip install Pillow`) — used to rebuild `installer/Assets/uninstall.ico` from `uninstall.png`

## Build

From repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\scripts\build-installer.ps1
```

## 联调构建（Host + 契约 + `plugins-src` 示例）

已按 **`docs/repositories.md`** §8 将契约仓库克隆到仓库根下 **`PluginAbstractions/`** 时，可在仓库根执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\scripts\verify-dev-setup.ps1
```

## Output

- `artifacts/ConverTool-v<版本>-setup.exe`（例如与 `AppVersion` 为 `1.1.0` 时对应 `ConverTool-v1.1.0-setup.exe`）
- 若还需打 **full/lite 便携 zip**（在已执行 `build-installer.ps1` 生成 `artifacts/host/v<版本>/...` 之后）：

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\scripts\package-portable-zips.ps1 -Version <版本>
```

## 安装包图标在资源管理器里没变？

常见原因：

1. **Windows 图标缓存**：资源管理器会长期缓存 `.exe` 图标。可尝试：
   - 把 `setup.exe` **复制到新路径/改个文件名**再查看；或
   - 结束 `explorer.exe` 后重新启动，或注销/重启；或
   - 清理图标缓存（网上可查 “重建 Windows 图标缓存”）。
2. **UAC 盾牌叠加**：若安装包请求管理员权限，系统可能在图标上叠一层盾牌，看起来像“默认安装程序”。
3. **看的是旧文件**：确认打开的是 `artifacts` 里**最新编译时间**的 `ConverTool-v*-setup.exe`。

## 卸载图标（垃圾桶）

- **“设置 → 应用”列表** 与 **开始菜单里的“卸载 ConverTool”快捷方式** 会使用 `uninstall.ico`（由 `Assets/uninstall.png` 生成）。
- **`unins000.exe` 文件本身** 的图标仍由 Inno 的 `SetupIconFile` 决定（与安装包相同），这是 Inno Setup 的限制；若也要改该 exe 的图标，需要额外用资源编辑工具改 PE，不建议纳入常规构建。

## Installer behavior

- Bundles both `full` and `lite` core payloads.
- Detects local .NET 8 Desktop Runtime:
  - found => defaults to `lite`
  - not found => defaults to `full`
- User can still manually select `full` or `lite`.
- Plugin components are selectable and checked by default:
  - FFmpeg Video Transcoder
  - ImageMagick Image Transcoder
