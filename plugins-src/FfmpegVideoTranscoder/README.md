# FfmpegVideoTranscoder

一个最简单的 ConverTool 插件：用 `ffmpeg` 把常见视频格式转换到所选目标格式，支持设置质量（CRF）与硬件编码后端。

## 功能

- 输入：常见视频扩展名（mp4/mkv/mov/webm/avi/...）
- 输出：mp4 / mkv / mov / webm / avi（可扩展）
- 配置：
  - CRF（0-51）
  - 硬件加速：CPU / NVIDIA / AMD / Intel
- ffmpeg 获取策略（按优先级）：
  1. 系统 PATH 中已有 `ffmpeg` / `ffprobe`
  2. 插件目录内已有（例如 `tools/ffmpeg/bin/ffmpeg.exe`）
  3. 若是 Windows：自动下载并解压到插件目录（只下载一次）

## 打包

在本目录运行：

```powershell
.\build.ps1
```

产物在 `dist/`，把 zip 用 Host 的「添加插件」安装即可。

