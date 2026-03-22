# ConverTool.PluginAbstractions v1.1.0（契约库）

> **受众**：插件开发者（NuGet 包 `ConverTool.PluginAbstractions`）。  
> **仓库**：[ConverTool-PluginAbstractions](https://github.com/kckc7887/ConverTool-PluginAbstractions)（与 Host **不同库**；本地目录名 **`PluginAbstractions/`** 见 [`repositories.md`](../repositories.md)）。  
> **Host 要求**：需使用实现了 **`visibleForTargetFormats`** 解析与 UI 规则的 Host 版本（本仓库 `Host` **v1.1.0** 与 [`plugin-dev.md`](../plugin-dev.md) 中契约版本 **1.1.0** 对齐）。

## 变更摘要

### 契约类型

- **`ConfigField`** 新增可选参数 **`VisibleForTargetFormatIds`**（`IReadOnlyList<string>?`）。
  - 语义：当列表**非空**时，Host 仅在**当前选中的目标格式 id** 与该列表中某项**相等**（忽略大小写）时显示该配置字段。
  - 与 JSON `manifest.json` 中 **`configSchema.sections[].fields[].visibleForTargetFormats`** 对应（见 [`plugin-dev.md`](../plugin-dev.md) §5 `visibleForTargetFormats`）。

### Host 行为（本仓库）

- `Host/PluginManifestModel.ConfigFieldModel` 增加 **`VisibleForTargetFormats`**。
- `MainWindowViewModel`：配置字段可见性在 **`RefreshTargetFormatsByVisibility()`** 之后计算；**`SelectedTargetFormat`** 变更时重新评估规则。

## 参考

- [`plugin-dev.md`](../plugin-dev.md) — `visibleForTargetFormats`、字段表  
- [`technical/IMPLEMENTATION.md`](../technical/IMPLEMENTATION.md) §10 — UI 规则引擎  
