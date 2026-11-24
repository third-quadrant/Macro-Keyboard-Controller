param([int]$AppPid)

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
if ($null -eq $btn) { Write-Output 'BUTTON NOT FOUND'; exit 3 }
Write-Output ("BUTTON enabled=" + $btn.Current.IsEnabled + " name=" + $btn.Current.Name)
$btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 1200

Write-Output '--- top-level windows for app pid ---'
$all = $root.FindAll([System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($w in $all) {
    if ($w.Current.ProcessId -eq $AppPid) {
        Write-Output ("NAME=[" + $w.Current.Name + "] CLASS=[" + $w.Current.ClassName + "] TYPE=[" + $w.Current.ControlType.ProgrammaticName + "]")
    }
}
