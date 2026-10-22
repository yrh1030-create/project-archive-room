# 编译“项目档案室”及安装程序。只需 Windows 自带的 .NET Framework 4.x，无需安装其他工具。
param([switch]$SkipSetup)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$wm = 'C:\Windows\System32\WinMetadata'
$csc = "$fw\csc.exe"
$out = Join-Path $here 'build\out'
New-Item -ItemType Directory -Force $out | Out-Null

# ---------- 图标（与程序内 Logo 同一份绘制代码 src\AppIcon.cs） ----------
$ico = Join-Path $here 'build\app.ico'
& $csc /nologo /codepage:65001 /target:exe "/out:$(Join-Path $here 'build\icongen.exe')" "/r:$fw\System.Drawing.dll" (Join-Path $here 'src\AppIcon.cs') (Join-Path $here 'build\IconGen.cs')
if ($LASTEXITCODE -ne 0) { throw '图标生成器编译失败' }
& (Join-Path $here 'build\icongen.exe') $ico
if ($LASTEXITCODE -ne 0) { throw '图标生成失败' }

# ---------- 主程序 ----------
$refs = @("$fw\System.Web.Extensions.dll", "$fw\Microsoft.VisualBasic.dll", "$fw\System.Security.dll", "$fw\System.Data.dll", "$fw\System.IO.Compression.dll", "$fw\System.IO.Compression.FileSystem.dll",
    "$fw\System.Windows.Forms.dll", "$fw\System.Drawing.dll", "$fw\System.Runtime.dll", "$fw\System.Runtime.WindowsRuntime.dll",
    "$wm\Windows.Foundation.winmd", "$wm\Windows.Data.winmd", "$wm\Windows.Storage.winmd", "$wm\Windows.Graphics.winmd",
    "$wm\Windows.Media.winmd", "$wm\Windows.Globalization.winmd") | ForEach-Object { "/r:$_" }
$src = Get-ChildItem (Join-Path $here 'src\*.cs') | ForEach-Object { $_.FullName }
$exe = Join-Path $out '项目档案室.exe'
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ "/win32icon:$ico" "/win32manifest:$(Join-Path $here 'build\app.manifest')" "/out:$exe" "/resource:$(Join-Path $here 'build\合同预览模板.html'),preview.html" $refs $src
if ($LASTEXITCODE -ne 0) { throw '主程序编译失败' }
Write-Host "主程序：$exe"

if ($SkipSetup) { return }
# ---------- 安装程序（内嵌主程序） ----------
$setup = Join-Path $here '安装包\项目档案室安装程序.exe'
New-Item -ItemType Directory -Force (Split-Path $setup) | Out-Null
& $csc /nologo /codepage:65001 /target:winexe /platform:x64 /optimize+ "/win32icon:$ico" "/win32manifest:$(Join-Path $here 'build\app.manifest')" "/out:$setup" "/resource:$exe,app.exe" "/resource:$ico,app.ico" "/r:$fw\System.Windows.Forms.dll" "/r:$fw\System.Drawing.dll" (Join-Path $here 'setup\Setup.cs') (Join-Path $here 'src\AppIcon.cs')
if ($LASTEXITCODE -ne 0) { throw '安装程序编译失败' }
Write-Host "安装程序：$setup"
