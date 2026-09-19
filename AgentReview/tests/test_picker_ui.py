"""Exercise real WinForms controls without opening an interactive window."""
from pathlib import Path
import subprocess
import tempfile
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
source = (root / 'resources/Select-ReviewInputs.ps1').read_text(encoding='utf-8-sig')
marker = 'try { [void]$form.ShowDialog() }'
assert marker in source
with tempfile.TemporaryDirectory(prefix='agentreview-ui-') as tmp:
    directory = Path(tmp)
    request = '''<request settingsOnly="false"><target>設計/日本語 &amp; target</target><choices>
    <model id="root" parent="" name="プロジェクト" path="プロジェクト" available="true"/>
    <model id="a" parent="root" name="同名" path="プロジェクト/A/同名" available="true"/>
    <model id="b" parent="root" name="同名" path="プロジェクト/B/同名" available="true"/>
    <model id="proxy" parent="root" name="未ロード" path="未ロード" available="false"/>
    </choices><settings><phase key="detailed"><model>a</model></phase>
    <phase key="architecture"><model>b</model></phase></settings></request>'''
    (directory/'request.xml').write_text(request, encoding='utf-8')
    common = r'''
    if ($accept.Enabled) { throw 'Phase must be selected' }
    $combo.SelectedIndex = 2
    if ($state.Phase -ne 'detailed' -or !$accept.Enabled -or $list.Items[0].Text -ne 'プロジェクト/A/同名') { throw 'Detailed restoration' }
    $combo.SelectedIndex = 1
    if ($list.Items[0].Text -ne 'プロジェクト/B/同名') { throw 'Architecture restoration' }
    $combo.SelectedIndex = 2
    $search.Text = 'B/同名'
    if ($tree.Nodes.Count -ne 1 -or $tree.Nodes[0].Nodes.Count -ne 1 -or $tree.Nodes[0].Nodes[0].Tag -ne 'b') { throw 'Search hierarchy' }
    $tree.Nodes[0].Nodes[0].Checked = $true
    if ($list.Items.Count -ne 2) { throw 'Add through tree check event' }
    $search.Text = ''
    if ($list.Items.Count -ne 2) { throw 'Search cleared selection' }
    $combo.SelectedIndex = 1; $combo.SelectedIndex = 2
    if ($list.Items.Count -ne 2) { throw 'Switch lost edits' }
    $state.Models['detailed']['deleted'] = $true; Refresh-Selection
    if ($accept.Enabled) { throw 'Deleted model accepted' }
    $state.Models['detailed'].Remove('deleted')
    $state.Files['detailed']['Z:missing-agentreview-file.pdf'] = $true; Refresh-Selection
    if ($accept.Enabled) { throw 'Missing file accepted' }
    $state.Files['detailed'].Clear(); Refresh-Selection
    if (!$accept.Enabled) { throw 'Valid selection disabled' }
    '''
    for action in ['accept', 'none', 'cancel']:
        script = source.replace(marker, "try {\n" + common + "\n$state.Action = '" + action + "'\n}")
        script_path = directory/'picker-test.ps1'
        script_path.write_text(script, encoding='utf-8-sig')
        subprocess.run(['powershell', '-NoProfile', '-STA', '-File', str(script_path),
                        '-RequestPath', str(directory/'request.xml'), '-ResponsePath', str(directory/'response.xml')],
                       check=True, timeout=30)
        result = ET.parse(directory/'response.xml').getroot()
        assert result.get('action') == action
        if action == 'accept':
            assert {n.text for n in result.findall('./selection/model')} == {'a', 'b'}
            assert [n.text for n in result.findall("./settings/phase[@key='architecture']/model")] == ['b']
        elif action == 'none':
            assert result.findall('./selection/model') == []
            assert [n.text for n in result.findall("./settings/phase[@key='detailed']/model")] == ['a']
        else:
            assert result.find('settings') is None
    print('PASS: real WinForms phase switching, search, selection, missing inputs, accept/none/cancel XML')
