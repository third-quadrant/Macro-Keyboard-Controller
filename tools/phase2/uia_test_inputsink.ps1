param([int]$SinkPid)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$fireCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    'Fireworm — Macro Keyboard Controller')
$fire = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $fireCond)
if ($null -eq $fire) { Write-Output 'FIREWORM NOT FOUND'; exit 2 }

$btnId = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'SimKey1')
$btn = $fire.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnId)
if ($null -eq $btn) { Write-Output 'BUTTON NOT FOUND'; exit 3 }

$sinkProc = Get-Process -Id $SinkPid -ErrorAction SilentlyContinue
if ($null -eq $sinkProc) { Write-Output 'SINK GONE'; exit 4 }
$sink = [System.Windows.Automation.AutomationElement]::FromHandle($sinkProc.MainWindowHandle)
if ($null -eq $sink) { Write-Output 'SINK WINDOW NOT FOUND'; exit 5 }

$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edit = $sink.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($null -eq $edit) { Write-Output 'EDIT NOT FOUND'; exit 6 }

$ws = New-Object -ComObject WScript.Shell
$null = $ws.AppActivate($SinkPid)
Start-Sleep -Milliseconds 500

$invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
# Give the app's worker a moment, then make sure the sink is the foreground target.
Start-Sleep -Milliseconds 120
$null = $ws.AppActivate($SinkPid)
Start-Sleep -Milliseconds 1000

$tp = $edit.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
$text = $tp.DocumentRange.GetText(-1)
Write-Output ('TEXT=' + $text.Replace("`r", '\r').Replace("`n", '\n'))
