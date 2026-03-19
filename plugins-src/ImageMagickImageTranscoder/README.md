# ImageMagick Image Transcoder Plugin

该插件使用系统 `magick` 命令行把输入图片转换为目标格式。

## 自动安装（可选行为）
- 若运行环境里能直接找到 `magick.exe`（PATH 可用），则直接使用。
- 否则插件会下载 ImageMagick 的 Windows portable 版本到用户缓存目录：
  - `%LocalAppData%\\ConverTool\\imagemagick\\<version>/`
- 下载完成后需要一个 `7z.exe` 解压器（从 PATH 查找）。
  - 如果系统没有 `7z.exe`，插件会在日志里提示你安装 7-Zip 或把 `magick.exe` 加入 PATH。

## 运行依赖
- 运行机器需要：
  - 能在 PATH 中找到 `magick.exe`（推荐）
  - 或者让插件具备下载/解压能力（需要 `7z.exe`）

## 使用方式
- 在 Host 中“添加插件”安装该插件 `zip`
- 选择输入文件后在配置区选择目标格式（由插件 manifest 的 `supportedTargetFormats` 决定）
- Host 会把输入写入 `TempJobDir`，插件输出文件放在同一目录下的 `output.<ext>`（由 Host 完成最终命名/移动）

