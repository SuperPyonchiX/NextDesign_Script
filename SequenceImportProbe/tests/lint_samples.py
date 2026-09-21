"""Report samples PlantUML itself refuses.

PlantUML ties activate and deactivate to the message above them, so a participant
deactivated since the last message cannot be activated again until another message
is written. A bar that encloses no message runs into this as soon as the next bar
on the same participant opens. The exporter never writes such a pair; only
hand-written samples did, and PlantUML would not render them.

Run from the repository root. Not wired into run_tests.py yet: one sample still
fails while the rule is being confirmed on a real PlantUML renderer.
"""
import pathlib, re, sys

MESSAGE = re.compile(r'^\s*[^\s:]+\s*(->>?|-->>?|<<?-|<<--)\s*[^\s:]+')

def offences(path):
    found = []
    closed = set()
    for number, raw in enumerate(path.read_text(encoding='utf-8').splitlines(), 1):
        line = raw.strip()
        if MESSAGE.match(line):
            closed = set()
            continue
        parts = line.split()
        if len(parts) == 2 and parts[0] == 'deactivate':
            closed.add(parts[1])
            continue
        if len(parts) == 2 and parts[0] == 'activate':
            if parts[1] in closed:
                found.append((number, raw.strip()))
            continue
    return found

root = pathlib.Path('SequenceImportProbe/samples')
bad = 0
for path in sorted(root.glob('*.puml')):
    rows = offences(path)
    if rows:
        bad += 1
        print(path.name)
        for number, text in rows:
            print('  L%d %s' % (number, text))
print('samples PlantUML would refuse: %d' % bad)
sys.exit(0)
