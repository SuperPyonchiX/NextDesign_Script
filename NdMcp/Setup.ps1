#Requires -Version 5.1
[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('Codex', 'Claude', 'Both')][string]$Client = 'Codex',
    [string]$ExtensionDirectory = (Join-Path $env:LOCALAPPDATA 'DENSO CREATE\Next Design\extensions\NdMcp')
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
    foreach ($file in @('manifest.json', 'main.cs', 'bridge\pyproject.toml', 'bridge\uv.lock', 'bridge\nd_mcp_bridge\server.py')) {
        if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $file) -PathType Leaf)) {
            throw "Missing $file. Obtain the complete NdMcp folder before setup."
        }
    }
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'manifest.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($manifest.name -ne 'NdMcp') { throw 'Unexpected extension manifest.' }
    $installFiles = @('manifest.json', 'main.cs')
    foreach ($tab in $manifest.extensionPoints.ribbon.tabs) {
        foreach ($group in $tab.groups) {
            foreach ($control in $group.controls) {
                foreach ($key in @('imageSmall', 'imageLarge')) {
                    if ($control.PSObject.Properties[$key]) {
                        $icon = [string]$control.$key
                        if ($icon -notmatch '^resources/[a-zA-Z0-9_-]+\.png$') { throw "Unexpected icon path: $icon" }
                        if (!(Test-Path -LiteralPath (Join-Path $PSScriptRoot $icon) -PathType Leaf)) { throw "Missing icon: $icon" }
                        $installFiles += $icon
                    }
                }
            }
        }
    }
    $installFiles = @($installFiles | Select-Object -Unique)
    $ExtensionDirectory = [IO.Path]::GetFullPath($ExtensionDirectory)
    if ($ExtensionDirectory.TrimEnd('\') -eq $PSScriptRoot.TrimEnd('\')) {
        throw 'Extension destination must differ from the source folder.'
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
    New-Item -ItemType Directory -Path $ExtensionDirectory -Force | Out-Null
    foreach ($file in $installFiles) {
        $target = Join-Path $ExtensionDirectory $file
        New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force | Out-Null
        Backup-File $target
        Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $target -Force
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
