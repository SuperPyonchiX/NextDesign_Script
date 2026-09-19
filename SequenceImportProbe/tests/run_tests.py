"""Windows: pure payload checks + optional exact V3.1/.NET6 API compile.

python SequenceImportProbe/tests/run_tests.py --sdk-root work/sequence-api-research
SDK files are local development dependencies, never shipped with the extension.
This does not execute Next Design or prove successful import/rollback/rendering.
"""
import argparse
import json
import os
from pathlib import Path
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--sdk-root', type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
source = (root / 'main.cs').read_text(encoding='utf-8-sig')
with tempfile.TemporaryDirectory(prefix='sequence-payload-') as tmp:
    work = Path(tmp)
    pure = source[source.index('public class SequencePayload'):]
    runner = '''
public static class PayloadTest {
 public static void Main(string[] args) {
   string[] types = { "fake-root", "fake-frame", "fake-A", "fake-B", "fake-exec-A", "fake-exec-B", "fake-message" };
   for (int i=0;i<2;i++) File.WriteAllText(Path.Combine(args[0], "payload"+i+".json"), SequencePayload.Build(types, "fake-view", "13.0").Json);
   File.WriteAllText(Path.Combine(args[0], "escape.json"), SequencePayload.Q("日本語\\n\\t\\\"\\\\"));
   int rejected=0;
   try { SequencePayload.Build(null, "fake", "13.0"); } catch(ArgumentException) { rejected++; }
   try { SequencePayload.Build(types, "", "13.0"); } catch(ArgumentException) { rejected++; }
   try { SequencePayload.Build(types, "fake", "13.0\\\"}"); } catch(ArgumentException) { rejected++; }
   if (rejected!=3) throw new Exception("invalid input accepted");
 }
}
'''
    pure_file = work / 'Pure.cs'
    pure_file.write_text('using System; using System.Collections.Generic; using System.Linq; using System.IO; using System.Text; using System.Text.RegularExpressions;\n' + pure + runner, encoding='utf-8-sig')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    exe = work / 'Tests.exe'
    subprocess.run([str(compiler), '/nologo', '/warnaserror+', '/out:' + str(exe), str(pure_file)], check=True)
    subprocess.run([str(exe), str(work)], check=True)
    previous_ids = set()
    for path in sorted(work.glob('payload*.json')):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        entities = {e['Id']: e for e in data['Entities']}
        relations = data['Relations']
        editor, = data['Editors']
        ids = set(entities) | {r['Id'] for r in relations} | {editor['Id'], editor['Frame']['Id']}
        shapes = [editor['Frame']] + editor['Lifelines'] + editor['Messages'] + editor['ExecutionSpecifications']
        ids.update(s['Id'] for s in shapes)
        assert len(ids) == 24 and not ids & previous_ids
        previous_ids.update(ids)
        assert len(entities) == 7 and len(relations) == 10
        assert all(r['SourceId'] in entities and r['TargetId'] in entities for r in relations)
        assert all(s['ModelId'] in entities for s in shapes)
        assert editor['ModelId'] == data['TopElementId'] and editor['DefinitionId'] == 'fake-view'
        assert len(editor['Lifelines']) == 2 and len(editor['Messages']) == 1
        assert {entities[s['ModelId']]['Name'] for s in editor['Lifelines']} == {'A', 'B'}
        message = entities[editor['Messages'][0]['ModelId']]
        assert message['Fields'] == {'Name': 'probe()', 'MessageSort': 'Sync'}
        embeds = [r for r in relations if r['RelationType'] == 'Embed']
        assert len(embeds) == 6 and {r['TargetId'] for r in embeds} == set(entities) - {data['TopElementId']}
        assert {r['SourceId'] for r in embeds} == {data['TopElementId']}
        send, = [r for r in relations if r['MetamodelId'].endswith('.SendMessage')]
        receive, = [r for r in relations if r['MetamodelId'].endswith('.ReceiveMessage')]
        assert send['TargetId'] == receive['TargetId'] == message['Id']
        assert send['SourceId'] != receive['SourceId']
        for port, lifeline in zip([send, receive], editor['Lifelines']):
            owned, = [r for r in relations if r['MetamodelId'].endswith('.OwnedExecutionSpecification') and r['TargetId'] == port['SourceId']]
            assert owned['SourceId'] == lifeline['ModelId']
        for shape in editor['ExecutionSpecifications']:
            assert shape['Y'] <= editor['Messages'][0]['SourceY'] < shape['Y'] + shape['Length']
        assert 'Profiles' not in data and 'Project' not in data
    assert json.loads((work/'escape.json').read_text(encoding='utf-8-sig')) == '日本語\n\t"\\'
    print('PASS: generated IDs, ownership, ports, labels, shapes, coordinates, JSON escaping and rejection')
    if args.sdk_root:
        sdk = args.sdk_root.resolve()
        refs = list((sdk/'net6-ref/ref/net6.0').glob('*.dll'))
        assert refs, 'obtain Microsoft.NETCore.App.Ref 6.0.36 in sdk-root/net6-ref first'
        start = source.index('public void CreateMinimalSequence')
        end = source.index('public static class SequenceExperiment')
        wrapped = source[:start] + 'public class Handlers {\n' + source[start:end] + '}\n' + source[end:]
        production = work/'Production.cs'
        production.write_text(wrapped, encoding='utf-8-sig')
        commands = ['/nologo', '/target:library', '/warnaserror+', '/out:' + str(work/'Probe.dll'), str(production)]
        refs += [sdk/'core/lib/netstandard2.0/NextDesign.Core.dll', sdk/'desktop/lib/net6.0-windows7.0/NextDesign.Desktop.dll']
        commands += ['/r:' + str(p) for p in refs]
        rsp = work/'compile.rsp'
        rsp.write_text('\n'.join('"' + v + '"' for v in commands))
        sdks = subprocess.check_output(['dotnet', '--list-sdks'], text=True).splitlines()
        latest = sdks[-1]
        version, location = latest.split(' [', 1)
        csc = Path(location.rstrip(']')) / version / 'Roslyn/bincore/csc.dll'
        subprocess.run(['dotnet', str(csc), '@' + str(rsp)], check=True)
        print('PASS: full script compiled against official V3.1.3 SDK and .NET 6 references')
