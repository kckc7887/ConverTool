# ConverTool 插件开发规范

> 文档版本：v1.2.0 | Host 版本：v1.2.0 | 契约包版本：1.1.0

本规范定义 ConverTool Host 与插件之间的协议。Host 通过 `manifest.json` 驱动 UI，插件实现 `IConverterPlugin` 接口完成转换。

---

## 目录

- [快速开始](#快速开始)
- [1. 插件工程依赖](#1-插件工程依赖)
- [2. 目录结构](#2-目录结构)
- [3. manifest.json 字段参考](#3-manifestjson-字段参考)
- [4. i18n 国际化](#4-i18n-国际化)
- [5. configSchema 配置界面](#5-configschema-配置界面)
- [6. 共享工具缓存](#6-共享工具缓存)
- [7. 契约类型参考](#7-契约类型参考)
- [8. 打包与安装](#8-打包与安装)
- [9. config.json（Host 与插件目录）](#9-configjsonhost-与插件目录)
- [10. 最佳实践](#10-最佳实践)

---

## 快速开始

### 最小 manifest.json

```json
{
  "pluginId": "demo.sample",
  "version": "1.0.0",
  "assembly": "DemoSamplePlugin.dll",
  "type": "DemoSamplePlugin.SamplePlugin",
  "supportsTerminationOnCancel": true,
  "supportedInputExtensions": ["mkv", "avi"],
  "supportedTargetFormats": [
    { "id": "mp4", "displayNameKey": "plugin/demo.sample/target/mp4" }
  ],
  "configSchema": { "sections": [] },
  "i18n": { "localesFolder": "locales" }
}
```

### 最小插件代码

```csharp
using PluginAbstractions;

public class SamplePlugin : IConverterPlugin
{
    public PluginManifest GetManifest() => new(
        "demo.sample", "1.0.0",
        new[] { "mkv", "avi" },
        new[] { new TargetFormat("mp4", "plugin/demo.sample/target/mp4", null) },
        new ConfigSchema(Array.Empty<ConfigSection>()),
        new[] { "en-US", "zh-CN" },
        new I18nDescriptor("locales")
    );

    public async Task ExecuteAsync(ExecuteContext ctx, IProgressReporter reporter, CancellationToken ct)
    {
        reporter.OnProgress(new ProgressInfo(ProgressStage.Running, 0));
        
        // 执行转换逻辑，输出写入 ctx.TempJobDir
        
        reporter.OnCompleted(new CompletedInfo("output.mp4", "mp4"));
    }
}
```

---

## 1. 插件工程依赖

### NuGet 包

| 包名 | 用途 |
|------|------|
| `ConverTool.PluginAbstractions` | 定义 `IConverterPlugin`、`ExecuteContext`、`PluginManifest` 等契约类型 |

### 核心接口

```csharp
public interface IConverterPlugin
{
    PluginManifest GetManifest();
    Task ExecuteAsync(ExecuteContext context, IProgressReporter reporter, CancellationToken cancellationToken);
}
```

### 职责边界

| 角色 | 职责 |
|------|------|
| **Host** | 扫描插件、渲染配置 UI、匹配输入格式、提供临时目录、移动输出文件 |
| **插件** | 实现转换逻辑、仅写入临时目录、上报进度和结果 |

---

## 2. 目录结构

### 扫描规则

Host 递归扫描 `AppContext.BaseDirectory/plugins/**/manifest.json`

### 推荐结构

```text
plugins/
  <pluginId>/
    manifest.json
    config.json
    <PluginName>.dll
    locales/
      zh-CN.json
      en-US.json
```

### 文件说明

| 文件 | 说明 |
|------|------|
| `manifest.json` | 插件元数据，Host 主要依据此文件加载插件 |
| **`config.json`** | **必需**。Host **始终**从该文件读取插件持久化配置（与保存时写回同一路径）；缺省时视为空配置，但 **发版 zip 中必须包含**（至少 `{"settings":[]}`）。详见 **[§9](#9-configjsonhost-与插件目录)**。 |
| `<PluginName>.dll` | 插件程序集 |
| `locales/*.json` | 国际化语言包 |

---

## 3. manifest.json 字段参考

### 根级字段

| 字段名 | 必需 | 类型 | 说明 |
|--------|:----:|------|------|
| `pluginId` | ✅ | string | 插件唯一标识，建议与目录名一致 |
| `version` | ✅ | string | 版本号，如 `1.0.0` |
| `assembly` | ✅ | string | DLL 文件名（相对插件目录） |
| `type` | ✅ | string | 入口类型完整名称（命名空间.类名） |
| `supportsTerminationOnCancel` | ✅ | bool | **必须为 `true`**，声明支持取消时终止进程 |
| `supportedInputExtensions` | ✅ | string[] | 支持的输入扩展名（不含点号，小写） |
| `supportedTargetFormats` | ✅ | array | 输出格式列表 |
| `configSchema` | ❌ | object | 配置界面结构 |
| `titleKey` | ❌ | string | 插件标题的 i18n 键 |
| `descriptionKey` | ❌ | string | 插件描述的 i18n 键 |
| `author` | ❌ | string | 作者名 |
| `supportedLocales` | ❌ | string[] | 支持的语言列表，如 `["zh-CN", "en-US"]` |
| `i18n` | ❌ | object | 语言包配置 |

### supportedTargetFormats 字段

| 字段名 | 必需 | 类型 | 说明 |
|--------|:----:|------|------|
| `id` | ✅ | string | 格式标识，如 `mp4`、`png`，作为输出扩展名 |
| `displayNameKey` | ✅ | string | 显示名称的 i18n 键 |
| `descriptionKey` | ❌ | string | 详细描述的 i18n 键 |
| `visibleIf` | ❌ | object | 条件显示规则 |

### i18n 字段

| 字段名 | 必需 | 默认值 | 说明 |
|--------|:----:|--------|------|
| `localesFolder` | ❌ | `"locales"` | 语言包子目录名 |

---

## 4. i18n 国际化

### 语言包结构

```json
{
  "strings": {
    "plugin/demo.sample/title": "示例插件",
    "plugin/demo.sample/target/mp4": "MP4（推荐，兼容性好）"
  }
}
```

### i18n 键命名规范

| 类型 | 格式 | 示例 |
|------|------|------|
| 插件标题 | `plugin/<pluginId>/title` | `plugin/ffmpeg.video.transcoder/title` |
| 插件描述 | `plugin/<pluginId>/description` | `plugin/ffmpeg.video.transcoder/description` |
| 目标格式 | `plugin/<pluginId>/target/<formatId>` | `plugin/ffmpeg.video.transcoder/target/mp4` |
| 配置分区 | `plugin/<pluginId>/section/<sectionId>` | `plugin/imagemagick.image.transcoder/section/targetSize` |
| 配置字段标签 | `plugin/<pluginId>/field/<fieldKey>/label` | `plugin/ffmpeg.video.transcoder/field/crf/label` |
| 配置字段帮助 | `plugin/<pluginId>/field/<fieldKey>/help` | `plugin/ffmpeg.video.transcoder/field/crf/help` |
| 选项标签 | `plugin/<pluginId>/field/<fieldKey>/opt/<optId>` | `plugin/ffmpeg.video.transcoder/field/resolution/opt/1080p` |

### 目标格式显示名称规范

**格式：** `格式名（用途说明）`

**示例：**

| 格式 | 中文 | 英文 |
|------|------|------|
| mp4 | MP4（推荐，兼容性好） | MP4 (Recommended, best compatibility) |
| mkv | MKV（适合保存多音轨） | MKV (Multiple audio tracks) |
| webm | WebM（适合网页使用） | WebM (Web optimized) |
| png | PNG（支持透明） | PNG (Transparent background) |
| jpeg | JPEG（照片推荐） | JPEG (Best for photos) |
| pdf | PDF（打印推荐） | PDF (Best for printing) |
| docx | DOCX（推荐，兼容性好） | DOCX (Recommended, best compatibility) |
| doc | DOC（兼容旧版 Office） | DOC (Legacy Office format) |

---

## 5. configSchema 配置界面

### 根对象

| 字段名 | 类型 | 说明 |
|--------|------|------|
| `sections` | array | 配置分区列表 |
| `fieldBoolRelations` | array | 复选框联动规则 |
| `fieldPersistOverrides` | array | 持久化覆盖规则 |

### section 分区

| 字段名 | 必需 | 类型 | 说明 |
|--------|:----:|------|------|
| `id` | ✅ | string | 分区标识 |
| `titleKey` | ❌ | string | 分区标题 i18n 键 |
| `descriptionKey` | ❌ | string | 分区描述 i18n 键 |
| `collapsedByDefault` | ❌ | bool | 是否默认折叠 |
| `fields` | ✅ | array | 配置字段列表 |

### field 字段通用属性

| 字段名 | 必需 | 类型 | 说明 |
|--------|:----:|------|------|
| `key` | ✅ | string | 配置键，传入 `ExecuteContext.SelectedConfig` |
| `type` | ✅ | string | 控件类型 |
| `labelKey` | ✅ | string | 标签 i18n 键 |
| `helpKey` | ❌ | string | 帮助文本 i18n 键 |
| `defaultValue` | ❌ | any | 默认值 |
| `visibleIf` | ❌ | object | 条件显示规则（由**另一个复选框**字段控制） |
| `visibleForInputExtensions` | ❌ | string[] | 非空时：仅当**当前输入文件扩展名**与列表中任一项匹配（忽略大小写）时显示 |
| `visibleForTargetFormats` | ❌ | string[] | 非空时：仅当**当前选中的目标格式 id**与列表中任一项匹配（忽略大小写）时显示 |

### 控件类型

#### Text - 文本输入

```json
{ "key": "name", "type": "Text", "labelKey": "...", "defaultValue": "" }
```

#### Checkbox - 复选框

```json
{ "key": "enabled", "type": "Checkbox", "labelKey": "...", "defaultValue": false }
```

#### Select - 下拉选择

```json
{
  "key": "resolution",
  "type": "Select",
  "labelKey": "...",
  "defaultValue": "keep",
  "options": [
    { "id": "keep", "labelKey": ".../opt/keep" },
    { "id": "1080p", "labelKey": ".../opt/1080p" }
  ]
}
```

#### Range - 滑块

```json
{
  "key": "quality",
  "type": "Range",
  "labelKey": "...",
  "defaultValue": 23,
  "range": { "min": 0, "max": 51, "step": 1 }
}
```

#### Number - 数字输入

```json
{
  "key": "count",
  "type": "Number",
  "labelKey": "...",
  "defaultValue": 1,
  "range": { "min": 1, "max": 100, "step": 1 }
}
```

#### Path - 路径选择

```json
{
  "key": "outputPath",
  "type": "Path",
  "labelKey": "...",
  "defaultValue": "",
  "path": { "kind": "Folder", "mustExist": true }
}
```

### visibleIf 条件显示

```json
{ "fieldKey": "enableAdvanced", "equals": true }
```

当 `enableAdvanced` 复选框为 `true` 时显示该字段。

### visibleForTargetFormats（按目标格式显示）

与 `visibleIf` 不同：此项按**主界面选中的目标格式**（`supportedTargetFormats[].id`）过滤，无需额外复选框。例如 ZIP 内页格式仅在选择目标 `zip` 时显示：

```json
"visibleForTargetFormats": ["zip"]
```

可与 `visibleForInputExtensions` 同时存在，二者**均**满足时才显示。

### fieldBoolRelations 联动规则

```json
{
  "if": { "fieldKey": "retainSettings", "equals": false },
  "then": { "targetKey": "enabled", "value": false }
}
```

### fieldPersistOverrides 持久化覆盖

```json
{
  "when": { "fieldKey": "retainSettings", "equals": false },
  "fields": { "enabled": false }
}
```

界面上的配置值写入磁盘时，Host 使用插件目录下的 **`config.json`**（**插件必须提供该文件**；另见 Host 根目录 **`config.json`**）承载 `settings[]` 项；字段形状与 **`persistValue`（是否允许写回 `value`）** 见 **[§9](#9-configjsonhost-与插件目录)**。

---

## 6. 共享工具缓存

**v1.1.0 新增**：Host 提供统一的工具缓存，避免各插件重复下载。

**契约 1.1.0**（NuGet `ConverTool.PluginAbstractions`）：`ConfigField` 支持 **`VisibleForTargetFormatIds`**；manifest 配置 **`visibleForTargetFormats`**，由 Host 按当前选中的目标格式显示/隐藏字段。详见 [§5](#5-configschema-配置界面) 与 **[releases/PluginAbstractions-v1.1.0.md](releases/PluginAbstractions-v1.1.0.md)**。

### 缓存目录

```text
%LOCALAPPDATA%\ConverTool\tools\
  ffmpeg\latest\
  imagemagick\7.1.2-17-portable-Q16-x64\
  pandoc\pandoc-3.6.4-windows-x86_64\
  libreoffice\25.8.5\
  document-pdf-render\1.1.0\   ← 内置「文档」插件 PDF→图片（PDFium + SkiaSharp），首次需要时下载
```

### 文档插件：PDF→图片依赖（`document-pdf-render`）

与 Pandoc / LibreOffice 一样，**不在安装包内**携带 PDFium / SkiaSharp 等大体积依赖。用户**第一次**将 PDF 转为长图 / ZIP 时，插件从 **nuget.org**（flat container）按固定版本列表下载 **`DtronixPdf`、`PDFiumCore`、`bblanchon.PDFium.Win32`、`SkiaSharp`、`SkiaSharp.NativeAssets.Win32`** 等到 **`document-pdf-render\<版本>\`**，并把插件目录内自带的瘦客户端 **`PandocDocumentTranscoder.PdfRender.dll`**、**`PluginAbstractions.dll`** 复制进同一目录后加载。**无需**在 GitHub Release 上单独上传 zip。

- **开发/离线**：设置环境变量 **`CONVERTOOL_PDF_RENDER_DIR`** 指向已具备完整 DLL 的目录（与缓存目录布局相同），可跳过下载；若目录不完整，会回退到默认缓存并尝试从 NuGet 拉取。

### API 参考

```csharp
public static class SharedToolCache
{
    public static readonly string CacheRoot;
    
    public static string GetToolDir(string toolName, string? version = null);
    
    public static string GetToolPath(string toolName, string executableName, string? version = null);
    
    public static bool IsToolCached(string toolName, string? version = null);
    
    public static Task DownloadAndExtractAsync(
        string toolName, string version, string downloadUrl, string targetDir,
        IProgressReporter? reporter = null, CancellationToken ct = default);
}
```

### 推荐使用模式

```csharp
private static readonly SemaphoreSlim ToolGate = new(1, 1);

private static async Task<string> EnsureToolAsync(IProgressReporter reporter, CancellationToken ct)
{
    // 1. 优先使用系统 PATH
    var fromPath = TryGetFromPath("tool.exe");
    if (fromPath is not null)
    {
        reporter.OnLog($"[tool] using from PATH: {fromPath}");
        return fromPath;
    }

    // 2. 检查共享缓存
    var cachedPath = SharedToolCache.GetToolPath("tool", "tool.exe", "1.0.0");
    if (File.Exists(cachedPath))
    {
        reporter.OnLog($"[tool] using cached: {cachedPath}");
        return cachedPath;
    }

    // 3. 下载到共享缓存
    await ToolGate.WaitAsync(ct);
    try
    {
        if (File.Exists(cachedPath)) return cachedPath; // 双重检查
        
        reporter.OnLog("[tool] downloading...");
        await SharedToolCache.DownloadAndExtractAsync(
            "tool", "1.0.0", "https://...", 
            SharedToolCache.GetToolDir("tool", "1.0.0"),
            reporter, ct);
        
        return cachedPath;
    }
    finally { ToolGate.Release(); }
}
```

---

## 7. 契约类型参考

### ExecuteContext

| 字段 | 类型 | 说明 |
|------|------|------|
| `JobId` | string | 任务 ID |
| `InputPath` | string | 输入文件绝对路径 |
| `TempJobDir` | string | 临时目录绝对路径（输出必须写在此目录） |
| `TargetFormatId` | string | 目标格式 ID |
| `SelectedConfig` | IReadOnlyDictionary | 用户配置 |
| `Locale` | string | 当前语言 |
| `OutputNamingContext` | IReadOnlyDictionary | 输出命名上下文 |

### ConfigField（`PluginAbstractions` record，节选）

| 字段 | 说明 |
|------|------|
| `Key` / `Type` / `LabelKey` / `DefaultValue` / `Options` / `Range` / `Path` | 与 manifest `configSchema` 中对应字段一致 |
| `VisibleIf` | 由**另一配置项**中的复选框控制显示（`VisibleIfCondition`） |
| `VisibleForInputExtensions` | 非空时仅当**当前输入文件扩展名**匹配其一 |
| `VisibleForTargetFormatIds` | **契约 1.1.0+**：非空时仅当**当前选中的目标格式 id**（`TargetFormatId`）匹配其一；与 JSON manifest 键 **`visibleForTargetFormats`** 对应 |

### IProgressReporter

| 方法 | 说明 |
|------|------|
| `OnProgress(ProgressInfo)` | 上报进度 |
| `OnLog(string)` | 输出日志 |
| `OnCompleted(CompletedInfo)` | 任务成功 |
| `OnFailed(FailedInfo)` | 任务失败 |

### ProgressStage

| 值 | 说明 |
|----|------|
| `Preparing` | 准备阶段 |
| `Running` | 执行阶段 |
| `Finalizing` | 收尾阶段 |

---

## 8. 打包与安装

### 打包结构

```text
<pluginId>.zip
  manifest.json
  config.json
  <PluginName>.dll
  locales/
    zh-CN.json
    en-US.json
```

`config.json` 为 **必需文件**（至少包含 `{"settings":[]}`）；与 `manifest.json` 一并置于 zip 根级。

### 安装位置

Host 将 zip 解压到 `plugins/<pluginId>/`

---

## 9. config.json（Host 与插件目录）

### 插件（必需）

每个插件目录 **必须** 提供 **`config.json`**。Host **会始终读取** `plugins/<pluginId>/config.json` 以恢复 `configSchema` 对应字段及目标格式等持久化状态；这不是可选步骤。若磁盘上尚无该文件（例如旧手工拷贝），Host 视为空 `settings`，但 **插件发行包（zip）与源码树中应始终包含该文件**，避免用户侧配置无法落盘或与文档不一致。

### Host（本体）

本体根目录的 **`config.json`** 承载 Host 全局项（语言、输出目录、并行等），与插件文件 **相互独立**。

路径约定：

| 位置 | 文件路径（相对 `AppContext.BaseDirectory`） |
|------|---------------------------------------------|
| Host | `config.json` |
| 插件 | `plugins/<pluginId>/config.json` |

### 文件结构

根对象为 **`settings`** 数组，每项至少包含 **`key`** 与 **`value`**；**`default`** 与 **`persistValue`** 为 **单项上的可选字段**（整份 `config.json` 对插件而言仍是 **必需文件**）：

```json
{
  "settings": [
    {
      "key": "TargetFormatId",
      "value": "mp4",
      "default": "mp4",
      "persistValue": true
    },
    {
      "key": "ShippedDefaultHint",
      "value": "do-not-change-in-ui-save",
      "persistValue": false
    }
  ]
}
```

### `persistValue`（单项可选）

仅表示 **某一条** `settings` 项是否允许把 UI 写回 `value`；**不是**「整个文件可有可无」。

| 取值 | 含义 |
|------|------|
| **未写** 或 **`true`** | 用户点击保存时，Host 会把界面上的值写回该项的 **`value`**（与历史行为一致）。 |
| **`false`** | **只读持久化**：仍从文件 **读取** `value` 供界面恢复；保存时 **不会** 用当前 UI 覆盖该项的 `value`，磁盘上该项保持原样。 |

插件可在发行包中通过 `persistValue: false` 锁定由安装包或管理员提供的只读项；需要用户可改的字段应省略 `persistValue` 或设为 `true`。

> 实现见 Host 仓库 `Host/Settings/SettingManager.cs`（`SettingItem.PersistValue`、`SetValue`）。

---

## 10. 最佳实践

### 本地化与 manifest 同步

- **`manifest.json`** 中所有 **`labelKey` / `displayNameKey`**（含 **`configSchema`** 内字段）必须在 **`locales/<locale>.json`** 的两种语言中都有对应条目；否则 Host 会显示 **key** 原文。
- 若你在 **`plugins-src/<Plugin>`** 维护源码，发布或拷贝到 **`Host/Plugins/<folder>/`** 时，请同步 **`manifest.json`** 与 **`locales/`**，避免 Host 内置插件与源码副本不一致。

### 目标格式命名

- 使用"格式（用途）"形式
- 推荐格式放在前面
- 说明应简洁实用

### 工具查找优先级

1. 系统环境变量（PATH）
2. 特定环境变量（如 `FFMPEG_PATH`）
3. 共享缓存
4. 自动下载

### 错误处理

- 使用 `OnFailed` 上报错误，包含错误码
- 日志中使用 `[tool]` 前缀标识工具
- 捕获并上报异常信息

### 取消支持

- 监听 `CancellationToken`
- 及时终止外部进程
- 清理临时资源

---

*文档版本与 Host **v1.2.0**、`ConverTool.PluginAbstractions` **1.1.0** 对齐；若 Host 行为升级，请以仓库内 `Host/PluginManifestModel.cs` 与 `MainWindowViewModel` 配置逻辑为准。*
