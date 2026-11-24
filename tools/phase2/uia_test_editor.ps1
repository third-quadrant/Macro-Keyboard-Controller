Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty,
    'Fireworm — Macro Keyboard Controller')
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if ($null -eq $win) { Write-Output 'WINDOW NOT FOUND'; exit 2 }

function Find-ById($id) {
    $c = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

$card = Find-ById 'CardKey1'
if ($null -eq $card) { Write-Output 'CARD NOT FOUND'; exit 3 }
$card.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 300

$combo = Find-ById 'ActionTypeCombo'
if ($null -eq $combo) { Write-Output 'COMBO NOT FOUND'; exit 4 }
$expand = $combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
$expand.Expand()
Start-Sleep -Milliseconds 300

$itemCond = New-Object System.Windows.Automation.AndCondition(
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)),
    (New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        '키 입력')))
$item = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $itemCond)
if ($null -eq $item) { Write-Output 'ITEM NOT FOUND'; exit 5 }
$item.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
$expand.Collapse()
Start-Sleep -Milliseconds 300

$input = Find-ById 'KeyComboInput'
if ($null -eq $input) { Write-Output 'INPUT NOT FOUND'; exit 6 }
$input.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue('Z')
Start-Sleep -Milliseconds 200

$save = Find-ById 'SaveActionButton'
if ($null -eq $save) { Write-Output 'SAVE NOT FOUND'; exit 7 }
$save.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 600
Write-Output 'EDITOR-SAVED'
