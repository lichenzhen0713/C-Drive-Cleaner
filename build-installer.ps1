# C盘清理大师 Pro v3.0 - 安装包构建脚本
# 用法:
#   .\build-installer.ps1                 编译主程序并生成安装包
#   .\build-installer.ps1 -SkipAppBuild   复用 dist 中已编译的主程序
#   .\build-installer.ps1 -Preview        额外编译界面预览工具（不发布）
#   .\build-installer.ps1 -Test           额外编译自动化测试工具（不发布）
param(
    [switch]$SkipAppBuild,
    [switch]$Preview,
    [switch]$Test,
    [string]$OutName = 'C盘清理大师-3.0-安装包.exe'
)

$ErrorActionPreference = 'Stop'

$root    = $PSScriptRoot
$dist    = Join-Path $root 'dist'
$ins     = Join-Path $root 'installer'
$release = Join-Path $root 'release'
$icon    = Join-Path $root 'app.ico'
$tools   = Join-Path $root 'tools'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw '找不到 x64 csc.exe（.NET Framework 4.x）' }

Write-Host '== 1/5 编译主程序 ==' -ForegroundColor Cyan
if (-not $SkipAppBuild) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1')
    if ($LASTEXITCODE -ne 0) { throw '主程序编译失败' }
}
$appExe = Join-Path $dist 'C盘清理大师.exe'
if (-not (Test-Path -LiteralPath $appExe)) { throw 'dist 中缺少主程序，请先执行 build.ps1' }

