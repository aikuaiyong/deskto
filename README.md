<p align="center">
  <img src="src/Deskto.Wpf/Deskto.ico" width="80" height="80" alt="deskto" />
</p>

<h1 align="center">deskto</h1>
<p align="center"><strong>桌面背景分区 · 不打扰你的桌面</strong></p>
<p align="center">一款 Windows 桌面背景分区工具。在桌面上框出半透明区域，把图标拖进去让桌面更有条理——锁定后分区变成纯背景，桌面操作与没装它时完全一样。</p>

<p align="center">
  <img src="https://img.shields.io/badge/version-2.0-blue" />
  <img src="https://img.shields.io/badge/.NET-8.0-blueviolet?logo=dotnet" />
  <img src="https://img.shields.io/badge/WPF-Native-orange?logo=windows" />
  <img src="https://img.shields.io/badge/license-MIT-green" />
  <img src="https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey?logo=windows" />
</p>

---

## 为什么是 deskto？

桌面文件越堆越多，但你不想装一个"接管整个桌面"的重型管理软件。deskto 的思路很简单：**它不管理你的文件，只给你的桌面画几块背景色。**

你可以在桌面上创建若干个半透明的「分区框」，拖动它们、调整大小、改标题和配色，然后把系统桌面图标拖进框里——图标还是系统桌面图标，点它、拖它、删它和平时一模一样。**分区只是一个视觉背景，不是一个容器。**

- 🧩 **分区**：可拖动、可缩放、可改标题/配色的半透明背景框
- 🔒 **锁定即背景**：锁定后点击穿透，不挡鼠标，彻底隐身
- 📋 **侧边栏**：标签 / 待办 / 便签，贴边不占地，高度随内容自适应
- 🚀 **启动栏**：顶部悬停呼出，快捷启动常用程序
- 🪶 **无打扰**：始终在窗口最底层，Win+D 不消失，开机自启驻留托盘

---

## 快速开始

**环境要求**：Windows 10+ · [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```powershell
git clone <你的仓库地址>
cd deskto
.\build.ps1          # 产物 → publish\deskto.exe
```

或者用 Visual Studio 打开 `deskto.sln`，F5 运行。

首次启动后，配置文件生成在 `%LOCALAPPDATA%\deskto\config.json`。

---

## 打包分发

想把 deskto 发给没有开发环境的朋友？用自包含单文件发布即可——**对方电脑无需安装 .NET**。

**最简单（推荐）**：直接**双击仓库根目录的 `build.bat`**，窗口会自动停住不会闪退，跟着提示走即可。

或手动在终端运行：

```powershell
.\build.ps1
```

脚本会依次完成：还原依赖 → 发布 `win-x64` 自包含单文件 → 自动压缩为 **`deskto-win-x64.zip`**。

把 `deskto-win-x64.zip` 发给别人，解压后双击 `deskto.exe` 即用。

| 方案 | 产物 | 适合人群 |
|------|------|----------|
| `.\build.ps1`（默认） | `deskto-win-x64.zip`（单文件 exe） | 大多数分发场景，零依赖 |
| `setup.iss` + Inno Setup | `deskto-Setup.exe` 安装向导 | 想给小白用户提供「下一步」安装体验 |

> 安装向导需先装 [Inno Setup](https://jrsoftware.org/isdl.php)，用其打开 `setup.iss` 编译即可。

> 想缩减体积？把 `Deskto.Wpf.csproj` 的 `<SelfContained>` 改为 `false` 做依赖框架发布，但目标机器需先装 .NET 8 运行时。

---

## 功能

### 🧩 分区窗口（桌面背景框）

| 功能 | 说明 |
|------|------|
| 背景色块 | 分区是一块可调颜色/透明度的半透明背景，不再是文件容器 |
| 标题操作 | 双击改名、右键配色/字号/透明度/锁定/删除 |
| 拖拽移动 | 按住标题栏拖动分区到桌面任意位置 |
| 拖拽缩放 | 解锁后拖动右下角调整大小 |
| 锁定即背景 | 锁定后点击穿透，鼠标事件全部落到桌面，图标正常操作 |
| 解锁入口 | 锁定后无法点选分区，通过侧边栏「🔓 解锁所有分区」或托盘菜单解锁 |

### 📋 侧边栏

| 区块 | 说明 |
|------|------|
| 功能设置 | 添加分区 / 设置 / 侧边栏外观 / 解锁所有分区 / 退出 |
| 🏷 自定义标签 | 彩色标签，右键编辑文字/颜色/字号，原生 Emoji 支持 |
| 📋 待办事项 | CheckBox + 编辑框，右键编辑背景色/文字颜色/字号 |
| 📝 临时便签 | 自由文本输入，自动保存；⚙ 20 种预设背景色 + 自定义色号 |
| 📐 外观调节 | 折叠/展开（38px 窄柱）、左右贴边切换、🔒 解锁后拖拽调宽、背景色与透明度（0%~100%）|

### 🚀 快捷启动栏

- 顶部悬浮，平时缩成细线不碍眼
- 鼠标悬停展开，📌 固定常驻
- 「＋」添加、右键删除

### ⚙ 设置

- 开机自启
- 桌面图标一键显隐
- 图层模式（置顶 / 置于图标之下）

---

## 技术要点

```
deskto.sln
├─ src/Deskto.Core/          纯逻辑库（无 WPF 依赖）
│   ├─ Config/               数据模型、JSON 序列化
│   ├─ Files/                系统默认程序打开（供启动栏使用）
│   └─ Geometry/             分区坐标/边界约束
├─ src/Deskto.Wpf/           WPF 界面 + Windows Shell 整合
│   ├─ Shell/                WindowEx (P/Invoke)、AppShell、TrayIcon
│   ├─ Views/                PartitionWindow、SidebarWindow、LaunchBar、SettingsWindow
│   └─ Helpers/              IconHelper (IShellItemImageFactory)、EmojiHelper
└─ tests/Deskto.Core.Tests/  独立测试运行器
```

### 桌面层级与点击穿透

| 技术 | 作用 |
|------|------|
| `Owner = SHELLDLL_DefView` | Win+D 后窗口不消失 |
| `SetWindowPos(HWND_BOTTOM)` + Hook | 始终 Z 序最底层，不挡任何窗口 |
| `WS_EX_TRANSPARENT`（点击穿透） | 锁定后鼠标事件穿透到桌面，图标正常操作 |
| `SWP_NOACTIVATE` | 保持底层同时可正常交互 |
| `EnumChildWindows` + `ShowWindow` | 一键隐藏/显示系统桌面图标 |
| `IShellItemImageFactory` | 图标渲染与 Windows 资源管理器完全一致 |

---

## 更新日志

### v2.0（首个版本）

- 桌面背景分区：在桌面上框出半透明区域，系统桌面图标拖进框内，视觉更整洁
- 锁定即背景：锁定后开启点击穿透，不挡鼠标、不干扰桌面操作
- 侧边栏：功能设置 / 标签 / 待办 / 便签，高度随内容自适应
- 启动栏：顶部悬停呼出，快捷启动常用程序
- 系统托盘：常驻后台，一键显示/隐藏、解锁分区
- 设置：开机自启、隐藏桌面图标、图层模式

---

## License

MIT
