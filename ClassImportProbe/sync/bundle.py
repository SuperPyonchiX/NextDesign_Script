"""Bundle the shared class sync sources into this extension's single script entrypoint.

The sources now live in PlantUmlTool/src (60-class-sync.cs, 61-class-sync-runtime.cs); this
extension only embeds them between the GENERATED markers. Edit them there and re-run this.
"""
import argparse
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
shared = root.parent / 'PlantUmlTool' / 'src'
path = root / 'main.cs'
source = path.read_text(encoding='utf-8-sig')
for name, shared_name in [('ClassSyncRuntime.cs', '61-class-sync-runtime.cs'), ('ClassSync.cs', '60-class-sync.cs')]:
    content = (shared / shared_name).read_text(encoding='utf-8-sig')
    start = '// BEGIN GENERATED ' + name
    end = '// END GENERATED ' + name
    if start not in source or end not in source:
        raise SystemExit('main.cs lacks the generated markers for ' + name)
    a = source.index(start)
    b = source.index(end, a) + len(end)
    source = source[:a] + start + '\n' + content.rstrip() + '\n' + end + source[b:]
if args.check:
    if path.read_text(encoding='utf-8-sig') != source:
        raise SystemExit('Sync bundle is stale: run python ClassImportProbe/sync/bundle.py')
else:
    path.write_text(source, encoding='utf-8-sig')
