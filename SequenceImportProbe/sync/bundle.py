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
if args.check:
    if path.read_text(encoding='utf-8-sig') != source:
        raise SystemExit('Sync bundle is stale: run python SequenceImportProbe/sync/bundle.py')
else:
    path.write_text(source, encoding='utf-8-sig')
