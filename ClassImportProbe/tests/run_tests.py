"""Windows: pure parser/plan checks + optional exact V3.1/.NET6 API compile.

python ClassImportProbe/tests/run_tests.py --sdk-root work/sequence-api-research
SDK files are local development dependencies, never shipped with the extension.
This does not execute Next Design or prove that the SDK reads a real diagram the same way.
"""
import argparse
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--sdk-root', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
subprocess.run([os.sys.executable, str(root / 'sync/bundle.py'), '--check'], check=True)
source = (root / 'main.cs').read_text(encoding='utf-8-sig')

# The three version strings have no single source of truth; keep them equal by test.
manifest = json.loads((root / 'manifest.json').read_text(encoding='utf-8-sig'))
title = (root / 'README.md').read_text(encoding='utf-8-sig').splitlines()[0]
code_version = re.search(r'public const string Version = "([^"]+)";', source).group(1)
assert manifest['version'] == code_version == title.rsplit(' ', 1)[-1], (manifest['version'], code_version, title)
for command in manifest['extensionPoints']['commands']:
    assert ('public void ' + command['execFunc'] + '(') in source, command['execFunc']

test_workspace = root.parent / 'work'
test_workspace.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='class-sync-', dir=test_workspace) as tmp:
    work = Path(tmp)
    pure = source[source.index('// BEGIN GENERATED ClassSync.cs'):]
    assert 'NextDesign.' not in pure and 'IModel' not in pure, 'pure core must not touch the SDK'
    runner = '''
public static class ClassTestMain {
 public static int Main(string[] args) { try {
   ClassPumlTests.Run(args[0]);
   ClassSyncTests.Run(args[0]);
   return 0; } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
 }
}
'''
    pure_file = work / 'Pure.cs'
    pure_file.write_text('using System; using System.Collections.Generic; using System.Linq; using System.IO; using System.Text; using System.Text.RegularExpressions;\n'
                         + pure + runner
                         + (root / 'tests/ClassPumlTests.cs').read_text(encoding='utf-8-sig')
                         + (root / 'tests/ClassSyncTests.cs').read_text(encoding='utf-8-sig'), encoding='utf-8-sig')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    exe = work / 'Tests.exe'
    subprocess.run([str(compiler), '/nologo', '/warnaserror+', '/out:' + str(exe), str(pure_file)], check=True)
    subprocess.run([str(exe), str(root / 'samples')], check=True)
    print('PASS: parser round trips, plan counts, audit text')
    if args.sdk_root:
        sdk = args.sdk_root.resolve()
        refs = list((sdk / 'net6-ref/ref/net6.0').glob('*.dll'))
        assert refs, 'obtain Microsoft.NETCore.App.Ref 6.0.36 in sdk-root/net6-ref first'
        start = source.index('public void ')
        end = source.index('public static class ClassExperiment')
        wrapped = source[:start] + 'public class Handlers {\n' + source[start:end] + '}\n' + source[end:]
        production = work / 'Production.cs'
        production.write_text(wrapped, encoding='utf-8-sig')
        commands = ['/nologo', '/target:library', '/warnaserror+', '/out:' + str(work / 'Probe.dll'), str(production)]
        refs += [sdk / 'core/lib/netstandard2.0/NextDesign.Core.dll', sdk / 'desktop/lib/net6.0-windows7.0/NextDesign.Desktop.dll']
        commands += ['/r:' + str(p) for p in refs]
        rsp = work / 'compile.rsp'
        rsp.write_text('\n'.join('"' + v + '"' for v in commands))
        sdks = subprocess.check_output(['dotnet', '--list-sdks'], text=True).splitlines()
        version, location = sdks[-1].split(' [', 1)
        csc = Path(location.rstrip(']')) / version / 'Roslyn/bincore/csc.dll'
        subprocess.run(['dotnet', str(csc), '@' + str(rsp)], check=True)
        print('PASS: full script compiled against official V3.1.3 SDK and .NET 6 references')
