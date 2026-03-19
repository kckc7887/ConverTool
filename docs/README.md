# ConverTool

> **当前主线版本：Host v1.0.1**（契约包 `ConverTool.PluginAbstractions` **1.0.0**；历史版本说明见 [`docs/releases/`](releases/)。）

ConverTool 是一个“轻量化的跨平台文件转换器骨架”：它用 Avalonia 做 UI，并通过动态加载的插件（.NET DLL）来完成真正的转换逻辑。Host 负责把输入文件路由到对应插件、提供配置 UI、创建临时目录、接收插件日志/进度/完成信息，并将临时产物按命名模板移动到最终输出目录。

## 使用方法（普通用户）

1. 启动 ConverTool
2. 插件：打开“插件管理” -> “添加插件”安装你的 `*.zip` 插件
3. 输入：点击「浏览」选择文件，或将文件**拖拽到输入区**；已选文件以**图标 + 文件名**列表显示，可点「×」移除。
4. 配置与目标：根据插件提供的目标格式选择输出，并填写配置项
5. 输出：设置输出目录/命名模板（可选），点击“Start”
6. 日志与结果：处理过程会在日志区域实时显示，完成后结果会列出到输出区域

## 内置基础插件（默认附带）

发行版与源码中的 `Host/plugins/`（或安装目录下的 `plugins/`）**默认包含**两个插件，一般无需再单独安装即可使用：

| 插件 ID | 功能 | 使用前提 |
|---------|------|----------|
| `ffmpeg.video.transcoder` | 视频转码（MP4 / MKV / MOV / WebM / AVI 等） | 本机已安装 **ffmpeg** 并在 **PATH** 中可用。 |
| `imagemagick.image.transcoder` | 图片格式互转（PNG、JPEG、WebP、TIFF、ICO 等） | 优先使用 **PATH** 中的 `magick`；若无，插件会尝试自动下载便携 ImageMagick（详见插件实现与日志）。 |

仍可在 **插件管理** 中通过 `.zip` 安装、更新或增删其他插件。

## 技术细节（展开查看）

<details>
<summary>架构/协议/实现细节（展开）</summary>

下面这份文档以“当前代码实际实现”为主，系统性描述底层架构、协议/数据结构、执行方式与关键约束。

## 1. 总体架构

1. `Host`（UI + 调度）
   - 扫描 `AppContext.BaseDirectory/plugins/` 下的插件 `manifest.json`
   - 动态加载插件程序集（可卸载的 `AssemblyLoadContext`）
   - 根据输入扩展名选择插件（`PluginRouter`）
   - 依据插件 `configSchema` 动态渲染配置控件
   - 为每个输入文件创建临时工作目录，并在插件完成后落盘输出
   - 将插件 `OnProgress` 映射到 UI 的总体进度

2. `PluginAbstractions`（协议层）
   - 定义插件接口 `IConverterPlugin`
   - 定义插件 manifest、配置 schema、执行上下文、进度/完成/失败回传的数据结构

3. `Plugin`（外部引擎适配器）
   - 实现 `IConverterPlugin`
   - 在 `ExecuteAsync` 中调用外部引擎（例如系统 `ffmpeg`，或随插件打包的 `ffmpeg.exe`）
   - 按 Host 的约定把产物写入 `ExecuteContext.TempJobDir`，并通过 reporter 回传 `OnCompleted` / `OnFailed`

## 2. 插件协议与数据结构（PluginAbstractions）

### 2.1 插件入口：`IConverterPlugin`

- `PluginManifest GetManifest()`
- `Task ExecuteAsync(ExecuteContext context, IProgressReporter reporter, CancellationToken cancellationToken = default)`

插件只关心：输入路径、临时目录、目标格式 id、用户配置、以及 reporter 回传方式。

### 2.2 `PluginManifest`（插件声明能力）

`PluginManifest` 包含：

- `PluginId`、`Version`
- `SupportedInputExtensions`：不带点号的扩展名集合（例如 `mkv`）
- `SupportedTargetFormats`：每个目标格式包含 `Id`、`DisplayNameKey`、可选 `DescriptionKey`
- `ConfigSchema`：配置 UI 描述（`Sections` -> `Fields`）
- `SupportedLocales` 与 `I18nDescriptor(Local esFolder)`：i18n 资源位置声明

### 2.3 `ConfigSchema`（配置 UI 的结构化描述）

`ConfigSchema` 的结构是：

