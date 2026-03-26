# ConverTool

> **当前主线版本：Host v1.2.2**（契约包 `ConverTool.PluginAbstractions` **1.1.0**；内置插件的 manifest `version` 与 Host 一致，见 [`plugin-dev.md`](plugin-dev.md)；历史版本说明见 [`docs/releases/`](releases/)。）

ConverTool 是一个"轻量化的跨平台文件转换器骨架"：它用 Avalonia 做 UI，并通过动态加载的插件（.NET DLL）来完成真正的转换逻辑。Host 负责把输入文件路由到对应插件、提供配置 UI、创建临时目录、接收插件日志/进度/完成信息，并将临时产物按命名模板移动到最终输出目录。

**仓库约定**：桌面应用（本仓库）与 **插件契约库** 分属**两个 Git 仓库**；契约以 NuGet **`ConverTool.PluginAbstractions`** 形式供插件开发者使用。详见 **[repositories.md](./repositories.md)**。

---

## 使用方法（普通用户）

1. 启动 ConverTool
2. 插件：打开"插件管理" -> "添加插件"安装你的 `*.zip` 插件
3. 输入：点击「浏览」选择文件，或将文件**拖拽到输入区**；已选文件以**图标 + 文件名**列表显示，可点「×」移除
4. 配置与目标：根据插件提供的目标格式选择输出，并填写配置项（部分选项会随**目标格式**自动显示或隐藏，见 [`plugin-dev.md`](plugin-dev.md) `visibleForTargetFormats`）
5. 输出：设置输出目录/命名模板，点击"Start"
6. 日志与结果：处理过程会在日志区域实时显示，完成后结果会列出到输出区域

---

## 内置插件

发行版与源码中的 `Host/plugins/`（或安装目录下的 `plugins/`）**默认包含**三个插件：

| 插件 | 功能 | 依赖 |
|------|------|------|
| **视频转换器** | 视频转码（MP4 / MKV / MOV / WebM / AVI 等），支持分辨率、帧率、画质调整 | FFmpeg（自动下载或使用系统 PATH） |
| **图片转换器** | 图片格式互转（PNG、JPEG、WebP、TIFF、ICO、AVIF 等），支持文件大小控制 | ImageMagick（自动下载或使用系统 PATH） |
| **文档转换器** | 文档格式互转（Markdown、Word、HTML、EPUB、PDF 等） | Pandoc + LibreOffice（自动下载或使用系统 PATH） |

### 共享工具缓存

**v1.1.0 新增**：所有插件共享统一的工具缓存目录 `%LOCALAPPDATA%\ConverTool\tools\`，避免重复下载同一工具。

---

## 技术文档

| 文档 | 说明 |
|------|------|
| **[plugin-dev.md](plugin-dev.md)** | 插件开发规范：manifest 字段、i18n、configSchema、**`visibleIf` / `visibleForInputExtensions` / `visibleForTargetFormats`**、**`config.json` / `persistValue`**、共享工具缓存 |
| [releases/PluginAbstractions-v1.1.0.md](releases/PluginAbstractions-v1.1.0.md) | 契约包 **1.1.0** 变更摘要（`ConfigField.VisibleForTargetFormatIds` / manifest `visibleForTargetFormats`） |
| [technical/IMPLEMENTATION.md](technical/IMPLEMENTATION.md) | Host 实现细节：架构、插件加载、配置 UI、**`SettingManager` 与 `config.json`**、批处理、进度映射等 |
| [technical/README.md](technical/README.md) | 技术文档索引 |
| [repositories.md](repositories.md) | 仓库边界说明 |

---

## 快速链接

- **Host 仓库**：<https://github.com/kckc7887/ConverTool>
- **契约仓库**：<https://github.com/kckc7887/ConverTool-PluginAbstractions>
