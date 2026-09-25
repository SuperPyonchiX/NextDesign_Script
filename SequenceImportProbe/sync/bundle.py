"""Bundle tested sync sources into the single Next Design script entrypoint."""
import argparse
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
path = root / 'main.cs'
source = path.read_text(encoding='utf-8-sig')
for name, anchor in [('SequenceSyncRuntime.cs', '// Pure JSON builder.'), ('SequenceSync.cs', None)]:
    content = (root / 'sync' / name).read_text(encoding='utf-8') if (root / 'sync' / name).exists() else ''
    if not content:
        continue
    start = '// BEGIN GENERATED ' + name
    end = '// END GENERATED ' + name
    block = start + '\n' + content.rstrip() + '\n' + end + '\n'
    if start in source:
        a = source.index(start)
        b = source.index(end, a) + len(end)
        source = source[:a] + block.rstrip('\n') + source[b:]
    elif anchor:
        source = source.replace(anchor, block + '\n' + anchor, 1)
    else:
        source = source.rstrip() + '\n\n' + block
# The PlantUML exporter, copied unchanged from PlantUmlTool (which is never edited from here),
# so the all-diagram check exports exactly what PlantUmlTool writes.
export_source = (root.parent / 'PlantUmlTool' / 'src' / '10-sequence-export.cs').read_text(encoding='utf-8-sig')
cut = export_source.index('//  出力対象（図とその所有モデルのペア）')
cut = export_source.rindex('// ----', 0, cut)
name = 'PlantUmlTool/src/10-sequence-export.cs (exporter)'
start = '// BEGIN GENERATED ' + name
end = '// END GENERATED ' + name
block = start + '\n' + export_source[:cut].rstrip() + '\n' + end + '\n'
if start in source:
    a = source.index(start)
    b = source.index(end, a) + len(end)
    source = source[:a] + block.rstrip('\n') + source[b:]
else:
    source = source.replace('// Pure JSON builder.', block + '\n' + '// Pure JSON builder.', 1)
if args.check:
    if path.read_text(encoding='utf-8-sig') != source:
        raise SystemExit('Sync bundle is stale: run python SequenceImportProbe/sync/bundle.py')
else:
    path.write_text(source, encoding='utf-8-sig')
