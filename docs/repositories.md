# 仓库边界：Host 与 PluginAbstractions（契约库）

本文说明 **ConverTool** 在源码与发布上的**仓库分工**。这是项目自始的约定：**桌面应用 Host** 与 **插件契约库** 分属**两个 Git 仓库**；契约以 **NuGet 包** `ConverTool.PluginAbstractions` 的形式提供给**插件开发者**，与最终用户是否安装 Host 无关。

---

## 1. 两个仓库分别是什么？

| 仓库 | 内容 | 受众 |
|------|------|------|
| **Host 仓库**（本仓库） | Avalonia 桌面应用、`plugins-src` 示例插件、安装器、文档等 | 应用维护者、内置插件维护者 |
| **PluginAbstractions 仓库**（契约库，独立库） | 仅 C# 契约：`IConverterPlugin`、`ExecuteContext`、与 manifest 对齐的类型等 | **插件开发者**（通过 NuGet 引用） |

契约库 **不是** Host 仓库的子目录产物；在版本控制上 **不应** 将契约源码并入 Host 仓库（本仓库根目录 `.gitignore` 已忽略本地路径 `PluginAbstractions/`，见下）。

---

## 2. 为什么分开？

- **版本与发布节奏**：契约变更可独立发 `ConverTool.PluginAbstractions` 的 NuGet 版本，Host 与第三方插件各自对齐同一主版本即可。
- **插件开发者体验**：插件作者只需引用 NuGet，**无需** clone 整个桌面应用仓库。
- **职责清晰**：Host 负责壳与加载；契约库仅定义 Host ↔ 插件 的 .NET 边界。

---

## 3. NuGet 包（给插件开发者）

| 项 | 说明 |
|----|------|
| **包 ID** | `ConverTool.PluginAbstractions` |
| **用途** | 插件工程引用后实现 `IConverterPlugin` 等；版本需与目标 Host 文档中声明的契约版本一致。 |
| **获取方式** | 以团队发布为准：公共源（如 nuget.org）、GitHub Packages、或 Release 中的 `.nupkg`。包页或组织主页通常提供**契约仓库**链接。 |

详细字段与 `manifest.json` 对齐说明见 **[plugin-dev.md](./plugin-dev.md)**。

---

## 4. 在本仓库（Host）里如何编译：本地路径约定

Host 与 `plugins-src` 中的工程使用 **`ProjectReference`** 指向与 Host **平级**的目录：

```text
<Host 仓库根>/
  Host/
  plugins-src/
  PluginAbstractions/   ← 不提交到 Git（见 .gitignore）
    PluginAbstractions.csproj
```

**推荐做法**：

1. 单独 clone **契约仓库**到上述 `PluginAbstractions/` 路径（与 Host 仓库根目录平级、文件夹名固定为 `PluginAbstractions`），或  
2. 使用 **git submodule**（若团队允许在 Host 工作区挂接契约仓库）。

这样本地 `dotnet build` 与 IDE 多项目加载与当前 `Host.csproj` / `plugins-src` 的引用方式一致。

> **不要** 把契约源码作为普通文件提交进 Host 仓库；若需对外共享契约，在 **PluginAbstractions 仓库** 发版并推送 NuGet。

---

## 5. 与「仅从 NuGet 引用」的关系

- **插件作者的日常**：在插件 `.csproj` 中使用 **`PackageReference`** `ConverTool.PluginAbstractions`（版本与目标 Host 一致）。**不需要** clone Host 仓库。
- **Host 仓库维护者**：为便于与契约同机联调，当前采用 **本地 `ProjectReference`**；该路径对应磁盘上由**另一仓库 clone** 得到的 `PluginAbstractions/`，该目录被 **`.gitignore`**，因此不会进入 Host 的 Git 历史。

若将来 Host 改为**仅**从 NuGet 还原契约（CI 只拉取包），需在 CI 中配置可用的 NuGet 源并发布对应版本的包。

---

## 6. 发版时的分工（概要）

| 产物 | 通常在哪个仓库 / 流程 |
|------|------------------------|
| `ConverTool` 安装包、full/lite zip | Host 仓库（流程见 `installer/README.md`） |
| `ConverTool.PluginAbstractions.x.y.z.nupkg` | **契约仓库** 内对契约项目 `dotnet pack` 与打 tag；可与 Host 共用版本号约定（`.csproj` 路径以该仓库为准） |

Host 侧 Release 可选附上契约 `.nupkg` 仅为分发便利；**源码与包的主发布仍属契约仓库**。

---

## 7. 参考链接

- **Host 仓库（GitHub）**：<https://github.com/kckc7887/ConverTool>
- **契约仓库（GitHub）**：<https://github.com/kckc7887/ConverTool-PluginAbstractions>（与本地 `PluginAbstractions` 工程中的 Source Link / 远程约定一致）。
- 用户向说明仍见根目录 **[README.md](../README.md)** 与 **[docs/README.md](./README.md)**。

---

## 8. 克隆与本地编译（Host + 契约，用于 `plugins-src` / Host 联调）

以下步骤在 **Host 仓库根目录** 得到 `Host/`、`plugins-src/` 与 **平级**的 `PluginAbstractions/`（含 `PluginAbstractions.csproj`），与当前 `Host.csproj`、`plugins-src/*/*.csproj` 中的 **`ProjectReference`** 一致。

### 8.1 必须注意的目录名

从 GitHub 默认 `git clone` 契约仓库时，文件夹名会是 **`ConverTool-PluginAbstractions`**，而工程引用的是 **`PluginAbstractions`**。必须在 clone 时**指定目录名**为 `PluginAbstractions`，或 clone 后再重命名。

### 8.2 使用代理访问 GitHub（可选）

若需通过 HTTP(S) 代理访问 GitHub（如 Clash、V2 等），可在**同一终端**先设置环境变量再执行 `git clone` / `dotnet restore`（端口号按本机代理为准，常见为 `7890`）：

```powershell
$env:HTTPS_PROXY = "http://127.0.0.1:7890"
$env:HTTP_PROXY  = "http://127.0.0.1:7890"
```

也可仅为 Git 配置（不影响全局时可加 `--local` 在仓库内执行）：

```powershell
git config --global http.proxy  http://127.0.0.1:7890
git config --global https.proxy http://127.0.0.1:7890
```

取消代理：`git config --global --unset http.proxy`（及 `https.proxy`）。

### 8.3 推荐命令（在父目录中执行）

```powershell
git clone https://github.com/kckc7887/ConverTool.git
cd ConverTool
git clone https://github.com/kckc7887/ConverTool-PluginAbstractions.git PluginAbstractions
```

完成后应存在路径：

`ConverTool/PluginAbstractions/PluginAbstractions.csproj`

### 8.4 编译校验

在 `ConverTool` 根目录执行：

```powershell
dotnet build .\Host\Host.csproj -c Release
```

或使用仓库内脚本（检查契约路径并构建 Host + `plugins-src` 示例插件）：

```powershell
powershell -ExecutionPolicy Bypass -File .\installer\scripts\verify-dev-setup.ps1
```

### 8.5 仅开发独立插件（不 clone Host）

在自有解决方案中 **`PackageReference`** `ConverTool.PluginAbstractions`（版本与目标 Host 文档一致），无需上述目录布局；参见 **[plugin-dev.md](./plugin-dev.md)**。
