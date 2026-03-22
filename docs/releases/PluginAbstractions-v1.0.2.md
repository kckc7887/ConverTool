# ConverTool.PluginAbstractions v1.0.2（契约库）

> **较新契约版本**：请见 **[PluginAbstractions-v1.1.0.md](./PluginAbstractions-v1.1.0.md)**（`visibleForTargetFormats` / `ConfigField.VisibleForTargetFormatIds`）。

> **受众**：插件开发者（面向 NuGet 包 `ConverTool.PluginAbstractions`）。  
> **仓库**：[ConverTool-PluginAbstractions](https://github.com/kckc7887/ConverTool-PluginAbstractions)（与 Host **不同库**；本地开发目录名 **`PluginAbstractions/`** 见 [`repositories.md`](../repositories.md)）。  
> **主发布**：以契约仓库内的 **`CHANGELOG.md`** / **`README.md`** 为准；本文件便于在 Host 文档索引中检索。

## 1.0.2 变更摘要

### 契约与 API

- **`ConfigFieldType`** 扩展 **`MultiSelect`**，与 manifest 中配置字段类型命名对齐，便于后续 Host 与插件在同一枚举上协作。  
  - **重要**：**Host v1.0.2 尚未实现** `MultiSelect` 的 UI 与值回传；**请勿**在已发布插件的 `manifest.json` 中使用，直至 Host 发行说明宣布支持（见主仓库 [`plugin-dev.md`](../plugin-dev.md)）。
- **`ExecuteContext`** 未改形状；Host 在 **`OutputNamingContext`** 中注入的键（如 `timeYmd`、`timeHms`）为 Host 行为扩展，插件可只读使用（与主仓库 v1.0.2 输出命名一致）。

### 文档与质量

- 类型注释与主仓库 `plugin-dev.md` 对齐说明；修正契约说明中的不一致表述。

### 升级指引

- 自 **1.0.1** 升级：若仅使用已有字段类型，**通常无需**改代码；若需引用 **`MultiSelect`** 枚举成员，请确认目标用户的 Host 版本已支持该类型后再在 manifest 中使用。
