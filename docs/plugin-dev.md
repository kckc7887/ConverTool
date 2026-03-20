# ConverTool 插件开发规范（与 Host v1.0.2 对齐）

本规范说明 ConverTool **Host** 与插件之间的协议：**文件型 `manifest.json`** 由 Host 解析并驱动 UI；**`PluginAbstractions` 契约包**定义 C# 类型，供插件实现 `IConverterPlugin` 与读取 `ExecuteContext` 等。二者字段应对齐；若仅用 JSON 部署插件，以本文 **manifest / configSchema** 为准。

> **仓库边界**：**Host 应用**与 **契约库源码** 分属**两个 Git 仓库**。  
> - **只开发独立插件**：在自有工程里用 **NuGet** 引用 `ConverTool.PluginAbstractions` 即可，**不必** clone 本仓库。  
> - **要联调本仓库中的 Host / `plugins-src`**：必须同时 clone Host 与契约库，且契约库目录名须为 `PluginAbstractions`（见 **[repositories.md](./repositories.md)** §8）。

**Host 侧**对「旧插件能否在新 Host 上跑」的约定（无版本协商、JSON 与硬性校验、契约加载）见技术文档 **[`docs/technical/IMPLEMENTATION.md` §28](technical/IMPLEMENTATION.md)**（面向 Host 维护者；插件作者亦建议了解硬性门槛）。

---

## 目录

