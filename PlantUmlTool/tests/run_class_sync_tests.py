"""Windows: pure parser/plan checks for the class diagram sync core.

python PlantUmlTool/tests/run_class_sync_tests.py

The pure core (src/60-class-sync.cs) is compiled with the .NET Framework C# compiler together
with tests/ClassPumlTests.cs and tests/ClassSyncTests.cs and run against tests/samples.
The SDK compile of the whole script is tests/compile_sdk.py. Neither executes Next Design.
"""
import os
from pathlib import Path
import subprocess
import tempfile

root = Path(__file__).resolve().parents[1]
pure = (root / 'src/60-class-sync.cs').read_text(encoding='utf-8-sig')
assert 'NextDesign.' not in pure and 'IModel' not in pure, 'pure core must not touch the SDK'

test_workspace = root.parent / 'work'
test_workspace.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='class-sync-', dir=test_workspace) as tmp:
    work = Path(tmp)
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
    subprocess.run([str(exe), str(root / 'tests/samples')], check=True)
    print('PASS: parser round trips, plan counts, audit text')
