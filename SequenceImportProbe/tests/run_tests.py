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
   PumlTests.Run(args[0],args[1]);
   int commits=0, cancels=0;
   var success = new SequenceCompletion();
   success.Commit(delegate { commits++; });
   success.Cancel(delegate { cancels++; });
   if (commits!=1 || cancels!=0) throw new Exception("completed transaction cancelled");
   var failed = new SequenceCompletion();
   try { failed.Commit(delegate { throw new Exception("fake commit failure"); }); } catch(Exception) { }
   failed.Cancel(delegate { cancels++; });
   failed.Cancel(delegate { cancels++; });
   if (cancels!=1) throw new Exception("rollback not exactly once");
   var broken = new SequenceCompletion();
   try { broken.Cancel(delegate { cancels++; throw new Exception("fake rollback failure"); }); } catch(Exception) { }
   broken.Cancel(delegate { cancels++; });
   if (cancels!=2) throw new Exception("failed rollback retried");
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
    pure_file.write_text('using System; using System.Collections.Generic; using System.Linq; using System.IO; using System.Text; using System.Text.RegularExpressions;\n' + pure + runner + (root/'tests/PumlTests.cs').read_text(encoding='utf-8-sig'), encoding='utf-8-sig')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    exe = work / 'Tests.exe'
    subprocess.run([str(compiler), '/nologo', '/warnaserror+', '/out:' + str(exe), str(pure_file)], check=True)
    subprocess.run([str(exe), str(work), str(root/'samples')], check=True)
    for path in sorted(work.glob('0*.json')):
        data = json.loads(path.read_text(encoding='utf-8-sig'))
        entities = {e['Id']: e for e in data['Entities']}
        editor, = data['Editors']
        relations = data['Relations']
        assert all(r['SourceId'] in entities and r['TargetId'] in entities for r in relations)
        embeds = [r for r in relations if r['RelationType'] == 'Embed']
        assert len(embeds) == len(entities) - 1
        assert {r['TargetId'] for r in embeds} == set(entities) - {data['TopElementId']}
        shapes = [editor['Frame']] + [s for k,v in editor.items() if isinstance(v,list) for s in v]
        assert all(s['ModelId'] in entities for s in shapes)
        assert len({s['Id'] for s in shapes}) == len(shapes)
        if path.name == '09-destroy.json':
            assert len(editor['Destructions']) == 2
            lifelines = {e['Name']:e['Id'] for e in entities.values() if e['EntityType']=='Lifeline'}
            for name in ('Worker','Idle'):
                destruction = next(r['TargetId'] for r in relations if r['MetamodelId']=='OwnedDestruction' and r['SourceId']==lifelines[name])
                point = next(v for v in editor['Destructions'] if v['ModelId']==destruction)
                owned = {r['TargetId'] for r in relations if r['MetamodelId']=='OwnedExecutionSpecification' and r['SourceId']==lifelines[name]}
                bars = [v for v in editor['ExecutionSpecifications'] if v['ModelId'] in owned]
                assert all(v['Length']>0 and v['Y']+v['Length']<=point['Y'] for v in bars)
                assert entities[destruction]['EntityType']=='Destruction'
            messages = {e['Name']:e['Id'] for e in entities.values() if e['EntityType']=='Message'}
            port = lambda label: next(r['SourceId'] for r in relations if r['MetamodelId']=='ReceiveMessage' and r['TargetId']==messages[label])
            assert port('shutdown()') != port('final stop()'), 'loop must retain the closed activation state'
        if path.name == '08-incoming.json':
            assert len(editor['Lifelines']) == 2, 'External source must not become a participant'
            messages = {e['Name']: e for e in entities.values() if e['EntityType']=='Message'}
            ports = lambda label, kind: next(r['SourceId'] for r in relations if r['MetamodelId']==kind and r['TargetId']==messages[label]['Id'])
            sender = ports('external signal','SendMessage')
            assert entities[sender]['EntityType'] == 'MessageEnd' and sender != editor['Frame']['ModelId']
            end = next(v for v in editor['MessageEnds'] if v['ModelId']==sender)
            assert not any(r['MetamodelId']=='OwnedExecutionSpecification' and r['TargetId']==sender for r in relations)
            receiver = ports('external signal','ReceiveMessage')
            assert entities[receiver]['EntityType'] == 'ExecutionSpecification'
            assert ports('process()','SendMessage') == ports('result','ReceiveMessage') == ports('finished','SendMessage') == receiver
            assert len(editor['ExecutionSpecifications']) == 2, 'activate must reuse incoming execution'
            assert messages['external signal']['Fields']['MessageSort'] == 'Async'
            shape = next(v for v in editor['Messages'] if v['ModelId']==messages['external signal']['Id'])
            assert shape['IsRightAtFrame'] is False and shape['SelfloopBendsX']==0
            assert shape['SourceY'] == shape['TargetY']
            bar = next(v for v in editor['ExecutionSpecifications'] if v['ModelId']==receiver)
            assert end['X'] == bar['X']-60 and end['Y'] == shape['SourceY']
            assert bar['Y'] <= shape['TargetY'] < bar['Y']+bar['Length']
        if path.name == '07-outgoing.json':
            assert len(editor['Lifelines'])==2, 'Diagram boundary must not become a participant'
            message = next(e for e in entities.values() if e['Name']=='leave diagram')
            shape = next(v for v in editor['Messages'] if v['ModelId']==message['Id'])
            assert shape['IsRightAtFrame'] is False and shape['SelfloopBendsX']==0
            assert shape['SourceY']==shape['TargetY']
            source_port = next(r['SourceId'] for r in relations if r['MetamodelId']=='SendMessage' and r['TargetId']==message['Id'])
            target = next(r['SourceId'] for r in relations if r['MetamodelId']=='ReceiveMessage' and r['TargetId']==message['Id'])
            assert entities[source_port]['EntityType']=='ExecutionSpecification'
            assert entities[target]['EntityType']=='MessageEnd' and target!=editor['Frame']['ModelId']
            end = next(v for v in editor['MessageEnds'] if v['ModelId']==target)
            source_shape = next(v for v in editor['ExecutionSpecifications'] if v['ModelId']==source_port)
            assert end['X'] == source_shape['X']-60 and end['Y'] == shape['TargetY']
            assert not any(r['MetamodelId']=='OwnedExecutionSpecification' and r['TargetId']==target for r in relations)
            assert message['Fields']['MessageSort']=='Sync'
            bar = next(v for v in editor['ExecutionSpecifications'] if v['ModelId']==source_port)
            assert bar['Y'] <= shape['SourceY'] < bar['Y']+bar['Length']
        if path.name == '06-activation-self-reply.json':
            messages = {e['Name']:e for e in entities.values() if e['EntityType']=='Message'}
            assert messages['result']['Fields']['MessageSort'] == 'Reply'
            ports = lambda label,kind: next(r['SourceId'] for r in relations if r['MetamodelId']==kind and r['TargetId']==messages[label]['Id'])
            parent = ports('run()','ReceiveMessage')
            caller = ports('run()','SendMessage')
            assert ports('prepare()','SendMessage') == ports('validate()','SendMessage') == ports('result','SendMessage') == parent
            assert ports('result','ReceiveMessage') == caller
            execution = {s['ModelId']:s for s in editor['ExecutionSpecifications']}
            assert len(execution)==4, 'activate after receive must reuse the receiving execution'
            for label in ('prepare()','validate()'):
                child = execution[ports(label,'ReceiveMessage')]
                outer = execution[parent]
                assert child['X'] > outer['X'] and child['Y'] >= outer['Y']
                assert child['Y']+child['Length'] <= outer['Y']+outer['Length']
                shape = next(v for v in editor['Messages'] if v['ModelId']==messages[label]['Id'])
                assert shape['TargetY'] > shape['SourceY'] and shape['SelfloopBendsX'] > child['X']
            for msg in editor['Messages']:
                label = entities[msg['ModelId']]['Name']
                for kind,coordinate in [('SendMessage','SourceY'),('ReceiveMessage','TargetY')]:
                    bar=execution[ports(label,kind)]
                    assert bar['Y'] <= msg[coordinate] < bar['Y']+bar['Length']
        if path.name == '05-all.json':
            by_type = lambda t: [e for e in entities.values() if e['EntityType'] == t]
            assert [m['Fields']['MessageSort'] for m in by_type('Message')] == ['Sync','Async','Async']
            assert {m['Fields']['Operator'] for m in by_type('CombinedFragment')} == {'ALT','LOOP'}
            assert {m['Fields']['Guard'] for m in by_type('InteractionOperand')} == {'ready','waiting','retry < 3'}
            assert len(editor['Fragments']) == 2 and len(editor['Operands']) == 3
            assert len(editor['Notes']) == len(editor['InteractionUses']) == 1
            assert by_type('InteractionNote')[0]['Fields']['Body'] == 'Import verification\nSecond line'
            assert by_type('InteractionUse')[0]['Name'] == 'Follow-up interaction'
            branches = [r for r in relations if r['MetamodelId'] == 'OperandTargetMessage']
            assert len(branches) == 3
            assert {entities[r['SourceId']]['Fields']['Guard'] for r in branches} == {'ready','retry < 3'}
            nested, = [r for r in relations if r['MetamodelId'] == 'NestedInteractionFragment']
            assert entities[nested['SourceId']]['Fields']['Guard'] == 'waiting'
            assert entities[nested['TargetId']]['Fields']['Operator'] == 'LOOP'
            frames = {s['ModelId']: s for s in editor['Fragments']}
            child = frames[nested['TargetId']]
            parent_id = next(r['SourceId'] for r in relations if r['RelationType']=='Embed' and r['TargetId']==nested['SourceId'])
            parent = frames[parent_id]
            guard = next(s for s in editor['Operands'] if s['ModelId']==nested['SourceId'])
            assert child['X'] > parent['X']
            assert child['X']+child['Width'] < parent['X']+parent['Width']
            assert child['Y'] >= parent['Y']+guard['Position']+40
            assert child['Y']+child['Height'] < parent['Y']+parent['Height']

            assert max(s['LaneLength'] for s in editor['Lifelines']) <= 650
            for msg in editor['Messages']:
                ports = [r['SourceId'] for r in relations if r['TargetId']==msg['ModelId'] and r['MetamodelId'] in ('SendMessage','ReceiveMessage')]
                for port in ports:
                    execution = next(s for s in editor['ExecutionSpecifications'] if s['ModelId']==port)
                    assert execution['Y'] <= msg['SourceY'] < execution['Y']+execution['Length']
            print('Mixed sample lane length:', editor['Lifelines'][0]['LaneLength'])
            message_y = [s['SourceY'] for s in editor['Messages']]
            assert message_y == sorted(set(message_y))
    print('PASS: PlantUML samples, async sorts, branches/nesting, ref/Note payloads and unsupported syntax rejection')
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
    assert 'transaction.Dispose(' not in source, 'explicit transaction completion followed by implicit termination'
    print('PASS: transaction completion paths; generated IDs, ownership, ports, labels, shapes, coordinates, JSON escaping and rejection')
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
