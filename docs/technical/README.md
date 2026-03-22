# ConverTool 技术文档索引

## 主文档

| 文档 | 说明 |
|------|------|
| **[IMPLEMENTATION.md](./IMPLEMENTATION.md)** | Host 架构、插件加载与路由、`AssemblyLoadContext`、动态配置 UI、**`config.json` / `SettingManager` / `persistValue`**、UI 规则引擎、串行/并行批处理、进度公式、落盘与命名模板等 |
| [../plugin-dev.md](../plugin-dev.md) | 插件开发规范：manifest 字段、i18n、configSchema、共享工具缓存 |

---

## 快速对照：源码路径

| 主题 | 主要文件 |
|------|-----------|
| 启动、插件 catalog | `Host/Program.cs`、`Host/AppServices.cs`、`Host/Services/ServiceLocator.cs` |
| 插件扫描 / 路由 / Zip 安装 | `Host/Plugins/PluginCatalog.cs`、`PluginRouter.cs`、`PluginZipInstaller.cs` |
| ALC 加载 | `Host/PluginRuntimeLoader.cs` |
| Manifest DTO | `Host/PluginManifestModel.cs` |
| 主窗口逻辑 | `Host/ViewModels/MainWindowViewModel.cs` |
| 输入文件项 / 命名模板 | `Host/ViewModels/InputFileItemVm.cs`、`InputFileViewModel.cs`、`NamingTemplateTokenVm.cs`、`NamingTemplateViewModel.cs` |
| 配置控件 VM | `Host/ViewModels/ConfigFields.cs` |
| 插件管理窗口 | `Host/ViewModels/PluginManagerViewModel.cs` |
| 用户设置 | `Host/Settings/UserSettingsStore.cs` |
| Host / 插件 `config.json` | `Host/Settings/SettingManager.cs` |
| 共享工具缓存 | `PluginAbstractions/SharedToolCache.cs` |
| 契约类型 | 独立仓库中的 `ConverTool.PluginAbstractions` |
| 安装器 | `installer/` |

---

## 版本信息

| 组件 | 版本 |
|------|------|
| Host | v1.1.0 |
| PluginAbstractions | 1.1.0 |

单元测试（`persistValue` 等）：`Host.Tests/`（`dotnet test Host.Tests/Host.Tests.csproj`）。

---

## 相关链接

- **Host 仓库**：<https://github.com/kckc7887/ConverTool>
- **契约仓库**：<https://github.com/kckc7887/ConverTool-PluginAbstractions>
