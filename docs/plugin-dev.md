# ConverTool 插件开发规范（V0.3）

本规范定义 ConverTool `Host` 与插件之间的协议、目录约定与关键约束。插件为 .NET DLL，供 Host 扫描、展示配置 UI，并执行转换任务。

## 1. 核心职责与执行边界

1. Host 负责：
   - 扫描 `plugins/` 目录下的 `manifest.json`
   - 按 `configSchema` 动态渲染配置 UI
   - 路由输入文件到对应插件（基于输入扩展名）
   - 创建每次任务的临时目录，并在插件完成后移动/重命名最终产物
   - 将插件上报的 `ProgressStage + percentWithinStage` 映射到 UI 总进度
2. 插件负责：
   - 实现 `PluginAbstractions.IConverterPlugin`
   - 在 `ExecuteAsync` 中执行转换逻辑
   - 将产物写入 Host 提供的 `TempJobDir`
   - 通过 reporter 回传日志、进度与成功/失败状态

## 2. 目录约定与扫描规则

Host 扫描路径（递归）：

- `AppContext.BaseDirectory/plugins/**/manifest.json`

对每个命中的 `manifest.json`，Host 推导：

1. `pluginDir` = `manifest.json` 的父目录
2. DLL 路径 = `pluginDir/manifest.assembly`
3. 入口类型 = `manifest.type`（反射实例化）

推荐插件目录结构：

```text
plugins/
  <pluginId>/
    manifest.json
    <assembly>.dll
    locales/
      zh-CN.json
      en-US.json
```

## 3. manifest.json 规范

`manifest.json` 用于声明插件能力、配置 UI 元数据与 i18n 信息。

### 3.1 字段说明

必需字段：

- `pluginId`：字符串，插件唯一标识（建议与目录名一致）
- `version`：字符串（用于显示/区分版本）
- `assembly`：字符串，DLL 文件名（相对于插件目录）
- `type`：字符串，插件实现类型全名（Namespace + ClassName）
- `supportsTerminationOnCancel`：`bool`
  - Host 要求其为 `true`
  - 若为 `false` 或缺失，Host 会跳过加载该插件
- `supportedInputExtensions`：`string[]`
  - 不包含点号，例如：`["mkv"]`
  - Host 会对输入扩展名做标准化并进行大小写不敏感匹配
- `supportedTargetFormats`：数组（至少包含 1 项）
  - 每项至少包含：
    - `id`：目标格式 id（例如：`"mp4"`）
    - `displayNameKey`：目标格式显示名称的 i18n key
    - `descriptionKey`：可选，描述的 i18n key

可选字段：

- `configSchema`：对象
  - 缺失、为 `null`，或 `sections` 为空时：配置区不渲染任何字段
- `supportedLocales`：字符串数组（当前 Host 不强校验，仅作为声明信息）
- `i18n`：对象
  - `i18n.localesFolder`：语言包目录名，缺失时 Host 默认使用 `"locales"`

### 3.2 最小可用示例

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

## 4. i18n 规范

语言包文件位置：

- `plugins/<pluginId>/<localesFolder>/<locale>.json`

语言包内容结构：

```json
{ "strings": { "<key>": "<text>" } }
```

Host 行为要点：

- 缺失 locale 文件或 key 不会崩溃
- 非 `en-US` 时会回退到 `en-US`
- 最终仍找不到 key 时，会直接显示 key

## 5. configSchema：配置 UI 规范

`configSchema` 决定 Host 渲染哪些配置控件，并在执行时把用户选择汇总到 `ExecuteContext.SelectedConfig`。

### 5.1 JSON 结构

```json
{
  "sections": [
    {
      "id": "basic",
      "titleKey": "plugin/<id>/section/basic",
      "descriptionKey": null,
      "collapsedByDefault": false,
      "fields": [
        { "...": "..." }
      ]
    }
  ]
}
```

`fields[]` 通用字段：

- `key`：配置键名（写入 `ExecuteContext.SelectedConfig[key]`）
- `type`：控件类型（区分大小写，必须为 Host 实际支持的取值）
- `labelKey`：字段标签 i18n key
- `helpKey`：可选，帮助说明 i18n key
- `defaultValue`：可选，默认值（类型取决于 `type`）

### 5.2 type 的可用值（Host v0.3）

Host 当前支持以下 `type` 值（大小写必须匹配）：

- `Text`
  - 默认值解析：`defaultValue` 为 string/number/bool 会转为字符串；缺失则默认为空字符串
- `Checkbox`
  - Host 当前固定默认值为 `false`（忽略 `defaultValue`）
  - 插件收到的 `SelectedConfig[key]` 为 `true/false`