- `Sections[]`
  - `id`、可选 `titleKey`/`descriptionKey`、`collapsedByDefault`
  - `Fields[]`
    - `key`：写入 `ExecuteContext.SelectedConfig` 的配置键
    - `type`：当前支持 `Select` / `MultiSelect` / `Range` / `Text` / `Checkbox` / `Path`
    - `labelKey` / 可选 `helpKey`
    - `defaultValue`：默认值（类型取决于字段类型）
    - `options`：适用于 `select`（以及 vNext 预留的多选扩展）
    - `range`：适用于 `Range`
    - `path`：适用于 `Path`（文件/文件夹语义）

> Host 的实际控件实现目前会根据 manifest 的 `ConfigFieldModel.Type` 字符串创建对应 `ConfigFieldVm`：`TextFieldVm` / `CheckboxFieldVm` / `SelectFieldVm` / `PathFieldVm` / `RangeFieldVm`。

### 2.4 Host->Plugin 执行上下文：`ExecuteContext`

`ExecuteContext` 包含：

- `JobId`：本次文件任务 id（Host 生成）
- `InputPath`：当前输入文件绝对路径
- `TempJobDir`：本次输入文件对应的临时目录（插件必须写入此目录）
- `TargetFormatId`：目标格式 id（来自 UI 选择；或 fallback）
- `SelectedConfig`：用户配置字典（`key -> object?`）
- `Locale`：当前 UI locale（Host 当前语言）
- `OutputNamingContext`：命名模板上下文（当前实现里提供 `base`/`index`/`ext`）

### 2.5 Plugin->Host 回传：`IProgressReporter`

插件在运行过程中通过 reporter 回传：

- `OnLog(string line)`：日志行（Host 展示到处理区）
- `OnProgress(ProgressInfo info)`：分 stage 的进度
- `OnCompleted(CompletedInfo info)`：成功完成并回传
  - `OutputRelativePath`：相对 `TempJobDir` 的路径
  - `OutputSuggestedExt`：可选建议后缀（当前 Host 主要使用命名模板里的 `{ext}`）
- `OnFailed(FailedInfo info)`：失败信息（`ErrorMessage` + 可选 `ErrorCode`）

`ProgressStage` 当前包含：`Preparing` / `Running` / `Finalizing`。

## 3. Host 的插件加载、发现与路由

### 3.1 插件发现：扫描 `plugins/manifest.json`

Host 在启动时通过 `PluginCatalog.LoadFromOutput(AppContext.BaseDirectory)`：

- 读取 `AppContext.BaseDirectory/plugins/**/manifest.json`
- 反序列化为 `PluginManifestModel`
- 对 `supportsTerminationOnCancel` 做“合法性检查”：为 `false` 或缺失时会跳过加载

### 3.2 插件路由：按输入扩展名匹配

`PluginRouter.RouteByInputPath(catalog, inputPath)`：

- 取 `Path.GetExtension(inputPath)` 得到扩展名
- 与 `SupportedInputExtensions`（不区分大小写）做匹配
- 在候选多个插件时，按“扩展名集合长度更短/更长”的逻辑排序后取第一个（当前实现逻辑见代码）

### 3.3 动态加载与卸载：`PluginRuntimeLoader`

`PluginRuntimeLoader.TryLoadPlugin(entry)`：

- 计算 `assemblyPath = Path.Combine(entry.PluginDir, entry.Manifest.Assembly)`
- 用可回收（collectible）的 `AssemblyLoadContext` 加载插件程序集
- 反射创建 `entry.Manifest.Type` 对应的类型实例
- 返回一个可 `Dispose()` 的 `PluginLoadHandle`（Dispose 中会 `Unload()` 并触发 GC）

这使得插件加载过程与 Host 解耦，并支持一定程度的卸载隔离。

## 4. UI 动态配置与 i18n

### 4.1 Host + Plugin 的语言包格式

- Host 文案：`Host/locales/<locale>.json`
- 插件文案：`plugins/<pluginId>/<localesFolder>/<locale>.json`

语言包结构约定为：

```json
{ "strings": { "<key>": "<text>" } }
```

`PluginI18nService` 的加载逻辑：

- 先查当前 `locale`
- 不存在则回退到 `en-US`
- 仍找不到则直接显示 key（而不是崩溃）

### 4.2 configSchema -> 控件渲染

`MainWindowViewModel.ReloadPluginContext()`：

1. 通过输入文件列表中的**第一个文件**确定“激活插件”（`ResolveActivePlugin`）
2. 将激活插件的：
   - `SupportedTargetFormats` 显示为目标格式下拉
   - `ConfigSchema.Sections[].Fields[]` 动态生成配置控件

控件创建会用到字段的：
- `Type`（字符串，如 `Checkbox/Select/Range/Text/Path`）
- `LabelKey` / `HelpKey`（由 `PluginI18nService` 翻译）
- `DefaultValue`（按字段类型转换成 Host 内部默认值）

