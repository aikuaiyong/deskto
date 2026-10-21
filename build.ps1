# deskto 一键发布 + 打包脚本（在 Windows 上运行）
# 前置：安装 .NET 8 SDK —— https://dotnet.microsoft.com/download/dotnet/8.0
#
# 用法（推荐：在文件夹里 Shift+右键 →「在此处打开 PowerShell 窗口」，再输入）：
#   .\build.ps1
#
# 产出：
#   1) publish\            —— 发布后的程序目录（含 deskto.exe）
#   2) deskto-win-x64.zip  —— 可直接分发给别人的压缩包
#
# 注意：本脚本结尾会暂停，方便你看结果。若提示「无法加载，因为禁止运行脚本」，
#       先执行一次：Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
#       并对本文件右键 → 属性 → 勾选「解除锁定」(Unblock)。

$ErrorActionPreference = "Stop"

# 全局异常捕获：出错时打印并暂停，避免 PowerShell 窗口一闪而过
trap {
    Write-Host ""
    Write-Host "✗ 脚本执行出错，已终止：" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Yellow
    Write-Host "（请把上方红色/黄色错误信息截图发我）" -ForegroundColor Cyan
    Read-Host "按回车键关闭窗口"
    exit 1
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$publishDir = Join-Path $root "publish"
$appProj    = Join-Path $root "src/Deskto.Wpf/Deskto.Wpf.csproj"

# 版本号（与 csproj AssemblyVersion 保持一致即可）
$version = "2.1.3"

# 前置检查：必须在项目根目录运行，且需已安装 .NET 8 SDK
if (-not (Test-Path (Join-Path $root "deskto.sln"))) {
    Write-Error "未在当前项目根目录找到 deskto.sln。请先 cd 到包含 deskto.sln 的目录再运行本脚本。"
    Read-Host "按回车键关闭窗口"
    exit 1
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "✗ 未检测到 dotnet。请先安装 .NET 8 SDK：" -ForegroundColor Red
    Write-Host "  https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
    Write-Host "  安装后请重新打开终端再运行本脚本。" -ForegroundColor Yellow
    Read-Host "按回车键关闭窗口"
    exit 1
}
$dotnetVer = (dotnet --version 2>$null)
Write-Host "检测到 dotnet 版本：$dotnetVer"

Write-Host "==> 1/4 还原依赖"
dotnet restore (Join-Path $root "deskto.sln")

Write-Host "==> 2/4 发布 deskto (win-x64, 轻量便携版)"
# 轻量便携版：产出仅数 MB，解压后双击 deskto.exe 即可
# 目标电脑需安装 .NET 8 桌面运行时（https://dotnet.microsoft.com/download/dotnet/8.0）
dotnet publish $appProj `
    -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=false -p:PublishTrimmed=false -o $publishDir

if (-not (Test-Path (Join-Path $publishDir "deskto.exe"))) {
    Write-Error "发布失败：未找到 deskto.exe"
    exit 1
}

# 写一份极简使用说明到发布目录，方便分发给别人
@"
deskto $version —— Windows 桌面背景分区工具

【运行方式】
解压后双击 deskto.exe 即可。

【运行环境】
轻量便携版，需要电脑已安装 .NET 8 桌面运行时（装一次即可）：
  https://dotnet.microsoft.com/download/dotnet/8.0  →  ".NET Desktop Runtime 8.x"
若未安装，双击时会提示下载，按提示装好即可。
首次运行会在「%LOCALAPPDATA%\deskto\config.json」生成配置文件。

【功能】
- 桌面分区：在桌面上框出半透明背景区域，把系统桌面图标拖进区域内，视觉更整洁
- 锁定分区后变成纯背景：不挡鼠标、不干扰点击，桌面操作与平时完全一样
- 侧边栏：快捷入口、临时便签、待办、标签
- 系统托盘：常驻后台，右键退出 / 解锁分区
- 设置：开机自启、隐藏/显示桌面图标

【系统要求】Windows 10 / 11，64 位
"@ | Out-File -Encoding utf8 (Join-Path $publishDir "使用说明.txt")

Write-Host "==> 3/4 打包为分发压缩包"
$zipName = "deskto-win-x64.zip"
$zipPath = Join-Path $root $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

# 仅压缩发布目录内的文件（扁平），不含 publish 目录本身
if (Get-Command Compress-Archive -ErrorAction SilentlyContinue) {
    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -Force
} else {
    # 兜底：用 dotnet 内置方式不行时，提示手动压缩
    Write-Warning "未找到 Compress-Archive，请手动压缩 $publishDir 目录"
}

Write-Host "==> 4/4 完成"
Write-Host "可执行程序：$publishDir\deskto.exe"
Write-Host "分发压缩包：$zipPath"
if (Test-Path $zipPath) {
    $sizeMB = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "压缩包大小：${sizeMB} MB"
}
Write-Host ""
Write-Host "把 deskto-win-x64.zip 发给别人，解压后双击 deskto.exe 即用。"
Write-Host ""
Read-Host "构建完成，按回车键关闭窗口"
