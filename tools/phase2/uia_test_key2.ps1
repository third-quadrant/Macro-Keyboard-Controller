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
    'SimKey2')
$btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $id)
if ($null -eq $btn) { Write-Output 'BUTTON NOT FOUND'; exit 3 }

$invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
Write-Output 'INVOKED'
