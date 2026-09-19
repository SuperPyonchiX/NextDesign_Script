param([Parameter(Mandatory=$true)][string]$RequestPath,
      [Parameter(Mandatory=$true)][string]$ResponsePath)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$xmlOptions = New-Object System.Xml.XmlReaderSettings
$xmlOptions.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
$xmlOptions.XmlResolver = $null
$reader = [System.Xml.XmlReader]::Create($RequestPath, $xmlOptions)
$request = New-Object System.Xml.XmlDocument
$request.XmlResolver = $null
try { $request.Load($reader) } finally { $reader.Dispose() }
$phaseKeys = @('requirements','architecture','detailed')
$phaseLabels = @('要件分析','アーキ設計','詳細設計')
$guidance = @('上位要求・関連資料を選択してください。','要件分析書のモデル・資料を選択してください。','アーキ設計のモデル・資料を選択してください。')
$state = @{ Phase = ''; Busy = $false; Action = 'cancel'; Models = @{}; Files = @{} }
$catalog = @{}
foreach ($model in $request.SelectNodes('/request/choices/model')) { $catalog[$model.id] = $model }
foreach ($key in $phaseKeys) {
    $state.Models[$key] = @{}
    $state.Files[$key] = @{}
    foreach ($phase in $request.SelectNodes('/request/settings/phase')) {
        if ($phase.key -ne $key) { continue }
        foreach ($node in $phase.SelectNodes('model')) { $state.Models[$key][$node.InnerText] = $true }
        foreach ($node in $phase.SelectNodes('file')) { $state.Files[$key][$node.InnerText] = $true }
    }
}
$form = New-Object System.Windows.Forms.Form
$form.Text = 'レビュー工程・上位文書の選択'
$form.Size = New-Object System.Drawing.Size(1100,760)
$form.MinimumSize = New-Object System.Drawing.Size(800,600)
$form.StartPosition = 'CenterScreen'
$form.AutoScaleMode = 'Dpi'
$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'; $layout.Padding = New-Object System.Windows.Forms.Padding(12)
$layout.ColumnCount = 1; $layout.RowCount = 5
foreach ($height in @(65,38,55)) { [void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute',$height))) }
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Percent',100)))
[void]$layout.RowStyles.Add((New-Object System.Windows.Forms.RowStyle('Absolute',48)))
$form.Controls.Add($layout)
$target = New-Object System.Windows.Forms.Label
$target.Text = "レビュー対象: $($request.request.target)"; $target.Dock = 'Fill'; $target.AutoEllipsis = $true
$tips = New-Object System.Windows.Forms.ToolTip
$tips.SetToolTip($target,$target.Text)
$layout.Controls.Add($target,0,0)
$phaseRow = New-Object System.Windows.Forms.FlowLayoutPanel
$phaseRow.Dock = 'Fill'
$label = New-Object System.Windows.Forms.Label
$label.Text = '対象工程'; $label.AutoSize = $true; $label.Padding = New-Object System.Windows.Forms.Padding(0,6,8,0)
$combo = New-Object System.Windows.Forms.ComboBox
$combo.DropDownStyle = 'DropDownList'; $combo.Width = 220
$combo.Items.AddRange([object[]]$phaseLabels)
$phaseRow.Controls.Add($label); $phaseRow.Controls.Add($combo)
$layout.Controls.Add($phaseRow,0,1)
$hint = New-Object System.Windows.Forms.Label
$hint.Dock = 'Fill'; $hint.Text = '対象工程を選択してください。'
$layout.Controls.Add($hint,0,2)
$split = New-Object System.Windows.Forms.SplitContainer
$split.Dock = 'Fill'; $split.Size = New-Object System.Drawing.Size(1050,470); $split.SplitterDistance = 510
$layout.Controls.Add($split,0,3)
$search = New-Object System.Windows.Forms.TextBox
$search.Dock = 'Top'
$tips.SetToolTip($search,'モデル名・パスで検索（部分一致）')
$tree = New-Object System.Windows.Forms.TreeView
$tree.Dock = 'Fill'; $tree.CheckBoxes = $true; $tree.HideSelection = $false; $tree.ShowNodeToolTips = $true
$leftTitle = New-Object System.Windows.Forms.Label
$leftTitle.Text = 'モデル名・パスで検索 / チェックしたモデルは配下も出力'; $leftTitle.Dock = 'Top'; $leftTitle.Height = 30
$split.Panel1.Controls.Add($tree); $split.Panel1.Controls.Add($search); $split.Panel1.Controls.Add($leftTitle)
$list = New-Object System.Windows.Forms.ListView
$list.Dock = 'Fill'; $list.View = 'Details'; $list.FullRowSelect = $true; $list.MultiSelect = $true
[void]$list.Columns.Add('選択済みの上位文書（フルパス）',480)
$split.Panel2.Controls.Add($list)
$fileButtons = New-Object System.Windows.Forms.FlowLayoutPanel
$fileButtons.Dock = 'Bottom'; $fileButtons.Height = 40
function New-Button([string]$text) {
    $button = New-Object System.Windows.Forms.Button
    $button.Text = $text; $button.AutoSize = $true; $button.Height = 32
    return $button
}
$addFile = New-Button '資料ファイルを追加'
$remove = New-Button '選択を解除'
$fileButtons.Controls.Add($addFile); $fileButtons.Controls.Add($remove)
$split.Panel2.Controls.Add($fileButtons)
$buttons = New-Object System.Windows.Forms.FlowLayoutPanel
$buttons.Dock = 'Fill'; $buttons.FlowDirection = 'RightToLeft'
$cancel = New-Button 'キャンセル'
$none = New-Button '今回は上位文書なし'
$accept = New-Button $(if ($request.request.settingsOnly -eq 'true') { '選択を保存' } else { 'レビュー開始' })
$none.Visible = $request.request.settingsOnly -ne 'true'
$buttons.Controls.Add($cancel); $buttons.Controls.Add($none); $buttons.Controls.Add($accept)
$layout.Controls.Add($buttons,0,4)
$form.CancelButton = $cancel

function Refresh-Selection {
    $list.Items.Clear()
    $valid = $true
    if ($state.Phase) {
        foreach ($id in @($state.Models[$state.Phase].Keys | Sort-Object)) {
            $model = $catalog[$id]
            $available = $null -ne $model -and $model.available -eq 'true'
            $name = if ($null -eq $model) { "[削除済み] $id" } elseif (!$available) { "[未ロード・選択不可] $($model.path)" } else { [string]$model.path }
            $item = New-Object System.Windows.Forms.ListViewItem($name)
            $item.Tag = @{ Kind = 'model'; Value = $id }; [void]$list.Items.Add($item)
            if (!$available) { $item.ForeColor = [System.Drawing.Color]::Firebrick; $valid = $false }
        }
        foreach ($path in @($state.Files[$state.Phase].Keys | Sort-Object)) {
            $exists = [System.IO.File]::Exists($path)
            $item = New-Object System.Windows.Forms.ListViewItem($(if ($exists) { $path } else { "[資料なし] $path" }))
            $item.Tag = @{ Kind = 'file'; Value = $path }; [void]$list.Items.Add($item)
            if (!$exists) { $item.ForeColor = [System.Drawing.Color]::Firebrick; $valid = $false }
        }
    }
    $accept.Enabled = [bool]$state.Phase -and $list.Items.Count -gt 0 -and $valid
    $none.Enabled = [bool]$state.Phase
    $addFile.Enabled = [bool]$state.Phase
    $tree.Enabled = [bool]$state.Phase
}
function Refresh-Tree {
    $state.Busy = $true; $tree.BeginUpdate()
    try {
        $tree.Nodes.Clear(); $visible = @{}; $nodes = @{}
        foreach ($id in $catalog.Keys) {
            $model = $catalog[$id]
            if ($search.Text.Length -eq 0 -or ([string]$model.path).IndexOf($search.Text,[System.StringComparison]::OrdinalIgnoreCase) -ge 0 -or ([string]$model.name).IndexOf($search.Text,[System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $current = $id; $seen = @{}
                while ($current -and $catalog.ContainsKey($current) -and !$seen.ContainsKey($current)) {
                    $visible[$current] = $true; $seen[$current] = $true; $current = [string]$catalog[$current].parent
                }
            }
        }
        foreach ($id in $visible.Keys) {
            $model = $catalog[$id]
            $node = New-Object System.Windows.Forms.TreeNode([string]$model.name)
            $node.Tag = $id; $node.ToolTipText = [string]$model.path
            $node.Checked = [bool]($state.Phase -and $state.Models[$state.Phase].ContainsKey($id))
            if ($model.available -ne 'true') { $node.ForeColor = [System.Drawing.Color]::Gray }
            $nodes[$id] = $node
        }
        foreach ($id in @($visible.Keys | Sort-Object { [string]$catalog[$_].path })) {
            $parent = [string]$catalog[$id].parent
            if ($parent -ne $id -and $nodes.ContainsKey($parent)) { [void]$nodes[$parent].Nodes.Add($nodes[$id]) }
            else { [void]$tree.Nodes.Add($nodes[$id]) }
        }
        if ($search.Text.Length -gt 0) { $tree.ExpandAll() }
        else { foreach ($node in $tree.Nodes) { $node.Expand() } }
    } finally { $tree.EndUpdate(); $state.Busy = $false }
}
$combo.Add_SelectedIndexChanged({
    $state.Phase = $phaseKeys[$combo.SelectedIndex]
    $hint.Text = $guidance[$combo.SelectedIndex] + "`r`n工程と上位文書は今回のレビューに引き継がれます。上位文書なしの場合、上位整合は未確認になります。"
    Refresh-Tree; Refresh-Selection
})
$search.Add_TextChanged({ Refresh-Tree })
$tree.Add_AfterCheck({ param($sender,$eventArgs)
    if ($state.Busy -or !$state.Phase) { return }
    $id = [string]$eventArgs.Node.Tag
    if ($eventArgs.Node.Checked -and $catalog[$id].available -ne 'true') {
        $state.Busy = $true; $eventArgs.Node.Checked = $false; $state.Busy = $false
    }
    if ($eventArgs.Node.Checked) { $state.Models[$state.Phase][$id] = $true }
    else { $state.Models[$state.Phase].Remove($id) }
    Refresh-Selection
})
$remove.Add_Click({
    foreach ($item in @($list.SelectedItems)) {
        if ($item.Tag.Kind -eq 'model') { $state.Models[$state.Phase].Remove($item.Tag.Value) }
        else { $state.Files[$state.Phase].Remove($item.Tag.Value) }
    }
    Refresh-Tree; Refresh-Selection
})
$addFile.Add_Click({
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Multiselect = $true; $dialog.Title = '上位資料を選択'
    try {
        if ($dialog.ShowDialog($form) -eq 'OK') {
            foreach ($path in $dialog.FileNames) { $state.Files[$state.Phase][$path] = $true }
            Refresh-Selection
        }
    } finally { $dialog.Dispose() }
})
$cancel.Add_Click({ $form.Close() })
$accept.Add_Click({ Refresh-Selection; if ($accept.Enabled) { $state.Action = 'accept'; $form.Close() } })
$none.Add_Click({ $state.Action = 'none'; $form.Close() })
$last = [string]$request.request.settings.lastPhase
if ($phaseKeys -contains $last) { $combo.SelectedIndex = [array]::IndexOf($phaseKeys,$last) }
else { Refresh-Tree; Refresh-Selection }
$form.Add_Shown({ $form.Activate(); $combo.Focus() })
try { [void]$form.ShowDialog() } finally { $form.Dispose(); $tips.Dispose() }

$response = New-Object System.Xml.XmlDocument
$result = $response.CreateElement('result'); [void]$response.AppendChild($result)
$result.SetAttribute('action',$state.Action); $result.SetAttribute('phase',$state.Phase)
function Add-Selections($element,[string]$key) {
    foreach ($id in @($state.Models[$key].Keys | Sort-Object)) {
        $node = $response.CreateElement('model'); $node.InnerText = $id; [void]$element.AppendChild($node)
    }
    foreach ($path in @($state.Files[$key].Keys | Sort-Object)) {
        $node = $response.CreateElement('file'); $node.InnerText = $path; [void]$element.AppendChild($node)
    }
}
if ($state.Action -ne 'cancel') {
    $selection = $response.CreateElement('selection'); [void]$result.AppendChild($selection)
    if ($state.Action -eq 'accept') { Add-Selections $selection $state.Phase }
    $settings = $response.CreateElement('settings'); $settings.SetAttribute('lastPhase',$state.Phase)
    [void]$result.AppendChild($settings)
    foreach ($key in $phaseKeys) {
        # 「今回なし」は保存済みの選択を変更しない。
        if ($state.Action -eq 'none') {
            foreach ($original in $request.SelectNodes('/request/settings/phase')) {
                if ($original.key -eq $key) { [void]$settings.AppendChild($response.ImportNode($original,$true)) }
            }
        } else {
            $phase = $response.CreateElement('phase'); $phase.SetAttribute('key',$key)
            Add-Selections $phase $key; [void]$settings.AppendChild($phase)
        }
    }
}
$response.Save($ResponsePath)