## 5. 执行方式：串行/并行、临时目录与落盘输出

### 5.1 开始任务：`StartAsync()`

Host 会：

- 从 `InputFiles` 集合得到输入文件路径列表
- 根据 `EnableParallelProcessing` 选择：
  - 串行：`RunSerialAsync`
  - 并行：`RunParallelAsync`（用 `SemaphoreSlim` 控制并发）

### 5.2 临时目录创建（每个输入一个临时工作目录）

每个输入文件会生成：

- `jobId = Guid.NewGuid().ToString("N")`
- `tempJobDir = Path.Combine(Path.GetTempPath(), "ConverTool", jobId, index.ToString())`

并在该目录下创建最终产物的“临时相对路径”。

### 5.3 构造 `ExecuteContext`

Host 在调用插件时创建：

- `JobId` / `InputPath` / `TempJobDir`
- `TargetFormatId`
  - 优先使用 UI 中的 `SelectedTargetFormat?.Id`
  - 若为空则 fallback 到当前路由插件的第一个 `SupportedTargetFormats` 的 `Id`
- `SelectedConfig`
  - 来自 `ConfigFields`（当前激活插件的配置控件值）
- `Locale`：来自 Host i18n
- `OutputNamingContext`
  - 由 Host 内部命名模板使用：`{ base, index, ext }`

### 5.4 reporter 回调 -> UI：日志与进度映射

Host 会用 `VmReporter` 适配回调：

1. `OnLog(line)` -> `AppendLog(line)`
2. `OnProgress(info)`
   - `MapToOverallPercent(stage, percentWithinStage)` 将阶段进度映射到 0~100：
     - `Preparing`：0~10
     - `Running`：10~90
     - `Finalizing`：90~100
   - 串行模式：用当前文件进度 + 已完成文件数映射为总体百分比
   - 并行模式：对各文件的总体百分比取平均（`UpdateOverallFromWorker`）
3. `OnCompleted(info)`
   - `tempOut = Path.Combine(tempJobDir, info.OutputRelativePath)`
   - 调用 `MoveToFinalOutput(...)` 将临时文件移动到最终输出目录
4. `OnFailed(info)` -> 记录失败原因并在结果列表展示

### 5.5 落盘输出：命名模板、重名策略与保留临时文件

`MoveToFinalOutput(tempOutputPath, inputPath, targetExt, index)`：

1. 输出目录选择
   - `UseInputDirAsOutput=true`：输出到输入文件所在目录
   - 否则使用 UI 的 `OutputDir`（若为空会 fallback 到 Host 默认 `output` 文件夹）
2. 命名模板
   - 使用 UI 的 `NamingTemplate`（默认 `{base}.{ext}`）
   - 替换占位符：
     - `{base}`：输入文件名（不含扩展名）
     - `{ext}`：目标格式扩展（`TargetFormatId`）
     - `{index}`：批处理序号（从 1 开始）
   - 若结果未包含扩展名，会自动追加 `.{targetExt}`
   - 替换非法文件名字符为 `_`
3. 重名冲突解决（`ResolveConflict`）
   - 若目标文件已存在，会尝试生成：`<name>_(<i>)<ext>`，`i` 从 1 开始，最多尝试到 9999
4. 移动与覆盖处理
   - 优先 `File.Move(..., overwrite:false)`
   - 如果发生 `IOException`，改用 `File.Copy(..., overwrite:false)` + `File.Delete(tempOutputPath)`
5. 保留临时目录
   - `KeepTemp=false`（默认）时，在每个输入完成后 best-effort 删除 `tempJobDir`
   - `KeepTemp=true` 时保留用于排查

## 6. ffmpeg / 其他外部引擎能否“随插件携带”？

可以，而且 Host 设计上就是“让插件决定引擎如何安装/如何调用”。

具体原因与当前实现：

1. Host 不参与引擎的下载/打包/路径定位
   - Host 只提供 `TempJobDir`、`InputPath`、`TargetFormatId`、`SelectedConfig` 给插件
   - 插件可以自行执行系统 `ffmpeg`，也可以执行自己随插件打包的 `ffmpeg.exe`
2. 插件安装（zip）会复制“manifest 所在目录的整棵目录树”
   - `PluginManagerViewModel.InstallFromZipAsync()` 解压 zip 到临时目录
   - 找到唯一的 `manifest.json`
   - 以 `manifest.json` 所在目录为源目录，把整个目录递归复制到 `AppContext.BaseDirectory/plugins/<pluginId>/`

