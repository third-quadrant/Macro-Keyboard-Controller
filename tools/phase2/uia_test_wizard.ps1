Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$fireCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    'Fireworm — Macro Keyboard Controller')
$fire = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $fireCond)
if ($null -eq $fire) { Write-Output 'FIREWORM NOT FOUND'; exit 2 }

$btnCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
    'OpenTriggerWizard')
$btn = $fire.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($null -eq $btn) { Write-Output 'WIZARD BUTTON NOT FOUND'; exit 3 }
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 800

$wizCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    '장치 트리거 설정')
$wiz = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $wizCond)
if ($null -eq $wiz) { Write-Output 'WIZARD NOT FOUND'; exit 4 }
Write-Output 'WIZARD-OPEN'
$wiz.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
Start-Sleep -Milliseconds 400
Write-Output 'WIZARD-CLOSED'
