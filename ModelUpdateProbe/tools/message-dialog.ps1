param([Parameter(Mandatory=$true)][string]$InputPath, [Parameter(Mandatory=$true)][string]$OutputPath, [switch]$SelfTest)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$data = Get-Content -LiteralPath $InputPath -Raw -Encoding UTF8 | ConvertFrom-Json
$form = New-Object System.Windows.Forms.Form
$form.Text = 'メッセージの名前を変更'
$form.ClientSize = New-Object System.Drawing.Size(760, 550)
$form.MinimumSize = New-Object System.Drawing.Size(640, 500)
$form.StartPosition = 'CenterScreen'
$form.Font = New-Object System.Drawing.Font('Yu Gothic UI', 11)
$form.AutoScaleMode = 'Dpi'
$layout = New-Object System.Windows.Forms.TableLayoutPanel
$layout.Dock = 'Fill'; $layout.Padding = 16; $layout.ColumnCount = 1; $layout.RowCount = 6
foreach ($height in @(32, 0, 30, 76, 54, 46)) {
 $style = New-Object System.Windows.Forms.RowStyle
 if ($height -eq 0) { $style.SizeType = 'Percent'; $style.Height = 100 } else { $style.SizeType = 'Absolute'; $style.Height = $height }
 [void]$layout.RowStyles.Add($style)
}
$form.Controls.Add($layout)
$label = New-Object System.Windows.Forms.Label
$label.Text = '1. 変更するメッセージを選ぶ'; $label.Dock = 'Fill'
$layout.Controls.Add($label,0,0)
$list = New-Object System.Windows.Forms.ListBox
$list.Dock = 'Fill'; $list.IntegralHeight = $false; $list.HorizontalScrollbar = $true
for ($i=0; $i -lt [int]$data.count; $i++) { [void]$list.Items.Add(('{0}. {1}' -f ($i+1), [string]$data."name$i")) }
$layout.Controls.Add($list,0,1)
$label2 = New-Object System.Windows.Forms.Label
$label2.Text = '2. 変更後の名前を入力する'; $label2.Dock = 'Fill'
$layout.Controls.Add($label2,0,2)
$value = New-Object System.Windows.Forms.TextBox
$value.Dock = 'Fill'; $value.Multiline = $true; $value.ScrollBars = 'Vertical'; $value.MaxLength = 4000
$layout.Controls.Add($value,0,3)
$hint = New-Object System.Windows.Forms.Label
$hint.Text = '名前を変更した後、図上の表示も確認してください。次の確認画面でOKを押すまでは変更されません。'
$hint.Dock = 'Fill'; $layout.Controls.Add($hint,0,4)
$buttons = New-Object System.Windows.Forms.FlowLayoutPanel
$buttons.Dock = 'Fill'; $buttons.FlowDirection = 'RightToLeft'
$cancel = New-Object System.Windows.Forms.Button
$cancel.Text = 'キャンセル'; $cancel.AutoSize = $true; $cancel.DialogResult = 'Cancel'
$ok = New-Object System.Windows.Forms.Button
$ok.Text = '変更内容を確認'; $ok.AutoSize = $true; $ok.Enabled = $false
$buttons.Controls.Add($cancel); $buttons.Controls.Add($ok); $layout.Controls.Add($buttons,0,5)
$form.CancelButton = $cancel
$list.Add_SelectedIndexChanged({
 if ($list.SelectedIndex -ge 0) { $value.Text = [string]$data.('name'+$list.SelectedIndex); $ok.Enabled = $false }
})
$value.Add_TextChanged({
 $ok.Enabled = $list.SelectedIndex -ge 0 -and $value.Text -cne [string]$data.('name'+$list.SelectedIndex)
})
$accept = {
 if (-not $ok.Enabled) { return }
 $result = @{ index=[string]$list.SelectedIndex; value=$value.Text } | ConvertTo-Json
 $bytes = [System.Text.UTF8Encoding]::new($true).GetBytes($result)
 $stream = [System.IO.File]::Open($OutputPath, 'CreateNew', 'Write', 'None')
 try { $stream.Write($bytes,0,$bytes.Length) } finally { $stream.Dispose() }
 $form.DialogResult = 'OK'; $form.Close()
}
$ok.Add_Click($accept)
try {
 if ($SelfTest) {
  if ($ok.Enabled -or $list.SelectedIndex -ne -1) { throw 'Initial selection must be empty' }
  $list.SelectedIndex = 1
  if ($ok.Enabled) { throw 'Unchanged name accepted' }
  $value.Text = 'Changed "message"'
  if (-not $ok.Enabled) { throw 'Changed name rejected' }
  $form.StartPosition = "Manual"; $form.Location = New-Object System.Drawing.Point(-30000,-30000)
  $form.Show(); [System.Windows.Forms.Application]::DoEvents()
  $bitmap = New-Object System.Drawing.Bitmap($form.Width, $form.Height)
  try { $form.DrawToBitmap($bitmap, (New-Object System.Drawing.Rectangle(0,0,$form.Width,$form.Height))); $bitmap.Save($OutputPath+'.png') } finally { $bitmap.Dispose() }
  & $accept
 } else { [void]$form.ShowDialog() }
} finally { $form.Dispose() }