$tmp = Join-Path $env:TEMP ('cdc-installer-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp -Force | Out-Null

function Get-Sources {
    param([string[]]$Names)
    $list = @()
    foreach ($n in $Names) { $list += (Join-Path $ins $n) }
    return $list
}

try {
    Write-Host '== 2/5 打包程序文件 ==' -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $payload = Join-Path $tmp 'payload.zip'
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $dist, $payload, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    $payloadSize = (Get-Item -LiteralPath $payload).Length

    $common = @(
        '/nologo',
        '/codepage:65001',
        '/target:winexe',
        '/platform:x64',
        '/optimize+',
        ('/win32icon:"' + $icon + '"')
    )
    $refsBase = @('/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll')
    $refsZip  = @('/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')

    Write-Host '== 3/5 编译卸载程序 ==' -ForegroundColor Cyan
    $uninstExe = Join-Path $tmp 'Uninstall.exe'
    $uninstSrc = Get-Sources @('Ui.cs', 'Core.cs', 'UninstallEngine.cs', 'Uninstaller.cs', 'UninstallerInfo.cs')
    & $csc @common `
        ('/out:"' + $uninstExe + '"') `
        ('/win32manifest:"' + (Join-Path $ins 'manifest.xml') + '"') `
        ('/resource:"' + $icon + '",CDriveCleaner.app.ico') `
        @refsBase @uninstSrc
    if ($LASTEXITCODE -ne 0) { throw "卸载程序编译失败，退出代码 $LASTEXITCODE" }

    Write-Host '== 4/5 编译安装程序 ==' -ForegroundColor Cyan
    $setupExe = Join-Path $tmp $OutName
    $setupSrc = Get-Sources @('Ui.cs', 'Core.cs', 'InstallEngine.cs', 'Installer.cs', 'InstallerInfo.cs')
    & $csc @common `
        ('/out:"' + $setupExe + '"') `
        ('/win32manifest:"' + (Join-Path $ins 'manifest.xml') + '"') `
        ('/resource:"' + $payload + '",CDriveCleaner.payload.zip') `
        ('/resource:"' + $uninstExe + '",CDriveCleaner.uninstaller.exe') `
        ('/resource:"' + $icon + '",CDriveCleaner.app.ico') `
        @refsBase @refsZip @setupSrc
    if ($LASTEXITCODE -ne 0) { throw "安装程序编译失败，退出代码 $LASTEXITCODE" }

    Write-Host '== 5/5 输出发布文件 ==' -ForegroundColor Cyan
    New-Item -ItemType Directory -Path $release -Force | Out-Null
    $finalSetup = Join-Path $release $OutName
    Copy-Item -LiteralPath $setupExe -Destination $finalSetup -Force

    # 便携版（免安装 zip）
    $portableDir = Join-Path $release 'C盘清理大师 v3.0'
    if (Test-Path -LiteralPath $portableDir) { Remove-Item -LiteralPath $portableDir -Recurse -Force }
    New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
    Copy-Item -Path (Join-Path $dist '*') -Destination $portableDir -Force
    $portableZip = Join-Path $release 'C盘清理大师-v3.0-win64.zip'
    if (Test-Path -LiteralPath $portableZip) { Remove-Item -LiteralPath $portableZip -Force }
    Push-Location $release
    try { tar.exe -a -c -f $portableZip ('C盘清理大师 v3.0'); if ($LASTEXITCODE -ne 0) { throw '便携版打包失败' } }
    finally { Pop-Location }

    New-Item -ItemType Directory -Path $tools -Force | Out-Null

    if ($Preview) {
        Write-Host '== 编译界面预览工具（不发布）==' -ForegroundColor Cyan
        $previewExe = Join-Path $tmp 'preview.exe'
        $previewSrc = Get-Sources @('Ui.cs', 'Core.cs', 'InstallEngine.cs', 'UninstallEngine.cs', 'Installer.cs', 'Uninstaller.cs', 'Preview.cs')
        & $csc @common `
            ('/out:"' + $previewExe + '"') `
            ('/win32manifest:"' + (Join-Path $ins 'manifest-preview.xml') + '"') `
            ('/resource:"' + $icon + '",CDriveCleaner.app.ico') `
            @refsBase @refsZip `
            '/main:CDriveCleaner.Setup.PreviewMain' `
            @previewSrc
        if ($LASTEXITCODE -ne 0) { throw "预览工具编译失败，退出代码 $LASTEXITCODE" }
        $previewOut = Join-Path $tools 'preview-installer.exe'
        Copy-Item -LiteralPath $previewExe -Destination $previewOut -Force
        Write-Host ('预览工具: ' + $previewOut) -ForegroundColor Green
    }

    if ($Test) {
        Write-Host '== 编译自动化测试工具（不发布）==' -ForegroundColor Cyan
        $testExe = Join-Path $tmp 'test-install.exe'
        $testSrc = Get-Sources @('Ui.cs', 'Core.cs', 'InstallEngine.cs', 'UninstallEngine.cs', 'TestInstall.cs')
        & $csc '/nologo' '/codepage:65001' '/target:exe' '/platform:x64' '/optimize+' `
            ('/win32icon:"' + $icon + '"') `
            ('/out:"' + $testExe + '"') `
            ('/win32manifest:"' + (Join-Path $ins 'manifest-preview.xml') + '"') `
            ('/resource:"' + $payload + '",CDriveCleaner.payload.zip') `
            ('/resource:"' + $uninstExe + '",CDriveCleaner.uninstaller.exe') `
            ('/resource:"' + $icon + '",CDriveCleaner.app.ico') `
            @refsBase @refsZip `
            '/main:CDriveCleaner.Setup.TestMain' `
            @testSrc
        if ($LASTEXITCODE -ne 0) { throw "测试工具编译失败，退出代码 $LASTEXITCODE" }
        $testOut = Join-Path $tools 'test-install.exe'
        Copy-Item -LiteralPath $testExe -Destination $testOut -Force
        Write-Host ('测试工具: ' + $testOut) -ForegroundColor Green
    }

    Write-Host ''
    Write-Host '构建完成' -ForegroundColor Green
    Write-Host ("  安装包: " + $finalSetup) -ForegroundColor Green
    Write-Host ("  便携包: " + $portableZip)
    Write-Host ("  程序负载: " + [math]::Round($payloadSize / 1KB, 1) + " KB")
    Write-Host ''
    Get-ChildItem -LiteralPath $release -File | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize
    foreach ($f in @($finalSetup, $portableZip)) {
        if (Test-Path -LiteralPath $f) {
            $h = (Get-FileHash -LiteralPath $f -Algorithm SHA256).Hash
            Write-Host ("SHA256 " + (Split-Path $f -Leaf) + " = " + $h) -ForegroundColor DarkGray
        }
    }
}
finally {
    if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue }
}
