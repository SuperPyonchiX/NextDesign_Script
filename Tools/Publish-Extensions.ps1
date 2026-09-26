#Requires -Version 5.1
<#
.SYNOPSIS
    エクステンション（DLL 方式）をビルドして検証し、必要なら Next Design の extensions フォルダへ配置する。

.DESCRIPTION
    既定では PlantUmlTool / AgentReview / NdMcp / SequenceImportProbe / ClassImportProbe を対象にする（後ろ2つは開発用）。
    1. dotnet publish で work\publish\<名前> に出力する
    2. validate_manifest.py（nextdesign-extension スキル）が見つかれば、出力を検査する
    3. -Deploy を付けたときだけ、extensions\<名前> を出力と同じ中身にする
       - Next Design の起動中は配置しない（DLL は起動時にしか読み込まれず、実行中は差し替えられない）
       - 配置前の中身を work\deploy-backup\<名前>\<日時> へ丸ごと写してから、出力に無いファイルを消し、出力をコピーする
         （旧版で消えたファイルやスクリプト版の main.cs を残さない。AgentReview のセッションは配置先の skills を
           ジャンクションで参照するので、バックアップは配置先の外に置き、フォルダ自体は消さない）
       - 元に戻すときは、Next Design を終了してバックアップの中身を配置先へ写し戻す

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1 -Deploy
    powershell -NoProfile -ExecutionPolicy Bypass -File Tools/Publish-Extensions.ps1 -Name PlantUmlTool -Deploy -NextDesignDir "C:\Program Files\DENSO CREATE\Next Design"
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string[]]$Name = @('PlantUmlTool', 'AgentReview', 'NdMcp', 'SequenceImportProbe', 'ClassImportProbe'),
    [switch]$Deploy,
    # インストール先の DLL を直接参照してビルドする場合に渡す（省略時は NuGet の NextDesign 3.1.3）
    [string]$NextDesignDir,
    [string]$ExtensionsRoot = (Join-Path $env:LOCALAPPDATA 'DENSO CREATE\Next Design\extensions'),
    [string]$Validator = (Join-Path $env:USERPROFILE '.claude\skills\nextdesign-extension\scripts\validate_manifest.py'),
    [int]$NdVersion = 3
)

# powershell -File では -Name A,B が "A,B" という 1 つの文字列で届くので、カンマで分ける
$Name = @($Name | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })

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
    $publish = [IO.Path]::GetFullPath((Join-Path $outputRoot $extension)).TrimEnd('\')
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
        $target = [IO.Path]::GetFullPath((Join-Path $ExtensionsRoot $extension)).TrimEnd('\')
        if (!$PSCmdlet.ShouldProcess($target, "$extension を配置")) { continue }
        Write-Host "== $extension : 配置 -> $target"
        New-Item -ItemType Directory -Path $target -Force | Out-Null
        $existing = @(Get-ChildItem -LiteralPath $target -Recurse -Force)
        if ($existing.Count -gt 0) {
            $backup = Join-Path $repo "work\deploy-backup\$extension\$(Get-Date -Format 'yyyyMMdd-HHmmss')"
            New-Item -ItemType Directory -Path $backup -Force | Out-Null
            Copy-Item -Path (Join-Path $target '*') -Destination $backup -Recurse -Force
            Write-Host "   配置前の中身を退避: $backup"
        }
        # 出力に無いファイルを消す。フォルダはセッションのジャンクションが指しうるので、出力に無く空になったものだけ消す
        $wanted = @{}
        foreach ($file in Get-ChildItem -LiteralPath $publish -Recurse -File) {
            $wanted[$file.FullName.Substring($publish.Length).TrimStart('\').ToLowerInvariant()] = $true
        }
        foreach ($file in Get-ChildItem -LiteralPath $target -Recurse -File -Force) {
            $relative = $file.FullName.Substring($target.Length).TrimStart('\')
            if (!$wanted.ContainsKey($relative.ToLowerInvariant())) {
                Remove-Item -LiteralPath $file.FullName -Force
                Write-Host "   削除: $relative"
            }
        }
        $publishDirs = @{}
        foreach ($dir in Get-ChildItem -LiteralPath $publish -Recurse -Directory) {
            $publishDirs[$dir.FullName.Substring($publish.Length).TrimStart('\').ToLowerInvariant()] = $true
        }
        $dirs = @(Get-ChildItem -LiteralPath $target -Recurse -Directory -Force | Sort-Object { $_.FullName.Length } -Descending)
        foreach ($dir in $dirs) {
            $relative = $dir.FullName.Substring($target.Length).TrimStart('\')
            if (!$publishDirs.ContainsKey($relative.ToLowerInvariant()) -and !(Get-ChildItem -LiteralPath $dir.FullName -Force)) {
                Remove-Item -LiteralPath $dir.FullName -Force
                Write-Host "   削除: $relative\"
            }
        }
        Copy-Item -Path (Join-Path $publish '*') -Destination $target -Recurse -Force
    }
}
if ($Deploy) { Write-Host '完了。Next Design を起動して動作を確認してください。' }
else { Write-Host "完了。出力: $outputRoot（配置するには -Deploy を付けて実行）" }