- `Select`
  - 需要 `options`：
    - `options` = `[ { "id": "...", "labelKey": "..." }, ... ]`
  - 默认值解析：`defaultValue` 会转为字符串并匹配 option 的 `id`
  - 插件收到的 `SelectedConfig[key]` 为选中 option 的 `id`（字符串）
- `Range`
  - 需要 `range`：
    - `range` = `{ "min": 0, "max": 100, "step": 1 }`
  - 默认值解析：`defaultValue` 为 JSON number 或可解析字符串；不可解析时回退到 `min`
  - 插件收到的 `SelectedConfig[key]` 为 Range 控件的 `ValueText`（字符串形式的数值）
- `Path`
  - 需要 `path`：
    - `path.kind`：`"File"` 或 `"Folder"`
    - `path.mustExist`：当前用于声明语义；Host 不强制校验
  - 默认值解析：`defaultValue` 转为字符串作为初始路径
  - 插件收到的 `SelectedConfig[key]` 为用户选择/输入的绝对路径字符串

不支持的字段类型：

- `MultiSelect`：当前 Host 不实现相应控件渲染与值回传逻辑

### 5.3 Path 字段示例

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

## 6. 插件代码规范（IConverterPlugin）

接口：

- `PluginManifest GetManifest()`
- `Task ExecuteAsync(ExecuteContext context, IProgressReporter reporter, CancellationToken cancellationToken = default)`

### 6.1 ExecuteContext 字段

- `InputPath`：输入文件绝对路径
- `TempJobDir`：本次输入文件对应的临时目录（插件必须在该目录内写入产物）
- `TargetFormatId`：目标格式 id
- `SelectedConfig`：配置键值字典（字段 `key` -> 用户值）
- `Locale`：Host 当前 UI 语言
- `OutputNamingContext`：输出命名上下文（Host 内部使用，插件可选读取）

### 6.2 输出与回传约束

1. 产物位置
   - 插件必须把输出文件写入：
     - `<TempJobDir>/<outputRelativePath>`
2. 成功回传
   - 完成后调用：
     - `reporter.OnCompleted(new CompletedInfo(outputRelativePath, ...))`
   - `outputRelativePath` 必须是相对于 `TempJobDir` 的相对路径
   - 且对应文件必须存在
3. 失败回传
   - 失败时调用：
     - `reporter.OnFailed(new FailedInfo("错误信息", "可选错误码"))`
   - 并尽快返回

### 6.3 日志与进度

- 日志：使用 `reporter.OnLog(line)` 输出执行过程，便于定位问题
- 进度：使用 `reporter.OnProgress(new ProgressInfo(stage, percentWithinStage))`
- 建议：
  - `ProgressStage.Preparing`：0~100（Host 映射到总进度 0~10）
  - `ProgressStage.Running`：0~100（Host 映射到总进度 10~90）
  - `ProgressStage.Finalizing`：0~100（Host 映射到总进度 90~100）

### 6.4 取消/终止行为（supportsTerminationOnCancel）

当 Host 用户执行“终止”时，会取消 `CancellationToken`。

若 `manifest.json` 声明 `supportsTerminationOnCancel: true`，插件必须做到：

1. 退出 `ExecuteAsync`（尽快响应 token）
2. 终止插件内部启动的外部进程（必须杀掉进程树/子进程）

## 7. 打包与安装规范

### 7.1 Host 的插件安装输入

Host “添加插件”接受一个 `.zip` 文件并安装。

安装规则（Host 行为）：

1. zip 解压后，在任意层级查找 `manifest.json`
2. 要求解压目录中恰好存在一个 `manifest.json`
3. 以该 `manifest.json` 的所在目录为“源插件目录”，复制到：
   - `AppContext.BaseDirectory/plugins/<manifest.pluginId>/`

建议打包：直接把 `<pluginId>/` 目录压成 zip，使 manifest 与 DLL 同级。

## 8. 示例：最小插件骨架

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

## 9. 常见错误清单（快速排查）

- `supportsTerminationOnCancel` 未声明或为 `false`：Host 跳过加载插件
- `manifest.type` 写错：反射无法创建插件实例
- `manifest.assembly` 指向的 DLL 不存在：插件无法加载
- 插件输出未写入 `TempJobDir` 或输出文件与 `outputRelativePath` 不一致
- `outputRelativePath` 对应文件不存在：Host 无法完成移动/重命名
- 取消时未终止外部进程树：用户“终止”后可能仍有残留进程
- `configSchema.fields[].type` 大小写不匹配：Host 可能把控件当作默认文本控件处理
