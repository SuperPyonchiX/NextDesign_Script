"""Exercise Windows setup with isolated files and fake CLIs; no downloads or user config writes."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


SOURCE = Path(__file__).resolve().parents[1]


@unittest.skipUnless(shutil.which('powershell'), 'Windows PowerShell required')
class SetupTests(unittest.TestCase):
    def test_setup_repeat_failure_and_whatif(self):
        with tempfile.TemporaryDirectory(prefix='ndmcp setup ') as temporary:
            root = Path(temporary)
            source = root / 'source with spaces'
            source.mkdir()
            shutil.copy2(SOURCE / 'Setup.ps1', source / 'Setup.ps1')
            # A built extension as dotnet publish lays it out (the DLL bytes are a stand-in).
            publish = source / 'publish'
            shutil.copytree(SOURCE / 'resources', publish / 'resources')
            shutil.copy2(SOURCE / 'manifest.json', publish / 'manifest.json')
            (publish / 'NdMcp.dll').write_bytes(b'MZ built NdMcp')
            (publish / 'NdMcp.deps.json').write_text('{}', encoding='utf-8')
            for name in ('manifest.json', 'bridge/pyproject.toml',
                         'bridge/uv.lock', 'bridge/nd_mcp_bridge/server.py'):
                dest = source / name
                dest.parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(SOURCE / name, dest)
            config = root / 'config'
            config.mkdir()
            (config / 'config.toml').write_text('# unrelated settings\n', encoding='utf-8')
            (config / '.claude.json').write_text(json.dumps({
                'mcpServers': {'nextdesign': {'command': 'old'}, 'other': {'command': 'keep'}}
            }), encoding='utf-8')
            fake = r'''
$entry = @{ cli = $MyInvocation.MyCommand.Name; args = @($args) }
Add-Content -LiteralPath (Join-Path $env:SETUP_TEST_ROOT 'calls.jsonl') -Value ($entry | ConvertTo-Json -Compress) -Encoding UTF8
$global:LASTEXITCODE = 0
if ($env:SETUP_TEST_FAIL -eq '1' -and $MyInvocation.MyCommand.Name -eq 'uv.ps1') { $global:LASTEXITCODE = 9 }
'''
            for name in ('uv', 'codex', 'claude'):
                (root / f'{name}.ps1').write_text(fake, encoding='utf-8-sig')
            wrapper = root / 'run.ps1'
            wrapper.write_text(r'''
param([string]$Root, [switch]$Fail, [switch]$Preview, [switch]$Missing)
$env:SETUP_TEST_ROOT = $Root
$env:SETUP_TEST_FAIL = if ($Fail) { '1' } else { '0' }
$env:CODEX_HOME = Join-Path $Root 'config'
$env:CLAUDE_CONFIG_DIR = Join-Path $Root 'config'
function Get-Command {
    param($Name, $CommandType, $ErrorAction)
    if ($Missing -and $Name -eq 'codex') { return $null }
    Microsoft.PowerShell.Core\Get-Command (Join-Path $env:SETUP_TEST_ROOT "$Name.ps1")
}
& (Join-Path $Root 'source with spaces\Setup.ps1') -Client Both -ExtensionDirectory (Join-Path $Root 'extension') -WhatIf:$Preview
if ($LASTEXITCODE) { exit $LASTEXITCODE }
''', encoding='utf-8-sig')

            def run(*options):
                return subprocess.run(['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass',
                                       '-File', str(wrapper), '-Root', str(root), *options],
                                      capture_output=True, timeout=60)

            result = run('-Preview')
            self.assertEqual(result.returncode, 0, result.stderr)
            self.assertFalse((root / 'extension').exists())
            self.assertFalse((root / 'calls.jsonl').exists())

            result = run('-Missing')
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((root / 'extension').exists())
            self.assertFalse((root / 'calls.jsonl').exists())

            result = run('-Fail')
            self.assertNotEqual(result.returncode, 0)
            self.assertFalse((root / 'extension').exists())
            (root / 'calls.jsonl').unlink()

            # An earlier script install leaves main.cs; setup keeps it only as a backup.
            (root / 'extension').mkdir()
            (root / 'extension/main.cs').write_text('// script version', encoding='utf-8')
            for _ in range(2):
                result = run()
                self.assertEqual(result.returncode, 0, result.stderr)
                self.assertIn(b'SETUP COMPLETE', result.stdout)
            self.assertEqual((root / 'extension/NdMcp.dll').read_bytes(), (publish / 'NdMcp.dll').read_bytes())
            self.assertEqual((root / 'extension/manifest.json').read_bytes(), (SOURCE / 'manifest.json').read_bytes())
            for icon in (SOURCE / 'resources').glob('*.png'):
                self.assertEqual((root / 'extension/resources' / icon.name).read_bytes(), icon.read_bytes())
            self.assertFalse((root / 'extension/main.cs').exists())
            self.assertEqual(len(list((root / 'extension').glob('main.cs.ndmcp-backup-*'))), 1)
            self.assertEqual(len(list((root / 'extension').glob('NdMcp.dll.ndmcp-backup-*'))), 1)
            self.assertEqual(len(list(config.glob('config.toml.ndmcp-backup-*'))), 2)
            self.assertEqual(json.loads((config / '.claude.json').read_text())['mcpServers']['other']['command'], 'keep')
            calls = [json.loads(line.lstrip('\ufeff')) for line in (root / 'calls.jsonl').read_text(encoding='utf-8-sig').splitlines()]
            adds = [c for c in calls if c['cli'] == 'codex.ps1']
            self.assertEqual(len(adds), 2)
            self.assertEqual(adds[0]['args'], ['mcp', 'add', 'nextdesign', '--', str(root / 'uv.ps1'),
                                             '--directory', str(source / 'bridge'), 'run', '--frozen',
                                             '--no-dev', 'nd-mcp-bridge'])
            claude = [c['args'] for c in calls if c['cli'] == 'claude.ps1']
            self.assertEqual(claude[0], ['mcp', 'remove', '--scope', 'user', 'nextdesign'])
            self.assertEqual(claude[1][:8], ['mcp', 'add', '--transport', 'stdio', '--scope', 'user', 'nextdesign', '--'])


if __name__ == '__main__':
    unittest.main()
