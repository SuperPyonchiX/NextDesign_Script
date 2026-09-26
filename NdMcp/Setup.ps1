#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Codex', 'Claude', 'Both')][string]$Client = 'Codex',
    [string]$ExtensionDirectory = (Join-Path $env:LOCALAPPDATA 'DENSO CREATE\Next Design\extensions\NdMcp'),
    # Built extension (dotnet publish output). Built here from NdMcp.csproj when missing and the .NET SDK is available.
    [string]$PublishDirectory = (Join-Path $PSScriptRoot 'publish')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Checked {
    param([string]$Program, [string[]]$Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed (exit $LASTEXITCODE). Setup stopped; fix the error and run again." }
}

function Find-Program([string]$Name) {
    $command = Get-Command $Name -CommandType Application, ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($command) { return $command.Source }
    return $null
}

function Backup-File([string]$Path) {
    if (Test-Path -LiteralPath $Path -PathType Leaf) {
        $backup = "$Path.ndmcp-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss-fff')"
        Copy-Item -LiteralPath $Path -Destination $backup
        Write-Host "Backup: $backup"
    }
}

try {
    $bridge = Join-Path $PSScriptRoot 'bridge'
    foreach ($file in @('manifest.json', 'bridge\pyproject.toml', 'bridge\uv.lock', 'bridge\nd_mcp_bridge\server.py')) {
        if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $file) -PathType Leaf)) {
            throw "Missing $file. Obtain the complete NdMcp folder before setup."
        }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.name -ne 'NdMcp') { throw 'Unexpected extension manifest.' }
    $PublishDirectory = [IO.Path]::GetFullPath($PublishDirectory)
    $dotnet = $null
    if (!(Test-Path -LiteralPath (Join-Path $PublishDirectory $manifest.main) -PathType Leaf)) {
        # No built extension: build it here only when the project and the .NET SDK are both present.
        $dotnet = Find-Program 'dotnet'
        if (!$dotnet -or !(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'NdMcp.csproj') -PathType Leaf)) {
            throw "Missing built extension $($manifest.main) in $PublishDirectory. Build it with 'dotnet publish NdMcp -c Release -o NdMcp\publish' (needs the .NET SDK), or obtain the NdMcp folder with publish\ included."
        }
    }
    $ExtensionDirectory = [IO.Path]::GetFullPath($ExtensionDirectory)
    if ($ExtensionDirectory.TrimEnd('\') -eq $PSScriptRoot.TrimEnd('\') -or $ExtensionDirectory.TrimEnd('\') -eq $PublishDirectory.TrimEnd('\')) {
        throw 'Extension destination must differ from the source and publish folders.'
    }
    if (!$PSCmdlet.ShouldProcess($ExtensionDirectory, "Prepare uv/Python, install NdMcp $($manifest.version), register nextdesign in $Client")) { return }

    # Check client availability before changing the environment.
    $clients = @{}
    if ($Client -in @('Codex', 'Both')) { $clients['codex'] = Find-Program 'codex' }
    if ($Client -in @('Claude', 'Both')) { $clients['claude'] = Find-Program 'claude' }
    foreach ($name in $clients.Keys) {
        if (!$clients[$name]) { throw "$name is not installed or not on PATH. See SETUP.md, then open a new PowerShell window." }
    }

    $uv = Find-Program 'uv'
    if (!$uv) {
        $candidate = Join-Path $env:USERPROFILE '.local\bin\uv.exe'
        if (Test-Path -LiteralPath $candidate) { $uv = $candidate }
    }
    if (!$uv) {
        Write-Host '[1/4] Installing uv from astral.sh...'
        Invoke-Checked 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', 'irm https://astral.sh/uv/install.ps1 | iex')
        $uv = Find-Program 'uv'
        if (!$uv -and (Test-Path -LiteralPath $candidate)) { $uv = $candidate }
        if (!$uv) { throw 'uv installation did not produce a discoverable uv.exe. Reopen PowerShell and retry.' }
    }
    Write-Host '[2/4] Preparing Python 3.12 and locked dependencies...'
    Invoke-Checked $uv @('python', 'install', '3.12')
    Invoke-Checked $uv @('--directory', $bridge, 'sync', '--frozen', '--no-dev', '--python', '3.12')
    Invoke-Checked $uv @('--directory', $bridge, 'run', '--frozen', '--no-dev', 'python', '-c', 'import nd_mcp_bridge.server')

    Write-Host '[3/4] Installing Next Design extension...'
    if ($dotnet) {
        Invoke-Checked $dotnet @('publish', (Join-Path $PSScriptRoot 'NdMcp.csproj'), '-c', 'Release', '-o', $PublishDirectory)
    }
    # Install exactly the publish output: manifest, NdMcp.dll and its files, resources.
    $installFiles = @(Get-ChildItem -LiteralPath $PublishDirectory -Recurse -File | ForEach-Object { $_.FullName.Substring($PublishDirectory.TrimEnd('\').Length + 1) })
    foreach ($required in @('manifest.json', $manifest.main)) {
        if ($installFiles -notcontains $required) { throw "Publish output lacks $required." }
    }
    foreach ($file in $installFiles) {
        if ($file -match '^NextDesign\.(Core|Desktop)\.dll$') { throw "Publish output contains $file; it conflicts with Next Design's own DLL." }
    }
    New-Item -ItemType Directory -Path $ExtensionDirectory -Force | Out-Null
    # The script version's entry file would sit unused beside the DLL; keep it only as a backup.
    $oldScript = Join-Path $ExtensionDirectory 'main.cs'
    if (Test-Path -LiteralPath $oldScript -PathType Leaf) {
        Backup-File $oldScript
        Remove-Item -LiteralPath $oldScript
    }
    foreach ($file in $installFiles) {
        $target = Join-Path $ExtensionDirectory $file
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Backup-File $target
        Copy-Item -LiteralPath (Join-Path $PublishDirectory $file) -Destination $target -Force
    }

    Write-Host '[4/4] Registering MCP server nextdesign...'
    $launchArgs = @('--directory', $bridge, 'run', '--frozen', '--no-dev', 'nd-mcp-bridge')
    if ($clients.ContainsKey('codex')) {
        $configRoot = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
        Backup-File (Join-Path $configRoot 'config.toml')
        Invoke-Checked $clients['codex'] (@('mcp', 'add', 'nextdesign', '--', $uv) + $launchArgs)
    }
    if ($clients.ContainsKey('claude')) {
        $claudeRoot = if ($env:CLAUDE_CONFIG_DIR) { $env:CLAUDE_CONFIG_DIR } else { $env:USERPROFILE }
        $claudeConfig = Join-Path $claudeRoot '.claude.json'
        Backup-File $claudeConfig
        # Only replace a user-scoped entry; do not remove project/local entries.
        if (Test-Path -LiteralPath $claudeConfig) {
            $config = Get-Content -LiteralPath $claudeConfig -Raw -Encoding UTF8 | ConvertFrom-Json
            if ($config.PSObject.Properties['mcpServers'] -and $config.mcpServers -and $config.mcpServers.PSObject.Properties['nextdesign']) {
                Invoke-Checked $clients['claude'] @('mcp', 'remove', '--scope', 'user', 'nextdesign')
            }
        }
        Invoke-Checked $clients['claude'] (@('mcp', 'add', '--transport', 'stdio', '--scope', 'user', 'nextdesign', '--', $uv) + $launchArgs)
    }
    Write-Host "SETUP COMPLETE: NdMcp $($manifest.version) / $Client"
    Write-Host "Extension: $ExtensionDirectory"
    Write-Host "Bridge: $bridge (keep this folder in place)"
    Write-Host 'Next: restart Next Design and your AI client, open a project, click NdMcp > Start Server, then ask the AI to call nd_ping and nd_project.'
    Write-Host 'Setup completion does not mean that a live MCP connection has been tested. See SETUP.md.'
} catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
