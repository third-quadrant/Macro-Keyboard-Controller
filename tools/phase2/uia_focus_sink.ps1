param([int]$SinkPid)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process -Id $SinkPid -ErrorAction SilentlyContinue
if ($null -eq $proc) { Write-Output 'SINK GONE'; exit 4 }
$sink = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
if ($null -eq $sink) { Write-Output 'SINK WINDOW NOT FOUND'; exit 5 }
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edit = $sink.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($null -eq $edit) { Write-Output 'EDIT NOT FOUND'; exit 6 }
$edit.SetFocus()
Write-Output 'FOCUSED'