1. [插件工程依赖（契约包）](#1-插件工程依赖契约包)  
2. [核心职责与执行边界](#2-核心职责与执行边界)  
3. [目录约定与扫描规则](#3-目录约定与扫描规则)（含 [3.1 自带插件与 manifest.version](#31-自带插件与-manifestversion本仓库)）  
4. [`manifest.json` 完整字段参考](#4-manifestjson-完整字段参考)  
5. [i18n 语言包](#5-i18n-语言包)  
6. [`configSchema` 完整字段参考](#6-configschema-完整字段参考)  
7. [`PluginAbstractions` 类型与 manifest 的对应关系](#7-pluginabstractions-类型与-manifest-的对应关系)  
8. [打包与安装](#8-打包与安装)  
9. [最小代码示例](#9-最小代码示例)  
10. [常见错误清单](#10-常见错误清单)  

---

## 1. 插件工程依赖（契约包）

插件需引用 **`ConverTool.PluginAbstractions`**（与 Host 配套的版本，当前文档：**1.0.2**）。契约库为**独立仓库**维护，与 Host **不同库**；你作为插件作者应**始终通过 NuGet（或团队提供的 `.nupkg`）**引用，不要依赖 Host 仓库里的源码路径。

| 项 | 说明 |
|----|------|
| **包名** | `ConverTool.PluginAbstractions` |
| **用途** | 定义 `IConverterPlugin`、`ExecuteContext`、`PluginManifest` 等，插件**必须**实现接口并引用该程序集（程序集名仍为 `PluginAbstractions.dll`）。 |
| **NuGet** | 以团队发布为准：公共源（如 [nuget.org](https://www.nuget.org/packages/ConverTool.PluginAbstractions)）、GitHub Packages、Release 中的 `.nupkg` 等；仓库与发版边界见 **[repositories.md](./repositories.md)**。 |
| **说明** | 使用 GitHub Packages 时通常需配置带 `read:packages` 的 Token。 |

实现：`IConverterPlugin`，并提供与插件目录同级的 **`manifest.json`**（Host 实际加载的是磁盘上的 JSON，而非仅依赖 `GetManifest()` 的返回值；`GetManifest()` 在部分流程中可用于校验，**以 JSON manifest 为准**）。

---

## 2. 核心职责与执行边界

| 角色 | 职责 |
|------|------|
| **Host** | 扫描 `plugins/**/manifest.json`；按 `configSchema` 渲染配置；按输入扩展名匹配插件；为每次任务提供 `TempJobDir`；任务结束后根据插件上报路径移动/重命名产物；映射进度到总进度条。 |
| **插件** | 实现 `IConverterPlugin`；在 `ExecuteAsync` 内完成转换；**仅**在 `TempJobDir` 内写入输出；通过 `IProgressReporter` 上报日志、进度、成功或失败。 |

---

## 3. 目录约定与扫描规则

**扫描路径（递归）：** `AppContext.BaseDirectory/plugins/**/manifest.json`

对每个 `manifest.json`：

| 推导项 | 规则 |
|--------|------|
| 插件根目录 | `manifest.json` 所在目录 |
| 托管 DLL | `<插件根目录>/<manifest.assembly 字段值>` |
| 入口类型 | `manifest.type`（完整类型名，用于反射创建实例） |

**推荐目录结构：**

```text
plugins/
  <pluginId>/
    manifest.json
    <YourPlugin>.dll
    locales/
      en-US.json
      zh-CN.json
```

---

## 4. `manifest.json` 完整字段参考

根对象中，JSON **键名**与下表一致（区分大小写处会注明）。

### 4.1 根级字段一览

| 字段名 | 必需 | 类型 | 说明 |
|--------|------|------|------|
| **`pluginId`** | ✅ | 字符串 | 插件唯一 ID。建议与 `plugins/` 下文件夹名一致；Host 安装 zip 时会安装到 `plugins/<pluginId>/`。 |
| **`version`** | ✅ | 字符串 | 语义化或任意版本号字符串，供展示或区分构建；Host 不据此做兼容性裁决。自带插件见 [3.1](#31-自带插件与-manifestversion本仓库)。 |
| **`assembly`** | ✅ | 字符串 | 插件主 DLL **文件名**（相对插件根目录），如 `MyPlugin.dll`。 |
| **`type`** | ✅ | 字符串 | 实现 `IConverterPlugin` 的类型的**完整名称**（命名空间.类名），用于 `Assembly` 内反射 `Activator.CreateInstance`。 |
| **`supportsTerminationOnCancel`** | ✅ | 布尔 | **必须为 `true`**。声明插件在收到取消时能否终止其启动的外部进程。若为 `false` 或缺失，**Host 会拒绝加载该插件**。 |
| **`supportedInputExtensions`** | ✅ | `string[]` | 该插件能处理的**输入文件扩展名**列表。**不要**包含点号，使用小写惯例，如 `"mkv"`、`"jpg"`。Host 会做规范化与大小写不敏感匹配。 |
| **`supportedTargetFormats`** | ✅ | 数组 | **至少 1 项**。用户可选的输出格式列表，见 [4.2](#42-supportedtargetformats-每一项字段)。 |
| **`configSchema`** | ❌ | 对象 | 配置 UI 结构；缺失、`null` 或 `sections` 为空时，不渲染配置项。结构见 [第 6 节](#6-configschema-完整字段参考)。 |
| **`supportedLocales`** | ❌ | `string[]` | 声明插件提供的语言包区域标识（如 `en-US`、`zh-CN`）。当前 Host **不强校验**，仅作文档/展示用途。 |
| **`i18n`** | ❌ | 对象 | 语言包位置，见 [4.3](#43-i18n-对象字段)。 |
| **`titleKey`** | ❌ | 字符串 | 插件在「插件管理」等处的**标题**所用 i18n 键。 |
| **`descriptionKey`** | ❌ | 字符串 | 插件**描述**所用 i18n 键。 |
| **`author`** | ❌ | 字符串 | 作者名；Host 解析并保留，是否展示取决于 UI 版本。 |

### 4.2 `supportedTargetFormats` 每一项字段

| 字段名 | 必需 | 类型 | 说明 |
|--------|------|------|------|
| **`id`** | ✅ | 字符串 | 目标格式标识，写入 `ExecuteContext.TargetFormatId`。应稳定、简短，如 `mp4`、`png`。 |
| **`displayNameKey`** | ✅ | 字符串 | 下拉列表等处显示的**名称**的 i18n 键。 |
| **`descriptionKey`** | ❌ | 字符串 | 可选的详细说明 i18n 键（若 Host UI 支持展示）。 |
| **`visibleIf`** | ❌ | 对象 | 若存在，则**仅当**条件满足时，该目标格式出现在下拉里。结构与配置字段的 `visibleIf` 相同，见 [4.4](#44-visibleif-对象字段共用)。用于按布尔配置项动态隐藏不适用的格式（例如启用某模式时隐藏无损格式）。 |

### 4.3 `i18n` 对象字段

| 字段名 | 必需 | 类型 | 说明 |
|--------|------|------|------|
| **`localesFolder`** | ❌ | 字符串 | 语言文件所在**子目录名**（相对插件根目录）。**缺省为 `"locales"`**。实际路径：`<pluginDir>/<localesFolder>/<locale>.json`。 |

### 4.4 `visibleIf` 对象（字段共用）

用于 **`configSchema` 内字段** 与 **`supportedTargetFormats[]` 项**。

| 字段名 | 类型 | 说明 |
|--------|------|------|
| **`fieldKey`** | 字符串 | 作为条件的**布尔配置项**的 `key`（必须为 `Checkbox` 类型字段的 `key`）。 |
| **`equals`** | 布尔 | 当该复选框的取值**等于**此值时，条件成立（显示字段或显示目标格式）。 |

Host 根据当前 UI 中 `fieldKey` 对应复选框的勾选状态判断是否显示。

### 4.5 最小可用 `manifest.json` 示例

```json
{
  "pluginId": "demo.sample",
  "version": "0.1.0",
  "assembly": "DemoSamplePlugin.dll",
  "type": "DemoSamplePlugin.SamplePlugin",
  "supportsTerminationOnCancel": true,
  "supportedInputExtensions": ["mkv"],
  "supportedTargetFormats": [
    { "id": "mp4", "displayNameKey": "plugin/demo.sample/target/mp4" }
  ],
  "configSchema": { "sections": [] },
  "i18n": { "localesFolder": "locales" }
}
```

---

## 5. i18n 语言包

**文件路径：** `plugins/<pluginId>/<localesFolder>/<locale>.json`  

**文件结构：**

```json
{
  "strings": {
    "<i18n-key>": "<显示文本>"
  }
}
```

| 项 | 说明 |
|----|------|
| **`strings`** | 根对象下必须包含 `strings` 对象（与 Host 加载约定一致）。 |
| **键** | 与 manifest 中 `labelKey`、`helpKey`、`titleKey`、`displayNameKey` 等引用一致。 |
| **回退** | 缺失文件或缺失 key 不崩溃；非 `en-US` 常回退到 `en-US`；仍找不到则界面可能直接显示 key 字符串。 |

---

## 6. `configSchema` 完整字段参考

`configSchema` 决定配置分区、控件类型、默认值、显隐条件，以及 Host 写入用户设置时的扩展规则。

### 6.1 `configSchema` 根对象

| 字段名 | 必需 | 类型 | 说明 |
|--------|------|------|------|
| **`sections`** | ❌* | 数组 | 配置分区列表。*若缺失或为空数组，则不展示任何配置字段。* |
| **`fieldBoolRelations`** | ❌ | 数组 | 复选框之间的联动规则，见 [6.5](#65-fieldboolrelations-每一项字段)。 |
| **`fieldPersistOverrides`** | ❌ | 数组 | 持久化快照覆盖规则（契约 **1.0.1+**），见 [6.6](#66-fieldpersistoverrides-每一项字段)。 |

### 6.2 `sections[]` 每一项（配置分区）

| 字段名 | 必需 | 类型 | 说明 |
|--------|------|------|------|
| **`id`** | ✅ | 字符串 | 分区唯一标识，用于区分逻辑块；Host 可用于内部状态或将来扩展。 |
| **`titleKey`** | ❌ | 字符串 | 分区标题的 i18n 键；`null` 或省略时可无标题或使用默认行为（取决于 Host UI）。 |
| **`descriptionKey`** | ❌ | 字符串 | 分区说明文字 i18n 键。 |
| **`collapsedByDefault`** | ❌ | 布尔 | `true` 表示该分区默认折叠；`false` 或未设置表示默认展开（以 Host 实现为准）。 |
| **`fields`** | ✅ | 数组 | 该分区下的配置项列表，见 [6.3](#63-fields-每一项通用字段)。 |

### 6.3 `fields[]` 每一项（通用字段）

所有配置项均包含以下通用字段；**`type` 决定还需要哪些额外字段**。

| 字段名 | 必需 | 类型 | 说明 |
|--------|------|------|------|
| **`key`** | ✅ | 字符串 | 配置键。**执行时**会进入 `ExecuteContext.SelectedConfig[key]`；必须唯一、稳定，建议 ASCII（如 `crf`、`enableFoo`）。 |
| **`type`** | ✅ | 字符串 | 控件类型，**大小写须与下表完全一致**：`Text`、`Checkbox`、`Select`、`Range`、`Number`、`Path`。不支持的类型可能被当作 `Text` 或无法正确渲染。 |
| **`labelKey`** | ✅ | 字符串 | 标签文案的 i18n 键。 |
| **`helpKey`** | ❌ | 字符串 | 帮助/提示文案 i18n 键。 |
| **`defaultValue`** | ❌ | JSON | 初始默认值；解析规则依 `type` 而定，见 [6.4](#64-按-type-区分的专有字段与-selectedconfig-值)。 |
| **`visibleIf`** | ❌ | 对象 | 条件显示；结构见 [4.4](#44-visibleif-对象字段共用)。未指定时字段始终参与布局（仍受 Host 规则引擎处理）。 |

### 6.4 按 `type` 区分的专有字段与 `SelectedConfig` 值

以下 **`type` 值**为 Host 当前支持的字符串（区分大小写）。

#### `Text`

| 专有字段 | 说明 |
|----------|------|
| 无 | — |
| **`defaultValue`** | 任意 JSON 标量会转为字符串；缺省为 `""`。 |
| **`SelectedConfig[key]`** | `string` |

#### `Checkbox`

| 专有字段 | 说明 |
|----------|------|
| 无 | — |
| **`defaultValue`** | `true`/`false` 或可解析为布尔的字符串；缺省或无法解析为 `false`。 |
| **`SelectedConfig[key]`** | `bool` |

#### `Select`

| 专有字段 | 说明 |
|----------|------|
| **`options`** | **必需**。数组，每一项见 [6.4.1](#641-options-每一项)。 |
| **`defaultValue`** | 字符串，须与某个 `options[].id` 一致；否则由 Host 决定首项或空行为。 |
| **`SelectedConfig[key]`** | `string`（选中项的 `id`） |

##### 6.4.1 `options` 每一项

| 字段名 | 说明 |
|--------|------|
| **`id`** | 选项值，写入 `SelectedConfig`。 |
| **`labelKey`** | 该项显示文字的 i18n 键。 |

#### `Range`

| 专有字段 | 说明 |
|----------|------|
| **`range`** | **必需**。见 [6.4.2](#642-range-对象)。 |
| **`defaultValue`** | 数字或可解析字符串；无效时通常回退为 `range.min`。 |
| **`SelectedConfig[key]`** | `string`（内部数值的文本表示，与 Host 控件一致） |

#### `Number`

| 专有字段 | 说明 |
|----------|------|
| **`range`** | **必需**。见 [6.4.2](#642-range-对象)。 |
| **`defaultValue`** | 数字或可解析字符串；无效时通常回退为 `range.min`。 |
| **UI** | 数字输入 + 步进按钮（Spinbox 风格）。 |
| **`SelectedConfig[key]`** | `string`（数值的文本表示，与内部小数/格式一致） |

##### 6.4.2 `range` 对象

| 字段名 | 类型 | 说明 |
|--------|------|------|
| **`min`** | 数字 | 允许的最小值。 |
| **`max`** | 数字 | 允许的最大值。 |
| **`step`** | 数字 | 步长；缺省一般为 `1`。 |

#### `Path`

| 专有字段 | 说明 |
|----------|------|
| **`path`** | **必需**。见 [6.4.3](#643-path-对象)。 |
| **`defaultValue`** | 初始路径字符串。 |
| **`SelectedConfig[key]`** | `string`（用户选择或输入的**绝对路径**） |

##### 6.4.3 `path` 对象

| 字段名 | 类型 | 说明 |
|--------|------|------|
| **`kind`** | 字符串 | `"File"` 或 `"Folder"`，决定浏览对话框类型。 |
| **`mustExist`** | 布尔 | 语义上表示是否必须已存在；**Host 可能不强制校验**，仅作约定。 |

#### 不支持的类型

| `type` | 说明 |
|--------|------|
| **`MultiSelect`** | 契约 **`ConfigFieldType`** 已包含该值（**PluginAbstractions 1.0.2+**），但 **Host v1.0.2 仍未实现**对应 UI 与值回传；manifest 中 **请勿使用**。待 Host 支持后再解除限制。 |

### 6.5 `fieldBoolRelations` 每一项字段

用于在用户**改变**某个驱动复选框时，自动设置另一个复选框（即时 UI 规则）。

| 字段名 | 类型 | 说明 |
|--------|------|------|
| **`if`** | 对象 | 条件，与 `visibleIf` 相同：`fieldKey` + `equals`（布尔）。 |
| **`then`** | 对象 | 满足条件时对目标字段赋值，见下表。 |
| **`applyWhen`** | 字符串 | 可选。若为 `"save"`，设计意图为仅在保存用户设置时应用；**当前 Host 实现中不会执行仅标记为 `save` 的联动**，需要「持久化与当前会话分离」时请优先使用 `fieldPersistOverrides`。 |

**`then` 对象：**

| 字段名 | 说明 |
|--------|------|
| **`targetKey`** | 被修改的布尔字段的 `key`。 |
| **`value`** | 要设置成的布尔值。 |

### 6.6 `fieldPersistOverrides` 每一项字段

用于：**当前会话**中 UI 可保持一组值，但**写入用户设置文件**时用另一组值，使**下次启动**恢复为指定默认（例如「未勾选保留」时仍可在会话内保持「启用」，但磁盘上保存「未启用」）。

| 字段名 | 类型 | 说明 |
|--------|------|------|
| **`when`** | 对象 | 条件，同 `visibleIf`：`fieldKey` + `equals`。Host 根据**当前 UI** 判断。 |
| **`fields`** | 对象 | 键为配置项 `key`，值为要**写入持久化快照**的 JSON 值（类型需与该字段控件一致，如布尔、数字、字符串）。 |

**Host 行为摘要：**

1. **保存用户设置**：若 `when` 成立，先用当前 UI 生成字段快照，再对 `fields` 中列出的键做覆盖，再写入磁盘。  
2. **从磁盘恢复后**：若 `when` 成立，对内存中控件再应用 `fields`（用于兼容旧配置或与快照一致）。

### 6.7 `Path` 字段完整示例

```json
{
  "key": "subtitlePath",
  "type": "Path",
  "labelKey": "plugin/demo.sample/field/subtitlePath/label",
  "helpKey": "plugin/demo.sample/field/subtitlePath/help",
  "defaultValue": "",
  "path": { "kind": "File", "mustExist": true }
}
```

### 6.8 `fieldPersistOverrides` 示例

```json
"fieldPersistOverrides": [
  {
    "when": { "fieldKey": "retainTargetSizeCompressionSettings", "equals": false },
    "fields": { "enableTargetSizeCompression": false }
  }
]
```

---

## 7. `PluginAbstractions` 类型与 manifest 的对应关系

磁盘 **`manifest.json` 比** `GetManifest()` **更全**（例如 `fieldBoolRelations`、`fieldPersistOverrides`、`Number`、`visibleIf` 等多在 JSON 中声明）。插件代码中的 `PluginManifest` / `ConfigSchema` 可与 JSON 并行维护，或仅作占位。

### 7.1 `IConverterPlugin`

| 成员 | 说明 |
|------|------|
| **`GetManifest()`** | 返回 `PluginManifest`；Host 以 **JSON** 为准加载插件时，此结果主要用于兼容或校验。 |
| **`ExecuteAsync(ExecuteContext, IProgressReporter, CancellationToken)`** | 执行单次转换；必须响应取消并终止子进程（若 `supportsTerminationOnCancel` 为 true）。 |

### 7.2 `PluginManifest`（记录类型参数顺序与含义）

| 参数 | 说明 |
|------|------|
| **`PluginId`** | 对应 `manifest.pluginId`。 |
| **`Version`** | 对应 `manifest.version`。 |
| **`SupportedInputExtensions`** | 对应 `supportedInputExtensions`。 |
| **`SupportedTargetFormats`** | 对应 `supportedTargetFormats` 的简化模型（见 `TargetFormat`）。 |
| **`ConfigSchema`** | 对应 `configSchema` 的简化模型（`ConfigSection`、`ConfigField`）；**JSON 中的额外键**以 Host 反序列化为准。 |
| **`SupportedLocales`** | 对应 `supportedLocales`。 |
| **`I18n`** | 对应 `i18n`（`I18nDescriptor`）。 |

### 7.3 `TargetFormat`（代码）

| 字段 | 对应 JSON |
|------|-----------|
| **`Id`** | `id` |
| **`DisplayNameKey`** | `displayNameKey` |
| **`DescriptionKey`** | `descriptionKey` |

JSON 中的 **`visibleIf`** 无对应记录字段，仅在 manifest JSON 中声明。

### 7.4 `ConfigSchema`（代码）

| 字段 | 说明 |
|------|------|
| **`Sections`** | `sections[]` |
| **`FieldPersistOverrides`** | 可选；对应 `fieldPersistOverrides`（`FieldPersistOverrideRule`）。 |

### 7.5 `ConfigSection`（代码）

| 字段 | 对应 JSON |
|------|-----------|
| **`Id`** | `id` |
| **`TitleKey`** | `titleKey` |
| **`DescriptionKey`** | `descriptionKey` |
| **`CollapsedByDefault`** | `collapsedByDefault` |
| **`Fields`** | `fields` |

### 7.6 `ConfigField`（代码）

| 字段 | 说明 |
|------|------|
| **`Key`** | `key` |
| **`Type`** | `ConfigFieldType` 枚举；与 JSON 字符串 `type` 命名不完全一致时以 **JSON** 为准。 |
| **`LabelKey`** | `labelKey` |
| **`HelpKey`** | `helpKey` |
| **`DefaultValue`** | `defaultValue` |
| **`Options`** | `Select` 的 `options` |
| **`Range`** | `Range`/`Number` 的 `range` |
| **`Path`** | `Path` 的 `path` |

代码中的 `ConfigField` **不含** `visibleIf`；条件显隐仅在 **manifest JSON** 中声明。

### 7.7 `ExecuteContext`

| 字段 | 类型 | 说明 |
|------|------|------|
| **`JobId`** | `string` | 当前任务 ID，便于日志关联。 |
| **`InputPath`** | `string` | 输入文件**绝对路径**。 |
| **`TempJobDir`** | `string` | 本次任务临时目录**绝对路径**；**所有输出必须写在该目录下**。 |
| **`TargetFormatId`** | `string` | 用户选择的 `supportedTargetFormats[].id`。 |
| **`SelectedConfig`** | `IReadOnlyDictionary<string, object?>` | 配置 `key` → 值；类型与控件一致（如布尔、字符串）。 |
| **`Locale`** | `string` | 当前 UI 区域标识（如 `zh-CN`）。 |
| **`OutputNamingContext`** | `IReadOnlyDictionary<string, object?>` | Host 提供的输出命名相关上下文；**可选读取**。**v1.0.2+** 典型键：`base`（字符串，无主扩展名）、`index`（int）、`ext`（目标格式 id）、`timeYmd` / `timeHms`（字符串，与落盘占位符格式一致：`yyyy-MM-dd`、`HH-mm-ss`）。 |

### 7.8 `IProgressReporter` 与相关记录

| 类型 | 字段 | 说明 |
|------|------|------|
| **`ProgressInfo`** | **`Stage`** | `Preparing` / `Running` / `Finalizing`。 |
| | **`PercentWithinStage`** | 可选，阶段内 0–100。 |
| **`CompletedInfo`** | **`OutputRelativePath`** | 相对于 `TempJobDir` 的输出文件路径。 |
| | **`OutputSuggestedExt`** | 可选，建议扩展名。 |
| | **`Metadata`** | 可选，附加键值。 |
| **`FailedInfo`** | **`ErrorMessage`** | 失败说明。 |
| | **`ErrorCode`** | 可选，错误码。 |

**输出约束：** 成功时必须 `OnCompleted`，且 `OutputRelativePath` 指向的文件在 `TempJobDir` 下**真实存在**。

**外部进程：** 若启动子进程，须将 **stdout/stderr 逐行** 转发到 `OnLog`，避免只缓冲最后几行。

**进度映射（建议）：** `Preparing` 0–100 → 总进度约 0–10%；`Running` → 约 10–90%；`Finalizing` → 约 90–100%。

### 7.9 取消 / 终止

当用户终止任务时，`CancellationToken` 会被取消。插件须**尽快**退出 `ExecuteAsync`，并**终止**其创建的外部进程（含子进程）。这与 `supportsTerminationOnCancel: true` 的要求一致。

---

## 8. 打包与安装

Host「添加插件」接受 **zip**：

1. 解压后在任意层级查找 `manifest.json`。  
2. 整个 zip 内须**恰好一个** `manifest.json`。  
3. 以该文件所在目录为插件根目录，复制到 `AppContext.BaseDirectory/plugins/<pluginId>/`。

建议：将 `<pluginId>/` 目录（含 `manifest.json` 与 dll）直接打成 zip。

---

## 9. 最小代码示例

```csharp
using PluginAbstractions;

namespace DemoSamplePlugin;

public sealed class SamplePlugin : IConverterPlugin
{
    public PluginManifest GetManifest() => throw new NotImplementedException();

    public async Task ExecuteAsync(
        ExecuteContext context,
        IProgressReporter reporter,
        CancellationToken cancellationToken = default)
    {
        reporter.OnProgress(new ProgressInfo(ProgressStage.Preparing, 0));
        reporter.OnLog($"InputPath={context.InputPath}");

        Directory.CreateDirectory(context.TempJobDir);

        var outputRelativePath = "output.txt";
        var outputPath = Path.Combine(context.TempJobDir, outputRelativePath);
        await File.WriteAllTextAsync(outputPath, "dummy", cancellationToken);

        reporter.OnProgress(new ProgressInfo(ProgressStage.Finalizing, 100));
        reporter.OnCompleted(new CompletedInfo(outputRelativePath, OutputSuggestedExt: "txt"));
    }
}
```

---

## 10. 常见错误清单

| 现象 | 常见原因 |
|------|----------|
| 插件未出现在列表中 | `supportsTerminationOnCancel` 非 `true` 或缺失 |
| 加载失败 / 无法创建实例 | `type` 拼写错误、类非 public、无参构造不可用 |
| DLL 未找到 | `assembly` 文件名错误或 DLL 未复制到插件目录 |
| 转换后 Host 报错 | 输出未在 `TempJobDir` 内，或 `OnCompleted` 路径与真实文件不一致 |
| 终止后仍有子进程 | 未随 `CancellationToken` 结束子进程树 |
| 配置控件异常 | `configSchema.fields[].type` 大小写错误或类型名 Host 不支持 |
| 某字段不显示 | `visibleIf` 的 `fieldKey` 与布尔字段 `key` 不一致，或条件不满足 |
| 目标格式缺失 | `supportedTargetFormats[].visibleIf` 条件不满足被过滤 |

---

*文档版本与 Host **v1.0.2**、`ConverTool.PluginAbstractions` **1.0.2** 对齐；若 Host 行为升级，请以仓库内 `Host/PluginManifestModel.cs` 与 `MainWindowViewModel` 配置逻辑为准。*
