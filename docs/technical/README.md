# ConverTool 技术文档索引

## 主文档（实现细节，持续更新）

| 文档 | 说明 |
|------|------|
| **[IMPLEMENTATION.md](./IMPLEMENTATION.md)** | **推荐首选**：Host 架构、插件加载与路由、`AssemblyLoadContext`、动态配置 UI、UI 规则引擎、`fieldPersistOverrides`、串行/并行批处理、暂停/取消、进度公式、落盘与命名模板、日志节流、用户设置路径、已知限制等；**§28** 为 Host 维护下的**插件兼容与版本策略**。**与当前源码一一对应。** |
| [../plugin-dev.md](../plugin-dev.md) | 插件作者向：`manifest.json` / `configSchema` **字段级**说明、`PluginAbstractions` 契约、打包与安装约定。 |

## 本页历史说明

此前本文件曾内联一份较长的架构说明，现已**收敛为索引**，避免与 `IMPLEMENTATION.md` 重复维护。若外部链接仍指向 `docs/technical/README.md`，请优先阅读 **[IMPLEMENTATION.md](./IMPLEMENTATION.md)**。

## 快速对照：源码路径

| 主题 | 主要文件 |
|------|-----------|
| 启动、插件 catalog | `Host/Program.cs`、`Host/AppServices.cs` |
| 插件扫描 / 路由 / Zip 安装 | `Host/Plugins/PluginCatalog.cs`、`PluginRouter.cs`、`PluginZipInstaller.cs` |
| ALC 加载 | `Host/PluginRuntimeLoader.cs` |
| Manifest DTO | `Host/PluginManifestModel.cs` |
| 主窗口逻辑 | `Host/ViewModels/MainWindowViewModel.cs` |
| 输入文件项 / 命名模板 Tag | `Host/ViewModels/InputFileItemVm.cs`、`NamingTemplateTokenVm.cs` |
| 配置控件 VM | `Host/ViewModels/ConfigFields.cs` |
| 插件管理窗口 | `Host/ViewModels/PluginManagerViewModel.cs` |
| 用户设置 | `Host/Settings/UserSettingsStore.cs` |
| 契约类型 | 独立仓库中的 `ConverTool.PluginAbstractions`（本地可为 `PluginAbstractions/`，见 [repositories.md](../repositories.md)） |
| 安装器 | `installer/`、`installer/README.md` |

## 安装器与发布

- 脚本与 Inno 资源：`installer/`  
- 使用说明：同目录下 **`installer/README.md`**（若存在）。
- **维护者发版**：`installer/README.md`（安装包、`package-portable-zips.ps1` 等）。
