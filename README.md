<p align="center">
  <img src="src/Deskup.Wpf/Deskup.ico" width="80" height="80" alt="Deskup" />
</p>

<h1 align="center">Deskup</h1>
<p align="center"><strong>轻量 · 原生 · 无打扰</strong></p>
<p align="center">一款 Windows 原生桌面整理工具。拖拽分区归档文件，侧边栏一站式管理标签、待办和便签。</p>

<p align="center">
  <img src="https://img.shields.io/badge/.NET-8.0-blueviolet?logo=dotnet" />
  <img src="https://img.shields.io/badge/WPF-Native-orange?logo=windows" />
  <img src="https://img.shields.io/badge/license-MIT-green" />
  <img src="https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey?logo=windows" />
</p>

---

## 为什么用 Deskup？

桌面文件越堆越多，但又不习惯桌面上什么都没有。Deskup 不像传统桌面管理软件那样接管整个桌面，而是提供一组**轻量窗口**浮在桌面上——你可以把文件拖进「分区」里分类收纳，在侧边栏里随手记便签、列待办。**不用改变习惯，只是让桌面更有条理。**

- 🧩 **分区**：桌面上的透明容器，拖入文件自动归档，图标跟资源管理器一模一样
- 📋 **侧边栏**：标签 / 待办 / 便签 / 快捷入口，贴边不占地
- 🚀 **启动栏**：顶部悬停呼出，快捷启动常用程序
- 🪶 **无打扰**：始终在窗口最底层，Win+D 不消失，开机自启驻留托盘

---

## 快速开始

**环境要求**：Windows 10+ · .NET 8 SDK

```powershell
git clone https://github.com/yourname/deskup.git
cd deskup
.\build.ps1          # 产物 → publish\Deskup.exe
```

或者用 Visual Studio 打开 `Deskup.sln`，F5 运行。

首次启动后配置文件自动生成在 `%USERPROFILE%\Documents\Deskup\config.json`。

---

## 功能

### 🧩 分区窗口

| 功能 | 说明 |
|------|------|
| 拖入文件 | 从桌面/资源管理器拖入，默认复制（可在设置中改为移动） |
| 图标展示 | 与 Windows 资源管理器完全一致的图标质量（`IShellItemImageFactory`） |
| 右键菜单 | 打开 / 打开文件位置 / 删除 |
| 标题操作 | 双击改名、右键配色/透明度/锁定/删除 |
| 拖拽缩放 | 锁定模式下固定，解锁后可拖动右下角调整大小 |
| 独立透明度 | 标题栏和内容区分别可调 |

### 📋 侧边栏

| 区块 | 说明 |
|------|------|
| 功能设置 | 2×2 按钮：添加分区 / 设置 / 外观 / 退出 |
| ⚡ 快捷入口 | 4×2 网格，共 8 个可自定义快捷方式（右键编辑名称/路径/图标） |
| 🏷 自定义标签 | 彩色标签，右键编辑文字/颜色/字号，原生 Emoji 支持 |
| 📋 待办事项 | CheckBox + 编辑框，右键编辑背景色/文字颜色/字号 |
| 📝 临时便签 | 自由文本输入，自动保存；⚙ 20 种预设背景色 + 自定义色号 |
| 📐 外观调节 | 折叠/展开（38px 窄柱）、左右贴边切换、🔒 解锁后拖拽调宽、背景色与透明度（0%~100%）|

### 🚀 快捷启动栏

- 顶部悬浮，平时缩成细线不碍眼
- 鼠标悬停展开，📌 固定常驻
- 「＋」添加、右键删除

### ⚙ 设置

- 分区文件保存路径
- 桌面图标一键显隐
- 开机自启
- 图层模式（置顶 / 置于图标之下）

---

## 技术要点

```
Deskup.sln
├─ src/Deskup.Core/          纯逻辑库（无 WPF 依赖）
│   ├─ Config/               数据模型、JSON 序列化
│   ├─ Files/                文件复制/移动
│   └─ Geometry/             分区坐标/边界约束
├─ src/Deskup.Wpf/           WPF 界面 + Windows Shell 整合
│   ├─ Shell/                WindowEx (P/Invoke)、AppShell、TrayIcon
│   ├─ Views/                PartitionWindow、SidebarWindow、LaunchBar、SettingsWindow
│   └─ Helpers/              IconHelper (IShellItemImageFactory)、EmojiHelper
└─ tests/Deskup.Core.Tests/  独立测试运行器
```

### 桌面层级方案

| 技术 | 作用 |
|------|------|
| `Owner = SHELLDLL_DefView` | Win+D 后窗口不消失 |
| `SetWindowPos(HWND_BOTTOM)` + `WM_SETFOCUS` Hook | 始终 Z 序最底层，不挡任何窗口 |
| `SWP_NOACTIVATE` | 保持底层同时可正常交互 |
| `EnumChildWindows` + `ShowWindow` | 一键隐藏/显示系统桌面图标 |
| `IShellItemImageFactory` | 图标渲染与 Windows 资源管理器完全一致 |

---

## 路线图

- [x] 侧边栏：标签、待办、便签、快捷入口
- [x] 分区窗口：拖入文件、图标展示、右键菜单
- [x] 快捷启动栏：悬停展开、固定
- [x] 系统托盘：最小化驻留、右键菜单
- [x] 桌面图标显隐
- [x] 侧边栏背景透明度 0%~100%
- [ ] 分区内文件排序/筛选
- [ ] 主题切换（深色/浅色）
- [ ] 更多分区布局模板
- [ ] 多显示器支持优化

---

## License

MIT
