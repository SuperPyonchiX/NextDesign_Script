"""Bundle the tested sync sources into the single Next Design script entrypoint."""
import argparse
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
path = root / 'main.cs'
source = path.read_text(encoding='utf-8-sig')
for name in ['ClassSyncRuntime.cs', 'ClassSync.cs']:
    content = (root / 'sync' / name).read_text(encoding='utf-8')
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
