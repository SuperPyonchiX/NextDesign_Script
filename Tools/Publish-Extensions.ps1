#Requires -Version 5.1
<#
.SYNOPSIS
    エクステンション（DLL 方式）をビルドして検証し、必要なら Next Design の extensions フォルダへ配置する。

.DESCRIPTION
    既定では PlantUmlTool / AgentReview / NdMcp / SequenceImportProbe を対象にする。
    1. dotnet publish で work\publish\<名前> に出力する
    2. validate_manifest.py（nextdesign-extension スキル）が見つかれば、出力を検査する
    3. -Deploy を付けたときだけ、出力の中身を extensions\<名前> へコピーする
       - Next Design の起動中は配置しない（DLL は起動時にしか読み込まれず、実行中は差し替えられない）
       - スクリプト版の main.cs が残っていれば、バックアップしてから外す

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1 -Deploy
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1 -Name PlantUmlTool -Deploy -NextDesignDir "C:\Program Files\DENSO CREATE\Next Design"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string[]]$Name = @('PlantUmlTool', 'AgentReview', 'NdMcp', 'SequenceImportProbe'),
    [switch]$Deploy,
    # インストール先の DLL を直接参照してビルドする場合に渡す（省略時は NuGet の NextDesign 3.1.3）
    [string]$NextDesignDir,
    [string]$ExtensionsRoot = (Join-Path $env:LOCALAPPDATA 'DENSO CREATE\Next Design\extensions'),
    [string]$Validator = (Join-Path $env:USERPROFILE '.claude\skills\nextdesign-extension\scripts\validate_manifest.py'),
    [int]$NdVersion = 3
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repo = Split-Path $PSScriptRoot -Parent
$outputRoot = Join-Path $repo 'work\publish'

function Invoke-Checked([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program $($Arguments -join ' ') が失敗しました（終了コード $LASTEXITCODE）。" }
}

if ($Deploy -and (Get-Process -Name 'NextDesign' -ErrorAction SilentlyContinue)) {
    throw 'Next Design が起動しています。終了してから -Deploy を実行してください（DLL は実行中に差し替えられません）。'
}
$python = Get-Command python -ErrorAction SilentlyContinue
$canValidate = $python -and (Test-Path -LiteralPath $Validator -PathType Leaf)
if (!$canValidate) { Write-Warning "validate_manifest.py か python が見つからないので、出力の検査を省略します: $Validator" }

foreach ($extension in $Name) {
    $project = Join-Path $repo $extension
    if (!(Test-Path -LiteralPath (Join-Path $project "$extension.csproj") -PathType Leaf)) { throw "$extension.csproj がありません: $project" }
    $publish = Join-Path $outputRoot $extension
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }

    Write-Host "== $extension : ビルド"
    $arguments = @('publish', (Join-Path $project "$extension.csproj"), '-c', 'Release', '-o', $publish, '-nologo')
    if ($NextDesignDir) { $arguments += "-p:NextDesignDir=$NextDesignDir" }
    Invoke-Checked 'dotnet' $arguments

    if ($canValidate) {
        Write-Host "== $extension : 検査"
        Invoke-Checked $python.Source @($Validator, $project, '--nd-version', "$NdVersion", '--publish-dir', $publish)
    }

    if ($Deploy) {
        $target = Join-Path $ExtensionsRoot $extension
        if (!$PSCmdlet.ShouldProcess($target, "$extension を配置")) { continue }
        Write-Host "== $extension : 配置 -> $target"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $oldScript = Join-Path $target 'main.cs'
        if (Test-Path -LiteralPath $oldScript -PathType Leaf) {
            $backup = "$oldScript.script-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            Move-Item -LiteralPath $oldScript -Destination $backup
            Write-Host "   スクリプト版の main.cs を退避: $backup"
        }
        Copy-Item -Path (Join-Path $publish '*') -Destination $target -Recurse -Force
    }
}
if ($Deploy) { Write-Host '完了。Next Design を起動して動作を確認してください。' }
else { Write-Host "完了。出力: $outputRoot（配置するには -Deploy を付けて実行）" }
