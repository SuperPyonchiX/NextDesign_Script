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

import lint_samples
print('PASS: samples PlantUML accepts: %d' % lint_samples.check(root / 'samples'))
subprocess.run([os.sys.executable, str(root/'sync/bundle.py'), '--check'], check=True)
source = (root / 'main.cs').read_text(encoding='utf-8-sig')
test_workspace = root.parent / 'work'
test_workspace.mkdir(exist_ok=True)
with tempfile.TemporaryDirectory(prefix='sequence-payload-', dir=test_workspace) as tmp:
    work = Path(tmp)
    pure = source[source.index('public class SequencePayload'):]
    runner = '''
public static class PayloadTest {
 public static int Main(string[] args) { try {
   PumlTests.Run(args[0],args[1]);
   MappingTests.Run(args[0]);
   EditorTests.Run();
   SyncTests.Run();
   StructurePreparationTests.Run();
   var trialBefore=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-trial-before.puml")));
   var trialAfter=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-trial-after.puml")));
   var trialPlan=SyncPlan.Build(trialBefore,trialAfter,()=>Guid.NewGuid().ToString());
   var trialGate=SequenceStructurePreflight.Check(trialBefore,trialPlan);
   if(!trialGate.Candidate || trialPlan.Changes.Count!=1 || trialGate.DeleteExecutions.Count!=1 || trialGate.ReconnectMessages.Count!=0)
       throw new Exception("trial sample is not exactly one execution deletion");

   var bareModel=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"roundtrip-probe.puml")));
   var bareInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"omitted-roundtrip.puml")));
   var barePlan=SyncPlan.Build(bareModel,bareInput,()=>Guid.NewGuid().ToString());
   if(barePlan.Changes.Count!=0 || barePlan.InheritRefusals.Count!=0)
       throw new Exception("omitted activations are not a no-op: "+barePlan.ToJson());
   if(SequenceStructurePreflight.Check(bareModel,barePlan).Targets!=0)
       throw new Exception("omitted activations produced structure targets");

   var bareBatchInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"omitted-batch.puml")));
   var bareBatchModel=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-batch-before.puml")));
   var bareBatchPlan=SyncPlan.Build(bareBatchModel,bareBatchInput,()=>Guid.NewGuid().ToString());
   if(bareBatchPlan.Changes.Any(c=>c.Kind=="execution" || c.Kind=="message" || c.Kind=="participant"))
       throw new Exception("omitted activations disturbed the batch sample: "+bareBatchPlan.ToJson());
   var bareBatchIds=new HashSet<string>(bareBatchModel.Elements.Select(e=>e.Id));
   if(bareBatchPlan.Expected.Elements.Any(e=>e.Kind=="execution" && !bareBatchIds.Contains(e.Id)))
       throw new Exception("omitted activations invented execution ids");
   if(SyncPlan.Build(bareBatchPlan.Expected,bareBatchInput,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("omitted activations are not idempotent");

   var bareFrameModel=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-fragment-before.puml")));
   var bareFrameInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"omitted-fragment.puml")));
   var bareFramePlan=SyncPlan.Build(bareFrameModel,bareFrameInput,()=>Guid.NewGuid().ToString());
   if(bareFramePlan.Changes.Count!=0 || bareFramePlan.InheritRefusals.Count!=0)
       throw new Exception("omitted activations inside a frame are not a no-op: "+bareFramePlan.ToJson());
   if(bareFramePlan.CarriedExecutions.Count!=bareFrameModel.Elements.Count(e=>e.Kind=="execution"))
       throw new Exception("a bar inside a frame was not carried: "+bareFramePlan.ToJson());

   // The diagram holds no frame at all, so nothing can be copied: every part of the new
   // frame is built from the resolved metaclasses.
   var frameAddModel=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-fragment-after.puml")));
   var frameAddInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-frameadd-after.puml")));
   var frameAddPlan=SyncPlan.Build(frameAddModel,frameAddInput,()=>Guid.NewGuid().ToString());
   var frameAddGate=SequenceStructurePreflight.Check(frameAddModel,frameAddPlan);
   if(!frameAddGate.Candidate || frameAddGate.AddFragments.Count!=1 || frameAddGate.AddOperands.Count!=1
       || frameAddGate.AddMessages.Count!=1 || frameAddGate.AddExecutions.Count!=2)
       throw new Exception("appended frame must be one frame, one operand, one message and two bars: "
           +frameAddPlan.ToJson()+frameAddGate.ToJson());
   // The same input against a diagram that already holds that frame changes nothing.
   var frameKeptModel=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-fragment-before.puml")));
   var frameKeptPlan=SyncPlan.Build(frameKeptModel,frameAddInput,()=>Guid.NewGuid().ToString());
   if(frameKeptPlan.Changes.Count!=0)
       throw new Exception("an existing frame was not recognised: "+frameKeptPlan.ToJson());
   // Putting messages that already exist inside a frame moves them; that is a different change.
   // Adding a frame to a diagram that already holds one: the shape collections are
   // already there, so nothing has to create them.
   var secondFrameModel=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-fragment-before.puml")));
   var secondFrameInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-frameadd-second.puml")));
   var secondFramePlan=SyncPlan.Build(secondFrameModel,secondFrameInput,()=>Guid.NewGuid().ToString());
   var secondFrameGate=SequenceStructurePreflight.Check(secondFrameModel,secondFramePlan);
   if(!secondFrameGate.Candidate || secondFrameGate.AddFragments.Count!=1 || secondFrameGate.AddOperands.Count!=1
       || secondFrameGate.AddMessages.Count!=1 || secondFrameGate.AddExecutions.Count!=2)
       throw new Exception("a second frame must be one frame, one operand, one message and two bars: "
           +secondFramePlan.ToJson()+secondFrameGate.ToJson());

   var frameWrapInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-framewrap-after.puml")));
   var frameWrapGate=SequenceStructurePreflight.Check(frameAddModel,
       SyncPlan.Build(frameAddModel,frameWrapInput,()=>Guid.NewGuid().ToString()));
   if(frameWrapGate.Candidate)
       throw new Exception("wrapping existing messages was accepted: "+frameWrapGate.ToJson());

   // A message inserted between two that already exist, onto bars already open there.
   var insertModel=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-insert-before.puml")));
   var insertInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-insert-after.puml")));
   var insertPlan=SyncPlan.Build(insertModel,insertInput,()=>Guid.NewGuid().ToString());
   var insertGate=SequenceStructurePreflight.Check(insertModel,insertPlan);
   if(!insertGate.Candidate || insertGate.AddMessages.Count!=1 || insertGate.Targets!=1)
       throw new Exception("an inserted message must be the only change: "+insertPlan.ToJson()+insertGate.ToJson());
   // The same insertion with a frame below it, and one into an operand already drawn.
   var frameBefore=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-fragment-before.puml")));
   foreach(var pair in new[]{new[]{"structure-fragment-before.puml","structure-insert-frame-after.puml"},
       new[]{"structure-fragment-before.puml","structure-insert-inframe-after.puml"},
       new[]{"structure-insert-frame-after.puml","structure-insert-inframe-next.puml"}})
   {
       string sample=pair[1];
       frameBefore=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],pair[0])));
       var framedInput=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],sample)));
       var framedPlan=SyncPlan.Build(frameBefore,framedInput,()=>Guid.NewGuid().ToString());
       var framedGate=SequenceStructurePreflight.Check(frameBefore,framedPlan);
       if(!framedGate.Candidate || framedGate.AddMessages.Count!=1 || framedGate.Targets!=1 || !framedGate.CanCommit())
           throw new Exception(sample+" must be a single insertion: "+framedPlan.ToJson()+framedGate.ToJson());
   }

   var reconnectBefore=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-reconnect-before.puml")));
   var reconnectAfter=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-reconnect-after.puml")));
   var reconnectPlan=SyncPlan.Build(reconnectBefore,reconnectAfter,()=>Guid.NewGuid().ToString());
   var reconnectGate=SequenceStructurePreflight.Check(reconnectBefore,reconnectPlan);
   if(!reconnectGate.Candidate || reconnectPlan.Changes.Count!=2 || reconnectGate.ReconnectMessages.Count!=1 || reconnectGate.DeleteExecutions.Count!=1)
       throw new Exception("reconnect sample must contain exactly one receiver update and one deletion: "+reconnectPlan.ToJson()+reconnectGate.ToJson());

   if(!trialGate.CanCommit() || !reconnectGate.CanCommit())
       throw new Exception("commit modes accepted the wrong scope");
   reconnectGate.Reasons.Add("unsupported change");
   if(reconnectGate.CanCommit())throw new Exception("commit accepted partially supported plan");
   var emptyGate=new SequenceStructurePreflight();
   if(emptyGate.CanCommit())throw new Exception("empty commit accepted");

   var batchBefore=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-batch-before.puml")));
   var batchAfter=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-batch-after.puml")));
   var batchPlan=SyncPlan.Build(batchBefore,batchAfter,()=>Guid.NewGuid().ToString());
   var batchGate=SequenceStructurePreflight.Check(batchBefore,batchPlan);
   if(!batchGate.CanCommit() || batchPlan.Changes.Count!=4 || batchGate.ReconnectMessages.Count!=2 || batchGate.DeleteExecutions.Count!=2)
       throw new Exception("batch sample must contain two reconnects and two deletions: "+batchPlan.ToJson()+batchGate.ToJson());
   if(SyncPlan.Build(batchPlan.Expected,batchAfter,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("batch semantic plan is not idempotent");

   // Same before diagram as the batch sample; only the first inner bar goes away,
   // so the deleted execution is not last in its owner collections.
   var nontailAfter=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-nontail-after.puml")));
   var nontailPlan=SyncPlan.Build(batchBefore,nontailAfter,()=>Guid.NewGuid().ToString());
   var nontailGate=SequenceStructurePreflight.Check(batchBefore,nontailPlan);
   if(!nontailGate.CanCommit() || nontailPlan.Changes.Count!=2 || nontailGate.ReconnectMessages.Count!=1 || nontailGate.DeleteExecutions.Count!=1)
       throw new Exception("non-tail sample must contain one reconnect and one deletion: "+nontailPlan.ToJson()+nontailGate.ToJson());
   // The batch sample deletes both inner bars; this one deletes only the earlier of
   // the two, so a later sibling of the same participant survives. Whether the product
   // then compacts that sibling's index is what the run has to show.
   if(batchGate.DeleteExecutions.Count!=2 || nontailGate.DeleteExecutions[0]!=batchGate.DeleteExecutions[0])
       throw new Exception("non-tail sample does not delete the earlier inner execution");
   if(SyncPlan.Build(nontailPlan.Expected,nontailAfter,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("non-tail semantic plan is not idempotent");

   // first() already receives on B's outer bar, so second() moves onto a collection
   // that is not empty. The target of the move is the same as in the batch sample.
   var occupiedBefore=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-occupied-before.puml")));
   var occupiedPlan=SyncPlan.Build(occupiedBefore,batchAfter,()=>Guid.NewGuid().ToString());
   var occupiedGate=SequenceStructurePreflight.Check(occupiedBefore,occupiedPlan);
   if(!occupiedGate.CanCommit() || occupiedPlan.Changes.Count!=2 || occupiedGate.ReconnectMessages.Count!=1 || occupiedGate.DeleteExecutions.Count!=1)
       throw new Exception("occupied sample must contain one reconnect and one deletion: "+occupiedPlan.ToJson()+occupiedGate.ToJson());
   var occupiedTarget=occupiedPlan.Expected.Elements.Single(e=>e.Id==occupiedGate.ReconnectMessages[0]).Links["receiveExecution"].Single();
   if(occupiedBefore.Elements.Count(e=>e.Kind=="message" && e.Links.ContainsKey("receiveExecution") && e.Links["receiveExecution"].Contains(occupiedTarget))==0)
       throw new Exception("occupied sample destination has no existing receiver");
   if(SyncPlan.Build(occupiedPlan.Expected,batchAfter,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("occupied semantic plan is not idempotent");

   // The occupied pair read backwards: the input asks for the inner bar the diagram
   // no longer has, while first() keeps the outer bar anchored.
   var addPlan=SyncPlan.Build(batchAfter,occupiedBefore,()=>Guid.NewGuid().ToString());
   var addGate=SequenceStructurePreflight.Check(batchAfter,addPlan);
   if(!addGate.Candidate || addPlan.Changes.Count!=2 || addGate.AddExecutions.Count!=1 || addGate.ReconnectMessages.Count!=1 || addGate.DeleteExecutions.Count!=0)
       throw new Exception("addition sample must contain one added execution and one reconnect: "+addPlan.ToJson()+addGate.ToJson());
   if(!addGate.CanCommit())
       throw new Exception("addition sample did not reach exactly the receiver-change commit mode");
   if(SyncPlan.Build(addPlan.Expected,occupiedBefore,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("addition semantic plan is not idempotent");

   // The batch diagram plus one declared lane, and the same pair read backwards.
   var laneAfter=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-participant-after.puml")));
   var lanePlan=SyncPlan.Build(batchAfter,laneAfter,()=>Guid.NewGuid().ToString());
   var laneGate=SequenceStructurePreflight.Check(batchAfter,lanePlan);
   if(!laneGate.Candidate || lanePlan.Changes.Count!=1 || laneGate.AddParticipants.Count!=1 || laneGate.Targets!=1)
       throw new Exception("participant sample must add exactly one lane: "+lanePlan.ToJson()+laneGate.ToJson());
   if(!laneGate.CanCommit())
       throw new Exception("participant sample did not reach exactly the receiver-change commit mode");
   if(SyncPlan.Build(lanePlan.Expected,laneAfter,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("participant semantic plan is not idempotent");

   var dropPlan=SyncPlan.Build(laneAfter,batchAfter,()=>Guid.NewGuid().ToString());
   var dropGate=SequenceStructurePreflight.Check(laneAfter,dropPlan);
   if(!dropGate.Candidate || dropPlan.Changes.Count!=1 || dropGate.DeleteParticipants.Count!=1 || dropGate.Targets!=1)
       throw new Exception("participant removal sample must delete exactly one lane: "+dropPlan.ToJson()+dropGate.ToJson());
   if(!dropGate.CanCommit())
       throw new Exception("participant removal did not reach exactly the receiver-change commit mode");

   // The batch diagram without its last reply.
   var messageAfter=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-message-after.puml")));
   var messagePlan=SyncPlan.Build(batchAfter,messageAfter,()=>Guid.NewGuid().ToString());
   var messageGate=SequenceStructurePreflight.Check(batchAfter,messagePlan);
   if(!messageGate.Candidate || messagePlan.Changes.Count!=1 || messageGate.DeleteMessages.Count!=1 || messageGate.Targets!=1)
       throw new Exception("message sample must delete exactly one message: "+messagePlan.ToJson()+messageGate.ToJson());
   if(!messageGate.CanCommit())
       throw new Exception("message removal did not reach exactly the receiver-change commit mode");
   if(SyncPlan.Build(messagePlan.Expected,messageAfter,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("message semantic plan is not idempotent");

   // The same pair read backwards puts the reply back, into the space the bars still cover.
   var addBackPlan=SyncPlan.Build(messageAfter,batchAfter,()=>Guid.NewGuid().ToString());
   var addBackGate=SequenceStructurePreflight.Check(messageAfter,addBackPlan);
   if(!addBackGate.Candidate || addBackPlan.Changes.Count!=1 || addBackGate.AddMessages.Count!=1 || addBackGate.Targets!=1)
       throw new Exception("message addition sample must add exactly one message: "+addBackPlan.ToJson()+addBackGate.ToJson());
   if(!addBackGate.CanCommit())
       throw new Exception("message addition did not reach exactly the receiver-change commit mode");
   if(SyncPlan.Build(addBackPlan.Expected,batchAfter,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("message addition semantic plan is not idempotent");

   // An alt block shaped like the fragment sample the product is known to import:
   // messages inside, no activation bars anywhere.
   var fragmentBefore=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-fragment-before.puml")));
   var fragmentAfter=SequenceDocument.Parse(File.ReadAllText(Path.Combine(args[1],"structure-fragment-after.puml")));
   var fragmentPlan=SyncPlan.Build(fragmentBefore,fragmentAfter,()=>Guid.NewGuid().ToString());
   var fragmentGate=SequenceStructurePreflight.Check(fragmentBefore,fragmentPlan);
   if(!fragmentGate.Candidate || fragmentGate.DeleteFragments.Count!=1 || fragmentGate.DeleteOperands.Count!=1
       || fragmentGate.DeleteMessages.Count!=1 || fragmentGate.DeleteExecutions.Count!=2 || fragmentGate.Targets!=5)
       throw new Exception("fragment sample must remove one fragment with its operand, message and bars: "
           +fragmentPlan.ToJson()+fragmentGate.ToJson());
   if(!fragmentGate.CanCommit())
       throw new Exception("fragment removal did not reach exactly the receiver-change commit mode");
   if(SyncPlan.Build(fragmentPlan.Expected,fragmentAfter,()=>Guid.NewGuid().ToString()).Changes.Count!=0)
       throw new Exception("fragment semantic plan is not idempotent");

   // Dropping the frame but keeping something inside leaves it nowhere to live.
   var frame=fragmentBefore.Elements.Single(e=>e.Kind=="fragment");
   var operand=fragmentBefore.Elements.Single(e=>e.Parent==frame.Id);
   var occupied=fragmentBefore.Copy();
   var inner=occupied.Elements.First(e=>e.Kind=="message").Copy();
   inner.Id="inner-message";inner.Parent=operand.Id;occupied.Elements.Add(inner);
   var kept=occupied.Copy();kept.Elements.RemoveAll(e=>e.Id==frame.Id || e.Id==operand.Id);
   foreach(var e in kept.Elements)if(e.Parent==operand.Id)e.Parent=occupied.Elements.Single(x=>x.Kind=="interaction").Id;
   foreach(var e in kept.Elements)foreach(var key in e.Links.Keys.ToArray())
       e.Links[key]=e.Links[key].Where(id=>id!=frame.Id && id!=operand.Id).ToArray();
   var keptPlan=new SyncPlan{Expected=kept};
   keptPlan.Changes.Add(new SequenceChange{Action="delete",Kind="fragment",Id=frame.Id});
   keptPlan.Changes.Add(new SequenceChange{Action="delete",Kind="operand",Id=operand.Id});
   var keptGate=SequenceStructurePreflight.Check(occupied,keptPlan);
   if(keptGate.DeleteFragments.Count!=0 || !keptGate.Reasons.Any(r=>r.Contains("中に残す要素")))
       throw new Exception("a fragment whose contents stay was accepted: "+keptGate.ToJson());

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
   var deltaSeed=SequencePayload.Build(types,"fake-view","11.1");
   File.WriteAllText(Path.Combine(args[0], "delta-seed.json"),deltaSeed.Json);
   File.WriteAllText(Path.Combine(args[0], "delta.json"),SequenceDeltaInput.Build(deltaSeed,"messageType","11.1").Json);
   File.WriteAllText(Path.Combine(args[0], "delta-delete.json"),SequenceDeltaInput.RestoreEditor(deltaSeed,"11.1"));
   File.WriteAllText(Path.Combine(args[0], "structure-delete.json"),SequenceStructureInput.WithoutReceiver(deltaSeed,"11.1"));
   var receiveRelation=SequenceJson.Parse(deltaSeed.Json)["Relations"].Items.Single(r=>r["MetamodelId"].StringValue()==SequencePayload.Prefix+"ReceiveMessage");
   File.WriteAllText(Path.Combine(args[0], "structure-reconnect.json"),SequenceStructureInput.ReconnectReceiver(deltaSeed,receiveRelation["Id"].StringValue()));
   bool badLinkRejected=false;
   try { SequenceStructureInput.ReconnectReceiver(deltaSeed,"unknown"); } catch(InvalidOperationException){badLinkRejected=true;}
   if(!badLinkRejected)throw new Exception("unknown receiver relation accepted");
   int rejected=0;
   try { SequencePayload.Build(null, "fake", "13.0"); } catch(ArgumentException) { rejected++; }
   try { SequencePayload.Build(types, "", "13.0"); } catch(ArgumentException) { rejected++; }
   try { SequencePayload.Build(types, "fake", "13.0\\\"}"); } catch(ArgumentException) { rejected++; }
   if (rejected!=3) throw new Exception("invalid input accepted");
   return 0; } catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
 }
}
'''
    pure_file = work / 'Pure.cs'
    pure_file.write_text('using System; using System.Collections.Generic; using System.Linq; using System.IO; using System.Text; using System.Text.RegularExpressions;\n' + pure + runner + (root/'tests/SyncTests.cs').read_text(encoding='utf-8') + (root/'tests/PumlTests.cs').read_text(encoding='utf-8-sig') + (root/'tests/MappingTests.cs').read_text(encoding='utf-8') + (root/'tests/EditorTests.cs').read_text(encoding='utf-8-sig') + (root/'tests/StructurePreparationTests.cs').read_text(encoding='utf-8'), encoding='utf-8-sig')
    compiler = Path(os.environ['WINDIR']) / 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
    exe = work / 'Tests.exe'
    subprocess.run([str(compiler), '/nologo', '/warnaserror+', '/out:' + str(exe), str(pure_file)], check=True)
    subprocess.run([str(exe), str(work), str(root/'samples')], check=True)
    delta = json.loads((work/'delta.json').read_text(encoding='utf-8-sig'))
    seed=json.loads((work/'delta-seed.json').read_text(encoding='utf-8-sig'))
    seed_ids=[e['Id'] for e in seed['Entities']]
    deletion=json.loads((work/'delta-delete.json').read_text(encoding='utf-8-sig'))
    assert deletion['Entities']==[] and deletion['Relations']==[]
    assert deletion['Editors']==seed['Editors']
    assert deletion['TopElementId']==seed['TopElementId']
    structure=json.loads((work/'structure-delete.json').read_text(encoding='utf-8-sig'))
    assert structure['Entities']==[] and structure['Relations']==[]
    expected_editor=json.loads(json.dumps(seed['Editors'][0]))
    expected_editor['ExecutionSpecifications']=[expected_editor['ExecutionSpecifications'][0]]
    assert structure['Editors']==[expected_editor]
    assert structure['TopElementId']==seed_ids[0]
    assert structure['SchemaVersion']=='11.1'
    reconnect=json.loads((work/'structure-reconnect.json').read_text(encoding='utf-8-sig'))
    expected_link=next(r.copy() for r in seed['Relations'] if r['MetamodelId'].endswith('.ReceiveMessage'))
    expected_link['SourceId']=seed_ids[4]
    assert next(e for e in seed['Entities'] if e['Id']==expected_link['SourceId'])['EntityType']=='ExecutionSpecification'
    assert reconnect['Entities']==[] and reconnect['Relations']==[expected_link]
    assert reconnect['Editors']==seed['Editors']
    assert reconnect['SchemaVersion']==seed['SchemaVersion'] and reconnect['TopElementId']==seed['TopElementId']
    added, = delta['Entities']
    assert added['Name']=='deltaProbe()' and added['Fields']['MessageSort']=='Sync'
    assert added['Id'] not in seed_ids
    assert delta['TopElementId']==seed_ids[0]
    assert {r['SourceId'] for r in delta['Relations']}=={seed_ids[i] for i in (0,4,5)}
    assert all(r['TargetId']==added['Id'] for r in delta['Relations'])
    assert len({r['Id'] for r in delta['Relations']})==3
    assert sum(r['RelationType']=='Embed' for r in delta['Relations'])==1
    editor,=delta['Editors']
    original,=seed['Editors']
    shape=editor['Messages'][-1]
    assert shape['ModelId']==added['Id'] and shape['SourceY']==shape['TargetY']==120
    assert editor['Messages'][:-1]==original['Messages']
    assert {k:v for k,v in editor.items() if k!='Messages'}=={k:v for k,v in original.items() if k!='Messages'}
    assert len(delta['Entities'])==1 and len(delta['Relations'])==3
    replacement = json.loads((work/'replacement.json').read_text(encoding='utf-8-sig'))
    assert replacement['TopElementId']=='existing-root'
    replacement_entities={e['Id']:e for e in replacement['Entities']}
    assert replacement_entities['existing-root']['EntityType']=='Interaction'
    assert replacement_entities['existing-frame']['EntityType']=='Frame'
    frame_relation=next(r for r in replacement['Relations'] if r['Id']=='existing-frame-relation')
    assert frame_relation['SourceId']=='existing-root' and frame_relation['TargetId']=='existing-frame'
    replacement_editor,=replacement['Editors']
    assert replacement_editor['Id']=='existing-editor' and replacement_editor['ModelId']=='existing-root'
    assert replacement_editor['Frame']['Id']=='existing-frame-shape'
    assert replacement_editor['Frame']['ModelId']=='existing-frame'
    assert all(r['SourceId'] in replacement_entities and r['TargetId'] in replacement_entities for r in replacement['Relations'])
    assert len({r['Id'] for r in replacement['Relations']})==len(replacement['Relations'])
    update_seed = json.loads((work/'update-seed.json').read_text(encoding='utf-8-sig'))
    update_changed = json.loads((work/'update-changed.json').read_text(encoding='utf-8-sig'))
    for entity in update_seed['Entities']:
        if entity['EntityType']=='Message':
            entity['Name']='updatedProbe()'
            entity['Fields']['Name']='updatedProbe()'
    assert update_seed == update_changed, 'Update probe must preserve all IDs, relations and shapes'
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
                link = next(r for r in relations if r['MetamodelId']=='DestructionTargetLifeline' and r['TargetId']==lifelines[name])
                destruction = link['SourceId']
                assert entities[link['SourceId']]['EntityType']=='Destruction'
                assert entities[link['TargetId']]['EntityType']=='Lifeline'
                assert link['RelationType']=='Ref'
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
        start = source.index('public void ')
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
