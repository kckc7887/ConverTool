# ConverTool

**简单好用的文件格式转换工具**

视频、图片、文档，拖进去就能转。

---

## ✨ 特点

- **开箱即用** — 安装后无需配置，自动下载所需工具
- **拖拽转换** — 把文件拖进窗口，选择格式，一键转换
- **批量处理** — 支持多个文件同时转换
- **插件扩展** — 需要更多格式？安装插件即可
- **自定义命名** — 支持自定义输出文件名模板，包含时间、序号等变量
- **共享缓存** — 工具文件共享缓存，节省磁盘空间和下载时间

---

## 📥 下载安装

前往 [GitHub Releases](https://github.com/kckc7887/ConverTool/releases) 下载：

| 版本 | 说明 |
|------|------|
| **安装版** | 双击安装，自动关联文件 |
| **便携版** | 解压即用，无需安装 |

---

## 🎬 支持的格式

### 视频转换器
MP4、MKV、MOV、WebM、AVI、FLV、M4V、TS、MTS、M2TS、OGG、OGV...

> 可调节分辨率、帧率、画质

### 图片转换器
PNG、JPEG、WebP、TIFF、ICO、AVIF、BMP...

> 可控制输出文件大小

### 文档转换器
Markdown、Word（DOCX/DOC）、HTML、EPUB、PDF、TXT...

---

## 🚀 快速上手

1. **打开 ConverTool**
2. **拖入文件** — 或点击「浏览」选择
3. **选择输出格式** — 从下拉列表选择目标格式
4. **点击开始** — 等待转换完成
5. **查看结果** — 输出文件在指定目录

---

## ❓ 常见问题

**Q: 转换需要联网吗？**

A: 首次使用某个转换器时，会自动下载所需工具（约 50-200MB），之后可离线使用。

**Q: 转换速度慢？**

A: 视频转换耗时取决于文件大小和分辨率。大文件建议先用较低分辨率测试。

**Q: 支持哪些操作系统？**

A: 目前支持 Windows。

---

## 🛠️ 开发者

### 构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
# 克隆仓库
git clone https://github.com/kckc7887/ConverTool.git
cd ConverTool

# 克隆契约库（与 Host 平级）
git clone https://github.com/kckc7887/ConverTool-PluginAbstractions.git PluginAbstractions

# 构建
dotnet build .\Host\Host.csproj -c Release

# 可选：单元测试（含 config.json / persistValue 等）
dotnet test .\Host.Tests\Host.Tests.csproj -c Release
```

### 文档

| 文档 | 说明 |
|------|------|
| [docs/README.md](docs/README.md) | 项目概述 |
| [docs/plugin-dev.md](docs/plugin-dev.md) | 插件开发指南（含 **`visibleForTargetFormats`**、Host/插件 **`config.json`** 与 **`persistValue`**） |
| [docs/releases/PluginAbstractions-v1.1.0.md](docs/releases/PluginAbstractions-v1.1.0.md) | 契约包 **1.1.0** 变更说明 |
| [docs/technical/IMPLEMENTATION.md](docs/technical/IMPLEMENTATION.md) | 技术实现细节（含 **`SettingManager`**、配置 UI 规则引擎） |
| [docs/repositories.md](docs/repositories.md) | 仓库边界说明 |

### 目录结构

```
ConverTool/
├── Host/              # 应用本体
├── plugins-src/       # 内置插件源码
├── installer/         # 安装包脚本
└── docs/              # 文档
```

---

## 📄 许可证

[MIT License](LICENSE) — 自由使用、修改和分发。