因此：你可以在 zip 里把 `ffmpeg.exe`、`ffmpeg` 依赖 dll/资源文件一起放在 `<pluginId>/` 目录结构中，只要你的插件运行时能用正确的路径找到它即可。

仍需注意一条硬约束：

- 若你的插件在取消时会终止外部进程树（要求），则 manifest 必须设置 `supportsTerminationOnCancel=true`，否则 Host 会跳过安装/加载该插件。

## 7. 目前实现的一个重要使用注意点（混合扩展名批处理）

当前 Host 的配置 UI 是“按列表中第一个输入文件选激活插件”：

- `ReloadPluginContext()` 使用 `InputFiles` 中第一项的路径来决定激活插件，从而决定配置控件与目标格式选择

但执行时（串行/并行）是“对每个输入文件单独路由到匹配插件”：

- `RunSerialAsync/RunParallelAsync` 对每个输入都会用 `PluginRouter` 找对应插件

这意味着当你的输入列表包含不同扩展名、对应不同插件时：

- UI 的 `SelectedConfig` / `SelectedTargetFormat` 可能并不完全符合其它插件的期望

如果你有这种用法需求，后续可以把 Host 升级为“按插件分组批处理（每组独立配置）”，但当前版本的行为就是以上事实。

## 8. 开发者如何部署用于开发插件（本地调试）

> 说明：插件开发的核心目标是：让你写出来的插件 `zip` 能被本地运行的 `Host.exe` 安装并成功执行。
> 因此你需要准备一个“可运行 Host 环境”，然后反复用 Host 的“添加插件”来安装/替换你的插件 zip。

### 8.1 准备可运行的 Host（两种方式）

方式 A：使用你已经发布/编译好的 Host Release（最省事）
- 直接从你的发行产物里拿到一个可运行目录（里面包含 `Host.exe`、`locales/`，以及可选的 `plugins/`）。

方式 B：本地从源码发布 Host（建议用于你会修改 Host 协议/行为时）

```powershell
cd d:\Projects\ConverTool
dotnet publish .\Host\Host.csproj -c Release -r win-x64 --self-contained false -o .\publish\win-x64
```

> Host 运行时会扫描：`AppContext.BaseDirectory/plugins/**/manifest.json`。所以你的 Host 目录下会有一个 `plugins/` 文件夹（初始可空）。

### 8.2 插件开发的最小依赖

- 插件实现 `PluginAbstractions.IConverterPlugin`
- 插件提供 `manifest.json`（声明 `pluginId`、`assembly`、`type`、`supportedInputExtensions`、`supportedTargetFormats`、`configSchema`、`supportsTerminationOnCancel` 等）

在你自己的插件工程里，建议引用同一个契约包：`ConverTool.PluginAbstractions`（具体 NuGet 源/Token 见 `docs/plugin-dev.md`）。

### 8.3 把插件打包成 zip 并安装到本地 Host

1) 按约定打包插件目录为 zip（要求：zip 解压后目录树里能找到且只能找到一个 `manifest.json`）。

2) 打开本地 `Host.exe`：
- “插件管理” → “添加插件” → 选择你的插件 zip

3) Host 会把你 zip 里 `manifest.json` 所在目录的整棵目录复制到：
`AppContext.BaseDirectory/plugins/<pluginId>/`

> 因此 zip 里可以携带 ffmpeg 等外部引擎文件（放在你的插件目录结构下），只要你的插件运行时能用正确路径找到它们。

### 8.4 调试建议（快速定位问题）

- `KeepTemp`：建议调试时打开（保留每次任务的 `TempJobDir`），便于你检查插件产物是否写进了 `TempJobDir`。
- `reporter.OnLog`：插件回传的日志会显示在 Host 的处理框，先用日志定位“参数是否读取正确/外部进程是否启动/输出文件是否存在”。

### 8.5 开发者交付给自己的“本地测试清单”

- 一个可运行 Host 目录：包含 `Host.exe` 和 `locales/`（可选 `plugins/`）
- 你正在开发的插件 `zip`（每次修改后重新打包安装）
- （可选）测试输入文件 + 命名模板/输出目录设置

## A. Host 源码目录速查（维护用）

- `Host/ViewModels/`：主窗口与插件管理 VM；用户设置持久化在 `MainWindowViewModel`（`%LocalAppData%\ConverTool\user-settings.json`）
- `Host/Plugins/`：`PluginCatalog` / `PluginRouter` / **`PluginZipInstaller`**（主窗口与插件管理共用的 zip 安装逻辑）
- `Host/Settings/`：`UserSettingsStore`（JSON 读写）
- `plugins-src/`：仓库内示例插件源码（构建产物见 `.gitignore`，勿提交 `bin/obj/out/dist`）



</details>

