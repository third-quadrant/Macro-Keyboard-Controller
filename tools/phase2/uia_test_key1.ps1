param([int]$NpId)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    'Fireworm — Macro Keyboard Controller')
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($null -eq $win) { Write-Output 'WINDOW NOT FOUND'; exit 2 }

$id = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'SimKey1')
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $id)
if ($null -eq $btn) { Write-Output 'BUTTON NOT FOUND'; exit 3 }

$ws = New-Object -ComObject WScript.Shell
$null = $ws.AppActivate($NpId)
Start-Sleep -Milliseconds 300

$invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
Start-Sleep -Milliseconds 900

$np = Get-Process -Id $NpId -ErrorAction SilentlyContinue
if ($null -eq $np) { Write-Output 'NOTEPAD GONE'; exit 4 }
$npWin = [System.Windows.Automation.AutomationElement]::FromHandle($np.MainWindowHandle)
$editCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Edit)
$edit = $npWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $editCond)
if ($null -eq $edit) { Write-Output 'EDIT NOT FOUND'; exit 5 }
$tp = $edit.GetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern)
$text = $tp.DocumentRange.GetText(-1)
Write-Output ('TEXT=' + $text.Replace("`r", '\r').Replace("`n", '\n'))
