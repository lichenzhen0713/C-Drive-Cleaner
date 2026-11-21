# C盘清理大师 Pro v3.0 - 构建脚本
# 默认生成需要管理员权限的正式版；-Dev 生成普通权限开发版。
param([switch]$Dev, [string]$OutName = 'C盘清理大师.exe')

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$core = Join-Path $root 'core'
$dist = Join-Path $root 'dist'
$lib = Join-Path $root 'lib\webview2'
$exe = Join-Path $dist $OutName

New-Item -ItemType Directory -Path $dist -Force | Out-Null

$csc64 = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$csc32 = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$csc = if (Test-Path -LiteralPath $csc64) { $csc64 } elseif (Test-Path -LiteralPath $csc32) { $csc32 } else { $null }
if (-not $csc) { throw '找不到 csc.exe（.NET Framework 4.x）' }

$sources = @(
    (Join-Path $core 'Models.cs'),
    (Join-Path $core 'Fs.cs'),
    (Join-Path $core 'Catalog.cs'),
    (Join-Path $core 'Engine.cs'),
    (Join-Path $root 'src\WebHost.cs'),
    (Join-Path $root 'src\Program.cs'),
    (Join-Path $root 'src\AssemblyInfo.cs')
)

$refs = @(
    '/r:System.dll',
    '/r:System.Core.dll',
    '/r:System.Drawing.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.ServiceProcess.dll',
    '/r:System.Web.Extensions.dll',
    ('/r:"' + (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll') + '"'),
    ('/r:"' + (Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll') + '"')
)

$cscArgs = @(
    '/nologo',
    '/codepage:65001',
    '/target:winexe',
    '/platform:x64',
    '/optimize+',
    ('/out:"' + $exe + '"'),
    ('/win32icon:"' + (Join-Path $root 'app.ico') + '"'),
    ('/win32manifest:"' + (Join-Path $root ($(if ($Dev) { 'manifest-dev.xml' } else { 'manifest.xml' }))) + '"'),
    ('/resource:"' + (Join-Path $root 'ui\index.html') + '",CDriveCleaner.ui.html')
) + $refs + $sources

if ($Dev) { $cscArgs += '/define:DEV' }

if (Test-Path -LiteralPath $exe) { Remove-Item -LiteralPath $exe -Force }

Write-Host '正在编译 C盘清理大师 Pro...' -ForegroundColor Cyan
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，退出代码 $LASTEXITCODE" }

Copy-Item -LiteralPath (Join-Path $lib 'Microsoft.Web.WebView2.Core.dll') -Destination $dist -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $lib 'Microsoft.Web.WebView2.WinForms.dll') -Destination $dist -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $lib 'WebView2Loader.dll') -Destination $dist -Force -ErrorAction SilentlyContinue
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $dist -Force -ErrorAction SilentlyContinue

Write-Host ('编译完成: ' + $exe) -ForegroundColor Green
Get-ChildItem -LiteralPath $dist -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
