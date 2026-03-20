# ConverTool 技术实现详解

> **目的**：以当前仓库代码为准，记录架构、数据流、算法与实现细节，便于维护、二次开发与问题定位。  
> **范围**：Host（Avalonia）、与 `ConverTool.PluginAbstractions` 契约的交互、插件加载与执行、动态配置 UI、用户设置、批处理与进度、命名与落盘。  
> **版本**：与 Host **v1.0.2**、`ConverTool.PluginAbstractions` **1.0.2** 及本仓库 manifest/schema 对齐；若代码变更，请以实际源码为准。  
> **契约库仓库**：`PluginAbstractions` 类型定义在**独立 Git 仓库**中发布（NuGet 包）；**不是**本 Host 仓库的一部分，见 **[../repositories.md](../repositories.md)**。

---

## 目录

1. [仓库与模块边界](#1-仓库与模块边界)  
2. [进程启动与全局服务](#2-进程启动与全局服务)  
3. [插件发现：`PluginCatalog`](#3-插件发现plugincatalog)  
4. [插件路由：`PluginRouter`](#4-插件路由pluginrouter)  
5. [运行时加载：`PluginRuntimeLoader` 与 `AssemblyLoadContext`](#5-运行时加载pluginruntimeloader-与-assemblyloadcontext)  
6. [Manifest 模型：JSON 与 Host 反序列化](#6-manifest-模型json-与-host-反序列化)  
7. [从 Zip 安装插件：`PluginZipInstaller`](#7-从-zip-安装插件pluginzipinstaller)  
8. [主窗口 ViewModel：职责总览](#8-主窗口-viewmodel职责总览)  
9. [配置 UI 动态生成与 `ConfigFieldVm` 类型映射](#9-配置-ui-动态生成与-configfieldvm-类型映射)  
10. [UI 规则引擎：`visibleIf`、`fieldBoolRelations`、目标格式过滤](#10-ui-规则引擎visibleiffieldboolrelations目标格式过滤)  
11. [持久化快照覆盖：`fieldPersistOverrides`](#11-持久化快照覆盖fieldpersistoverrides)  
12. [目标大小单位与动态标签（ImageMagick 相关 Host 逻辑）](#12-目标大小单位与动态标签imagemagick-相关-host-逻辑)  
13. [用户设置：`UserSettingsStore` 与防抖保存](#13-用户设置usersettingsstore-与防抖保存)  
14. [激活插件解析：`ResolveActivePlugin`](#14-激活插件解析resolveactiveplugin)  
15. [批处理执行：串行 `RunSerialAsync`](#15-批处理执行串行-runserialasync)  
16. [批处理执行：并行 `RunParallelAsync`](#16-批处理执行并行-runparallelasync)  
17. [暂停、取消与停止](#17-暂停取消与停止)  
18. [进度映射：`MapToOverallPercent` 与批次公式](#18-进度映射maptooverallpercent-与批次公式)  
19. [`VmReporter` 与插件回传](#19-vmreporter-与插件回传)  
20. [落盘：`MoveToFinalOutput` 与 `ResolveConflict`](#20-落盘movetofinaloutput-与-resolveconflict)  
21. [命名模板：Tag UI、字符串拼接与占位符替换](#21-命名模板tag-ui字符串拼接与占位符替换)  
22. [日志管线：队列与 UI 节流](#22-日志管线队列与-ui-节流)  
23. [i18n：Host 与插件语言包](#23-i18nhost-与插件语言包)  
24. [插件管理窗口与目录刷新](#24-插件管理窗口与目录刷新)  
25. [安装器与发布（概要）](#25-安装器与发布概要)  
26. [已知限制与设计取舍](#26-已知限制与设计取舍)  
27. [实现演进与当前能力清单](#27-实现演进与当前能力清单)  
28. [插件与 Host 的兼容性与版本策略（Host 开发规范）](#28-插件与-host-的兼容性与版本策略host-开发规范)  

---

## 1. 仓库与模块边界

| 模块 | 路径 / 包 | 职责 |
|------|-------------|------|
| **Host** | `Host/` | Avalonia 桌面壳：插件目录扫描、配置 UI、任务调度、进度/日志、输出移动。 |
| **PluginAbstractions** | **独立仓库**；NuGet：`ConverTool.PluginAbstractions`；本地开发可为平级目录 `PluginAbstractions/`（`.gitignore`，见 [repositories.md](../repositories.md)） | 仅类型契约：`IConverterPlugin`、`ExecuteContext`、`PluginManifest` 等；**不含** Host 的 JSON manifest 全部扩展字段。 |
| **插件源码** | `plugins-src/*` | 示例/内置插件实现；构建产物通常同步到 `Host/plugins/<pluginId>/` 或由脚本发布；**自带插件** `manifest.json` 的 `version` 与 Host 版本对齐（见 `plugin-dev.md` §3.1）。 |
| **文档** | `docs/` | 用户向 `plugin-dev.md`；本文件为技术向实现说明。 |
| **安装器** | `installer/` | Inno Setup 脚本、资源、构建脚本（见第 25 节）。 |

**依赖关系**：插件作者通过 **NuGet** 引用契约包；本仓库内 Host / `plugins-src` 使用 **`ProjectReference`** 指向本地 clone 的 `PluginAbstractions/` 以便联调，该目录**不提交**。插件 DLL 与 Host 共用同一契约程序集加载约定（见第 5 节）。

---

## 2. 进程启动与全局服务

**入口**：`Host/Program.cs` → `Main`。

1. **尽早加载插件目录**  
   `AppServices.Plugins = PluginCatalog.LoadFromOutput(AppContext.BaseDirectory)`  
   - `BaseDirectory` 一般为 `Host.exe` 所在目录。  
   - 随后 `PrintSummary()` 向 `Console`/`Trace` 输出发现的插件（便于无 UI 排错）。

2. **可选 CLI：`route`**  
   若命令行参数为 `route <path>`，则打印 `PluginRouter.RouteByInputPath` 结果并退出 Avalonia 启动前的逻辑分支（用于快速验证扩展名路由）。

3. **异常落盘**  
   启动失败时写入 `AppContext.BaseDirectory/startup-error.log`，避免 WinExe 无控制台时无法看到异常。

4. **`AppServices` 静态门脸**（`Host/AppServices.cs`）  
   - `I18n`：`I18nService` 单例（Host 文案）。  
   - `Plugins`：`PluginCatalog`（可被主窗口在插件增删后重新赋值）。  
   - `PluginI18n`：`PluginI18nService`（按插件目录加载 `locales/*.json`）。

---

## 3. 插件发现：`PluginCatalog`

**文件**：`Host/Plugins/PluginCatalog.cs`

- 扫描路径：`Directory.GetFiles(pluginsDir, "manifest.json", SearchOption.AllDirectories)`，即 **`plugins/**/manifest.json`**。  
- 反序列化：`JsonSerializer.Deserialize<PluginManifestModel>(json, PluginZipInstaller.ManifestJsonOptions)`（**属性名大小写不敏感**）。  
- **硬门槛**：`supportsTerminationOnCancel == false` 或缺失 → **跳过**该 manifest，并打印原因。  
- 产出：`PluginEntry(PluginDir, ManifestPath, Manifest)` 列表；`PluginDir` = manifest 所在目录。

**设计意图**：先扫描、后按需加载 DLL，避免启动时加载全部程序集。

---

## 4. 插件路由：`PluginRouter`

**文件**：`Host/Plugins/PluginRouter.cs`

**算法** `RouteByInputPath(catalog, inputPath)`：

1. `Path.GetExtension` → 去掉前导 `.` → `Trim` → **`ToLowerInvariant()`** 得到规范扩展名。  
2. 在 `catalog.Plugins` 中筛选：`SupportedInputExtensions` 中存在**忽略大小写**相等项。  
3. 排序：  
   - 主键：`SupportedInputExtensions.Length` **升序**（扩展名列表更短的插件优先，减少“宽泛匹配”抢任务）。  
   - 次键：`PluginId` 忽略大小写。  
4. 取 `FirstOrDefault()`。

---

## 5. 运行时加载：`PluginRuntimeLoader` 与 `AssemblyLoadContext`

**文件**：`Host/PluginRuntimeLoader.cs`

### 5.1 `PluginLoadContext`

- 继承 `AssemblyLoadContext`，**`isCollectible: true`**，支持卸载。  
- 使用 `AssemblyDependencyResolver(pluginAssemblyPath)` 解析依赖与原生 DLL。  
- **`Load(AssemblyName)` 特判**：若程序集名为 `PluginAbstractions`，**返回 `null`**，使插件使用 **默认上下文** 中已由 Host 加载的契约程序集，避免类型身份不一致。

### 5.2 `TryLoadPlugin(PluginEntry entry)`

1. `assemblyPath = Path.Combine(entry.PluginDir, entry.Manifest.Assembly)`，必须存在。  
2. 新建 `PluginLoadContext`，`LoadFromAssemblyPath`。  
3. `asm.GetType(entry.Manifest.Type, throwOnError: false)`：类型必须存在且实现 `IConverterPlugin`。  
4. `Activator.CreateInstance` → `PluginLoadHandle(IConverterPlugin, alc)`。

### 5.3 `PluginLoadHandle.Dispose`

- 调用 `AssemblyLoadContext.Unload()`。  
- **刻意不在此处 `GC.Collect`**：注释说明若在每次任务同步强制 GC，会导致 UI 线程卡顿及 Stop/批处理时的硬故障；可回收 ALC 由运行时自然回收。

### 5.4 生命周期

- **串行**：每个输入文件 `using var handle = TryLoadPlugin`，执行完即 Dispose。  
- **并行**：每个并行任务同样独立 `using`，互不共享实例。

---

## 6. Manifest 模型：JSON 与 Host 反序列化

**文件**：`Host/PluginManifestModel.cs`

Host 使用 **独立 DTO** 与磁盘 JSON 对齐，字段包括：

- 根级：`pluginId`、`version`、`assembly`、`type`、`supportedInputExtensions`、`supportedTargetFormats`、`configSchema`、`supportedLocales`、`i18n`、`titleKey`、`descriptionKey`、`author`、`supportsTerminationOnCancel`。  
- `TargetFormatModel`：`id`、`displayNameKey`、`descriptionKey`、**`visibleIf`**。  
- `ConfigSchemaModel`：`sections`、`fieldBoolRelations`、**`fieldPersistOverrides`**。  
- `ConfigFieldModel`：`key`、`type`、`labelKey`、`helpKey`、`defaultValue`（`JsonElement?`）、`options`、`range`、`path`、**`visibleIf`**。  
- `VisibleIfModel`：JSON 键 **`equals`** → C# 属性 **`Expected`**（避免与 `object.Equals` 冲突）。  
- `FieldBoolRelationModel`：`if`、`then`、`applyWhen`。  
- `FieldPersistOverrideModel`：`when`（`VisibleIfModel`）、`fields`（`Dictionary<string, JsonElement>`）。

**与 `PluginAbstractions.PluginManifest` 的差异**：代码里 `GetManifest()` 返回的记录类型**字段更少**；**运行时以 JSON manifest 为准**（配置 schema 扩展、visibleIf 等）。

---

## 7. 从 Zip 安装插件：`PluginZipInstaller`

**文件**：`Host/Plugins/PluginZipInstaller.cs`

**流程** `InstallFromZipAsync(zipPath, baseDirectory)`：

1. 校验 `.zip` 扩展名。  
2. 解压到临时目录 `Path.GetTempPath()/ConverToolPluginInstall/<guid>/`。  
3. 递归查找 **`manifest.json` 恰好一个**；否则返回 `MANIFEST_NOT_FOUND` / `MANIFEST_NOT_UNIQUE`。  
4. 反序列化；`pluginId` 必填；**`supportsTerminationOnCancel` 必须为 true**，否则 `MISSING_TERMINATION_SUPPORT`。  
5. 目标目录：`baseDirectory/plugins/<pluginId>/`；若已存在则带重试删除后整树复制（`manifest.json` 所在目录为根）。  
6. 错误码常量：`InvalidZip`、`FilesInUse`、`FilesLocked`、`Unknown` 等。

**用途**：主窗口与插件管理器共用，保证与手动复制 `plugins/` 目录结构一致。

---

## 8. 主窗口 ViewModel：职责总览

**文件**：`Host/ViewModels/MainWindowViewModel.cs`（体量最大）

核心职责：

- 维护 **`ObservableCollection<InputFileItemVm> InputFiles`**（拖拽/添加/移除）。  
- 维护 **`PluginCatalog` 本地副本 `_catalog`**（与 `AppServices.Plugins` 同步）。  
- **`ReloadPluginContext()`**：根据**当前激活插件**重建 `ConfigFields`、`TargetFormats`、规则缓存，并恢复用户设置。  
- **`StartAsync`**：创建 `CancellationTokenSource`，串行或并行执行转换。  
- 绑定命令：开始、暂停、停止、浏览输出目录、插件安装、语言切换等。

---

## 9. 配置 UI 动态生成与 `ConfigFieldVm` 类型映射

**入口**：`ReloadPluginContext()` 中遍历 `configSchema.sections[].fields[]`。

**类型映射**（`field.Type` 字符串，缺省按 `Text`）：

| JSON `type` | ViewModel 类型 | 说明 |
|-------------|----------------|------|
| `Checkbox` | `CheckboxFieldVm` | `defaultValue` → `TryGetBoolDefault` |
| `Select` | `SelectFieldVm` | `options` → `OptionVm` 列表；`defaultValue` 匹配 `id` |
| `Path` | `PathFieldVm` | `path.kind` → `File`/`Folder` |
| `Range` | `RangeFieldVm` | `range.min/max/step` |
| `Number` | `NumberFieldVm` | 同 Range；UI 为 Spinbox |
| 其他 | `TextFieldVm` | |

**XAML**：`MainWindow.axaml` 中通过 `DataTemplate` 按运行时类型选择控件；`Checkbox` 使用 **`IsChecked` 双向绑定**以驱动 ViewModel。

---

## 10. UI 规则引擎：`visibleIf`、`fieldBoolRelations`、目标格式过滤

**状态字段**：`_visibleIfByFieldKey`、`_fieldBoolRelations`、`_allTargetFormatModels`。

**`ReevaluateUiRules(string? changedFieldKey)`**（重入保护 `_isReevaluatingUiRules`）：

1. **`fieldBoolRelations`**（**非** `applyWhen: "save"`）：仅当 `rel.If.FieldKey == changedFieldKey` 时评估；若条件满足则设置 `Then.TargetKey` 对应复选框。  
   - *注：`applyWhen: "save"` 在当前实现中被跳过，持久化联动请用 `fieldPersistOverrides`。*  

2. **`visibleIf`**：对每个 `ConfigFieldVm`，若存在规则，则 `field.IsVisible = (控制复选框值 == Expected)`。  

3. **`ApplyTargetSizeUnitConversionIfNeeded` / `UpdateTargetSizeLabels`**：与图片插件 `targetSizeUnit` 联动时，在切换 KB/MB 时换算数值并刷新标签括号内单位（见第 12 节）。  

4. **`RefreshTargetFormatsByVisibility()`**：对 `_allTargetFormatModels` 逐项应用 `visibleIf`；重建 `TargetFormats` 集合；若当前选中 id 不在可见列表则回退到第一项。

---

## 11. 持久化快照覆盖：`fieldPersistOverrides`

**配置位置**：`configSchema.fieldPersistOverrides[]`，每项含 `when` + `fields`。

**实现要点**（`CaptureCurrentUserSettings` / `RestorePluginUiFromSettings` / `ApplyFieldPersistOverrides*`）：

- **保存**：先按 `ConfigFields` 常规序列化到 `PluginUserSettings.Fields`，再对匹配 `when` 的规则，用 `JsonElement` 转成字符串**覆盖**指定 key（磁盘上的「下次启动」状态）。  
- **恢复加载后**：再次对匹配规则把值应用回 VM（兼容旧文件、与快照一致）。  
- **不**在保存瞬间强行改内存中的「当前会话」勾选（由规则语义保证：覆盖的是快照）。

**典型用例**：未勾选「保留某设置」时，会话内仍可保持「启用」；写入磁盘的 `enable*` 为 false，下次启动折叠。

---

## 12. 目标大小单位与动态标签（ImageMagick 相关 Host 逻辑）

当 manifest 中存在字段 `targetSizeUnit`、`targetSizeMinKb`、`targetSizeMaxKb` 时：

- **`ApplyTargetSizeUnitConversionIfNeeded`**：在 `targetSizeUnit` 变化时，在 KB/MB 间换算 `NumberFieldVm` 的数值（防重入 `_isConvertingTargetSizeUnit`）。  
- **`UpdateTargetSizeLabels`**：在基础 label（`_baseLabelByFieldKey`）后拼接当前单位（中/英括号差异）。  

此为 **Host 内针对字段 key 的约定实现**，其他插件若复用相同 key 可获得相同行为。

---

## 13. 用户设置：`UserSettingsStore` 与防抖保存

**文件**：`Host/Settings/UserSettingsStore.cs`

- **路径**：`%LocalAppData%/ConverTool/user-settings.json`。  
- **选项**：驼峰命名、忽略 null、允许注释、尾随逗号等。  
- **`UserSettingsFile`**：`locale`、`outputDir`、`useInputDirAsOutput`、`namingTemplate`、`enableParallelProcessing`、`parallelism`、`keepTemp`、`plugins[pluginId]`（`targetFormatId` + `fields` 字典）。  
- **不持久化输入文件列表**（`InputPaths` 在保存时置空）。

**防抖**：`MainWindowViewModel` 中 `ScheduleSaveUserSettings` 使用 **`DispatcherTimer` 500ms**；多次变更合并为一次 `FlushSaveUserSettings` → `UserSettingsStore.Save`。

---

## 14. 激活插件解析：`ResolveActivePlugin`

```text
firstInput = InputFiles.FirstOrDefault()?.FullPath
若为空 → return null
否则 return PluginRouter.Route(firstInput) ?? catalog.Plugins.FirstOrDefault()
```

**含义**：

- **无输入文件**时无「路由依据」，返回 `null`（配置区可能为空或占位）。  
- 有输入时按扩展名路由；若无匹配则退化为目录中**第一个**插件（避免 UI 完全空白）。

**与批处理的关系**：执行时**每个文件**单独 `RouteByInputPath`；UI 的 `ConfigFields` / `SelectedTargetFormat` 来自**激活插件**，可能与后续文件实际所用插件不一致（见第 26 节）。

---

## 15. 批处理执行：串行 `RunSerialAsync`

对每个 `inputPaths[i]`：

1. `WaitIfPausedAsync(ct)`（见第 17 节）。  
2. `PluginRouter` → `TryLoadPlugin` → 创建 **`tempJobDir = %Temp%/ConverTool/<jobId>/<index>/`**。  
3. `targetFormatId`：`SelectedTargetFormat?.Id` → 否则 manifest 第一个格式 → 否则 `"txt"`。  
4. `selectedConfig`：`ConfigFields.ToDictionary(f => f.Key, f => f.GetValue())`。  
5. `ExecuteContext`：`OutputNamingContext` 含 **`base`（无扩展名）、`index`（1-based）、`ext`（目标格式 id）**。  
6. `VmReporter`：`OnCompleted` 时 `Path.Combine(tempJobDir, OutputRelativePath)` → `MoveToFinalOutput`。  
7. `KeepTemp == false`：`TryDeleteDirectory(tempJobDir)`。  
8. 进度：`MapBatchToOverallPercent(completed, total, perFile)`，`completed` 在本文件处理前计数。

**批末**：`InputFiles.Clear()`（若未取消）。

---

## 16. 批处理执行：并行 `RunParallelAsync`

- **`Parallelism`**：`Math.Clamp(Parallelism, 1, 8)`。  
- **`SemaphoreSlim(maxConcurrency)`** 控制并发。  
- 每任务独立：`Route`、**独立** `PluginLoadHandle`、独立 `tempJobDir`、独立 reporter。  
- **总体进度**：`perFilePercents[]` 保存每个文件 0–100；`overall = round(sum(perFilePercents)/total)`。  
- 日志前缀：`[{index}/{total}]` 区分并行交错输出。

---

## 17. 暂停、取消与停止

- **`_pauseGate`**：`ManualResetEventSlim`（或同类）；**暂停**在 `WaitIfPausedAsync` 中阻塞工作流，**在「下一个文件」开始前**生效（串行循环每轮开头等待；并行任务在获取信号量后等待）。  
- **`StopAsync`**：`_runCts.Cancel()`；若处于暂停则 **`Set()` 暂停门**，避免工作线程永久阻塞在 `Wait`。  
- **`StartAsync`**：新建 `CTS`，重置 `_paused` 并 `Set()` 暂停门。

---

## 18. 进度映射：`MapToOverallPercent` 与批次公式

**单文件阶段 → 总进度 0–100**：

| `ProgressStage` | 公式（`p = Clamp(percentWithinStage ?? 0, 0, 100)`） |
|-----------------|------------------------------------------------------|
| `Preparing` | `round(0 + 10 * p/100)` |
| `Running` | `round(10 + 80 * p/100)` |
| `Finalizing` | `round(90 + 10 * p/100)` |

**串行批次**：`MapBatchToOverallPercent(completedFilesBeforeThis, totalFiles, currentFilePercent)`  
`overall = ((completed + currentFilePercent/100) / totalFiles) * 100`（概念上：已完成文件算 100%，当前文件贡献部分百分比）。

---

## 19. `VmReporter` 与插件回传

**定义**：`MainWindowViewModel.cs` 文件末尾 `internal sealed class VmReporter : IProgressReporter`，简单委托到 Host 提供的四个 `Action`。

**插件契约**：成功必须 `OnCompleted` 且相对路径文件存在；失败 `OnFailed`；进度可选。

---

## 20. 落盘：`MoveToFinalOutput` 与 `ResolveConflict`

**输出目录**：

- `UseInputDirAsOutput` → 输入文件所在目录。  
- 否则 `OutputDir`；若为空 → `AppContext.BaseDirectory/output`。

**文件名**：

- 从 `NamingTemplate` 字符串做占位符替换（`{ext}` 为目标格式 id）：  
  **`{base}`**（输入主文件名）、**`{ext}`**、**`{index}`**（1 基序号）、**`{timeYmd}`**（`yyyy-MM-dd`）、**`{timeHms}`**（`HH-mm-ss`，与 Host 文案一致）。  
- 若结果不以 `.<targetExt>` 结尾则追加。  
- 非法文件名字符替换为 `_`。  

**冲突**：`ResolveConflict`：若存在则尝试 `name_(i).ext`，`i` 从 1 到 9999。  

**移动**：优先 `File.Move(..., overwrite:false)`；`IOException` 时 **Copy + Delete** 临时文件。

---

## 21. 命名模板：Tag UI、字符串拼接与占位符替换

**UI 层**：`NamingTemplateSelectedTags` 为有序标签；`RecomputeNamingTemplateFromSelectedTags` 用 **`string.Join("_", ...)`** 生成持久化字符串 **`NamingTemplate`**（标签之间为 **下划线 `_`**）。

**占位符**：`MoveToFinalOutput` 替换 **`{base}`、`{ext}`、`{index}`、`{timeYmd}`、`{timeHms}`**；`ExecuteContext.OutputNamingContext` 提供键 **`base`、`index`、`ext`、`timeYmd`、`timeHms`**（后两者为字符串，与替换格式一致），供插件只读。

**`NormalizeNamingTemplate`**：去掉历史写法中的 `{ext}` / `.{ext}`，因 UI 右侧固定展示目标后缀。

**`NamingTemplateTokenVm.DisplayText`**：对 `{base}`/`{index}`/`{timeYmd}`/`{timeHms}` 显示本地化候选文案，其它值原样显示。

---

## 22. 日志管线：队列与 UI 节流

- **`ConcurrentQueue<string> _pendingLogLines`** + **`List<string> _logLines`**。  
- **`EnqueueLog`** → `Dispatcher.UIThread.Post(EnsureLogFlushTimer)`。  
- **`DispatcherTimer` 100ms** 批量 `FlushLogs`，合并为 `ProcessLog` 字符串。  
- **上限**：超过 `MaxLogLines`（2000）则删除最旧行。

目的：避免高频 `OnLog` 触发过多 UI 属性变更。

---

## 23. i18n：Host 与插件语言包

- **Host**：`Host/locales/<locale>.json`，结构 `{ "strings": { "key": "text" } }`。  
- **插件**：`plugins/<pluginId>/<localesFolder>/<locale>.json`，同上。  
- **`PluginI18nService`**：按插件目录加载；回退 **`en-US`**；再不行显示 key。  
- **语言切换**：更新 `MainWindowViewModel` 中大量 `RaisePropertyChanged` 与 `ReloadPluginContext` + 重建命名模板显示。

---

## 24. 插件管理窗口与目录刷新

- **`PluginManagerViewModel`**：调用 `PluginZipInstaller`，成功后通知主窗口 `ReloadCatalog()`。  
- **`MainWindowViewModel.ReloadCatalog()`**：重新 `PluginCatalog.LoadFromOutput`，写回 `AppServices.Plugins`，`ReloadPluginContext()`。

---

## 25. 安装器与发布（概要）

- **`installer/ConverTool.iss`**：Inno Setup 脚本（`AppVersion` 与 `Host`、`build-installer.ps1` 参数对齐）。  
- **`installer/scripts/build-installer.ps1`**：publish full/lite + 编译安装包 → `artifacts/ConverTool-v<版本>-setup.exe`。  
- **`installer/scripts/package-portable-zips.ps1`**：在上一脚本产出 `artifacts/host/v<版本>/...` 后，打 full/lite 便携 zip。  
- **`installer/scripts/verify-dev-setup.ps1`**：校验平级 `PluginAbstractions/` 存在后，构建 Host 与 `plugins-src` 示例插件（联调约定见 `docs/repositories.md` §8）。  
- **`installer/Languages/ChineseSimplified.isl`** 等：安装界面语言。  
- （可选）仓库根 **`NuGet.Config`**：限定 `nuget.org`，避免本机全局 NuGet 源配置导致还原失败。

详细步骤以 **`installer/README.md`** 为准；本文件不展开向导页配置。

---

## 26. 已知限制与设计取舍

1. **混合扩展名批处理**：配置 UI 仅反映**激活插件**（由列表**第一个**输入文件决定）；每个文件执行时仍独立路由，故多插件混排时 **UI 配置可能不匹配**部分文件。  
2. **`fieldBoolRelations` + `applyWhen: save`**：当前不在保存路径自动应用；请用 **`fieldPersistOverrides`** 表达持久化语义。  
3. **插件卸载**：`AssemblyLoadContext` 卸载后不强制 GC，依赖 CLR 回收；长时间开发可重启进程验证隔离。  
4. **命名模板**：落盘替换支持 `{base}`/`{ext}`/`{index}`/`{timeYmd}`/`{timeHms}`；自定义 token 仍须与 Host 替换链一致或在未来扩展。

---

## 27. 实现演进与当前能力清单

**已实现**（主线）：

- 插件目录扫描、zip 安装、collectible 加载、按扩展名路由。  
- 动态配置：`Checkbox`/`Select`/`Text`/`Path`/`Range`/`Number`、`visibleIf`、目标格式 `visibleIf`。  
- `fieldPersistOverrides`、`fieldBoolRelations`（即时）、目标大小单位换算与标签（约定字段名）。  
- 串行/并行、暂停（文件边界）、取消、进度映射、日志节流。  
- 用户设置持久化、命名模板 Tag UI（拖拽排序；`{timeYmd}` / `{timeHms}` 与 `OutputNamingContext`）、输出冲突处理。  

**插件侧**：具体转换算法（ImageMagick CLI、FFmpeg 等）在各自 `ExecuteAsync` 内实现，不在本文展开；请参阅 `plugins-src/*/README.md` 或源码。

---

## 28. 插件与 Host 的兼容性与版本策略（Host 开发规范）

本节约定 **Host 维护者**在演进 manifest、契约与加载逻辑时，对「旧插件能否在新 Host 上运行」的预期；插件作者面向说明仍见 **[`plugin-dev.md`](../plugin-dev.md)**。

### 28.1 无「版本协商」协议

- **`manifest.json` 中的 `version`**：仅作插件自身版本标识（展示等），**不参与**与 Host 程序版本的比较或兼容层分支。  
- **不存在** `minHostVersion`、`pluginApiVersion`、Host↔插件握手等机制；兼容与否由**当前**反序列化模型 + 加载校验 + **`.NET` 契约类型**共同决定。

### 28.2 JSON manifest 的宽松程度

- 反序列化使用 **`System.Text.Json`**（`PluginZipInstaller.ManifestJsonOptions` 等），**未**将未知属性视为错误；**多出来的键**一般会被忽略。  
- **缺失的键**使用 `PluginManifestModel` 等类型的**默认值**（例如布尔缺省多为 `false`）。  
- 因此：在**不收紧校验**的前提下，为 manifest **新增可选字段**通常**不**要求旧插件立刻改 JSON。

### 28.3 硬性校验（不满足则拒绝加载 / 安装）

Host **不会**为「旧插件」单独保留旁路；下列为当前实现中的典型门槛（维护者**新增**类似门槛 = 潜在破坏性变更）：

| 项 | 行为要点 |
|----|----------|
| **`supportsTerminationOnCancel`** | 必须为 **`true`**；为 `false` 或缺失时，`PluginCatalog` **跳过**该 manifest；安装流程亦有同类校验。 |
| **反序列化失败** | `catch` 后忽略该 manifest（不进入列表）。 |
| **程序集 / 类型** | `PluginRuntimeLoader` 要求磁盘上存在 `assembly` 指向的 DLL，且 `type` 可解析且实现 **`IConverterPlugin`**。 |

### 28.4 契约程序集 `PluginAbstractions`

- 插件 ALC 对名为 **`PluginAbstractions`** 的程序集 **不**从插件目录加载，而使用 **Host 默认上下文中已加载的契约**（见第 5 节），避免类型身份分裂。  
- **旧插件 DLL** 若仍针对**旧版**契约编译，只要 **CLR 仍认为**其实现了当前 Host 所带的 **`IConverterPlugin`**（接口未做破坏性变更），通常仍可加载；若契约做**破坏性**变更（接口成员、必选 record 形状等），则需插件**重新编译/发版**。  
- **契约版本发布**与 Host 发版的对齐关系见 **`docs/repositories.md`**。

### 28.5 Host 维护者检查清单（变更插件相关行为时）

1. **新增 manifest 必填语义**：评估是否会让**旧 JSON** 在反序列化后落入非法或危险默认（尤其是布尔、枚举）。  
2. **新增「跳过 / 拒绝」条件**：等价于缩小可加载插件集合，应在 **`docs/plugin-dev.md`** 与发行说明中写明。  
3. **升级 `PluginAbstractions`**：按语义化版本与迁移说明处理；破坏性变更应同步 bump 契约包与文档。  

---

*文档维护：重大行为变更时请同步更新本节与 `docs/plugin-dev.md`。*  
