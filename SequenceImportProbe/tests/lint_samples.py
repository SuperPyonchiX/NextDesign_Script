"""Report samples PlantUML itself refuses.

PlantUML ties activate and deactivate to the message above them, so a participant
deactivated since the last message cannot be activated again until another message
is written. A bar that encloses no message runs into this as soon as the next bar
on the same participant opens. The exporter never writes such a pair; only
hand-written samples did, and PlantUML would not render them.

Confirmed on PlantUML 1.2024.3: reopening a bar this way errors with
"Activate/Deactivate already done", and writing a message in between fixes it.
An activate before any message is fine.

Run from the repository root; run_tests.py calls check() as part of the suite.
"""
import pathlib, re

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
                found.append((number, line))
            continue
    return found


def check(root):
    samples = sorted(pathlib.Path(root).glob('*.puml'))
    bad = []
    for path in samples:
        for number, text in offences(path):
            bad.append('%s L%d %s' % (path.name, number, text))
    if bad:
        raise SystemExit('PlantUML would refuse these samples:\n  ' + '\n  '.join(bad))
    return len(samples)


if __name__ == '__main__':
    print('samples PlantUML accepts: %d' % check('SequenceImportProbe/samples'))
