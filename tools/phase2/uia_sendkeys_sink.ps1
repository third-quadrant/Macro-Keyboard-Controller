param([int]$SinkPid)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$proc = Get-Process -Id $SinkPid -ErrorAction SilentlyContinue
if ($null -eq $proc) { Write-Output 'SINK GONE'; exit 4 }
$sink = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edit = $sink.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($null -eq $edit) { Write-Output 'EDIT NOT FOUND'; exit 5 }

$edit.SetFocus()
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait('c')
Start-Sleep -Milliseconds 500

$text = ''
try {
    $tp = $edit.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
    $text = $tp.DocumentRange.GetText(-1)
} catch { $text = 'TEXT_ERR:' + $_.Exception.Message }

$value = ''
try {
    $vp = $edit.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    $value = $vp.Current.Value
} catch { $value = 'VALUE_ERR:' + $_.Exception.Message }

Write-Output ('TEXT=' + $text.Replace("`r", '\r').Replace("`n", '\n'))
Write-Output ('VALUE=' + $value.Replace("`r", '\r').Replace("`n", '\n'))
