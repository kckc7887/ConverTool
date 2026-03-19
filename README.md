# ConverTool

轻量级桌面文件转换工具（Avalonia + 插件架构）。**v1.0** 起默认附带两个基础插件，安装后即可转换常见视频与图片格式；也可在「插件管理」中安装更多 `.zip` 插件。

- **下载安装（Windows）**：[GitHub Releases](https://github.com/kckc7887/ConverTool/releases) 提供 **安装程序 `setup.exe`** 以及 **免安装 zip（full / lite）**，无需自行编译。
- **详细说明（含使用方法与技术文档）** → [docs/README.md](docs/README.md)

## 内置基础插件（默认附带）

| 插件 | 作用 | 说明 |
|------|------|------|
| **FFmpeg 视频转码** (`ffmpeg.video.transcoder`) | 视频容器/编码转换 | 依赖系统 **PATH** 中的 `ffmpeg`。支持 MP4、MKV、MOV、WebM、AVI 等目标格式，可在界面中调节质量（CRF）等选项。 |
| **ImageMagick 图片转换** (`imagemagick.image.transcoder`) | 任意常见图片互转 | 依赖 **ImageMagick** 的 `magick` 命令。若未安装，插件会尝试自动下载便携包（含 7z 解压逻辑，见插件实现）。支持 PNG、JPEG、WebP、TIFF、ICO 等。 |

安装包或发布目录中的 `plugins/` 下已包含上述插件；卸载或替换插件后，仍可通过「添加插件」从 `plugins-src` 构建的 zip 重新安装。

## 仓库结构提示

- **应用本体**：`Host/`（发布后用户可见文件名为 `ConverTool.exe`）
- **插件契约（NuGet）**：独立仓库 **`PluginAbstractions`**（包名 `ConverTool.PluginAbstractions`，与 Host 主版本对齐）
- **插件源码与构建**：`plugins-src/`、`plugins-src/build-and-sync.ps1`
- **Windows 安装包**：`installer/`（Inno Setup）

## 许可证

见仓库根目录 [LICENSE](LICENSE)（MIT）。
