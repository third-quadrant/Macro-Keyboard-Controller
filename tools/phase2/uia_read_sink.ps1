param([int]$SinkPid)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Id $SinkPid -ErrorAction SilentlyContinue
if ($null -eq $proc) { Write-Output 'SINK GONE'; exit 4 }
$sink = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edit = $sink.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($null -eq $edit) { Write-Output 'EDIT NOT FOUND'; exit 5 }
$tp = $edit.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
$text = $tp.DocumentRange.GetText(-1)
Write-Output ('TEXT=' + $text.Replace("`r", '\r').Replace("`n", '\n'))
