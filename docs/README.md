# ConverTool

> **当前主线版本：Host v1.0.2**（契约包 `ConverTool.PluginAbstractions` **1.0.2**；内置示例插件的 manifest `version` 与 Host 一致，见 [`plugin-dev.md`](plugin-dev.md)；历史版本说明见 [`docs/releases/`](releases/)。）

ConverTool 是一个“轻量化的跨平台文件转换器骨架”：它用 Avalonia 做 UI，并通过动态加载的插件（.NET DLL）来完成真正的转换逻辑。Host 负责把输入文件路由到对应插件、提供配置 UI、创建临时目录、接收插件日志/进度/完成信息，并将临时产物按命名模板移动到最终输出目录。

**仓库约定**：桌面应用（本仓库）与 **插件契约库** 分属**两个 Git 仓库**；契约以 NuGet **`ConverTool.PluginAbstractions`** 形式供插件开发者使用。详见 **[repositories.md](./repositories.md)**。

## 使用方法（普通用户）

1. 启动 ConverTool
2. 插件：打开“插件管理” -> “添加插件”安装你的 `*.zip` 插件
3. 输入：点击「浏览」选择文件，或将文件**拖拽到输入区**；已选文件以**图标 + 文件名**列表显示，可点「×」移除。
4. 配置与目标：根据插件提供的目标格式选择输出，并填写配置项
5. 输出：设置输出目录/命名模板（**v1.0.2** 起为**标签拖拽**组合，并支持日期时间片段；可选），点击“Start”
6. 日志与结果：处理过程会在日志区域实时显示，完成后结果会列出到输出区域

## 内置基础插件（默认附带）

发行版与源码中的 `Host/plugins/`（或安装目录下的 `plugins/`）**默认包含**两个插件，一般无需再单独安装即可使用：

| 插件 ID | 功能 | 使用前提 |
|---------|------|----------|
| `ffmpeg.video.transcoder` | 视频转码（MP4 / MKV / MOV / WebM / AVI 等） | 本机已安装 **ffmpeg** 并在 **PATH** 中可用。 |
| `imagemagick.image.transcoder` | 图片格式互转（PNG、JPEG、WebP、TIFF、ICO 等） | 优先使用 **PATH** 中的 `magick`；若无，插件会尝试自动下载便携 ImageMagick（详见插件实现与日志）。 |

仍可在 **插件管理** 中通过 `.zip` 安装、更新或增删其他插件。

## 技术细节（实现与架构）

完整说明见 **[`docs/technical/IMPLEMENTATION.md`](technical/IMPLEMENTATION.md)**（目录、算法、公式、源码路径、已知限制与能力清单）。索引与快速对照表见 **[`docs/technical/README.md`](technical/README.md)**。

插件 manifest / 契约字段级说明见 **[`docs/plugin-dev.md`](plugin-dev.md)**。

**Host 与契约库是否同一仓库？** 否——见 **[`docs/repositories.md`](repositories.md)**。

发版与安装包构建见 **`installer/README.md`**（含便携 zip 脚本路径）。

