# 在 Windows 上构建单文件 Deskup.exe
# 需要：Windows + .NET 8 SDK（或 9，把各 csproj 的 TargetFramework 改为 net9.0 / net9.0-windows 即可）
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$out  = Join-Path $root "publish"

Write-Host "==> 还原依赖"
dotnet restore (Join-Path $root "Deskup.sln")

Write-Host "==> 发布 Deskup (win-x64, 自包含单文件)"
dotnet publish (Join-Path $root "src/Deskup.Wpf/Deskup.Wpf.csproj") `
    -c Release -r win-x64 --self-contained true -o $out

Write-Host ""
Write-Host "完成。可执行文件位于: $out\Deskup.exe"
Write-Host "直接双击运行即可（首次运行会自动在 %USERPROFILE%\Documents\Deskup 生成 config.json）。"
