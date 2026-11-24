Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$settings = Join-Path $env:LOCALAPPDATA 'Fireworm\settings.json'
Copy-Item $settings (Join-Path $env:TEMP 'fireworm-settings-backup2.json') -Force

$proc = Get-Process Fireworm.App -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Output 'NO_WINDOW'; exit 1 }

$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::FromHandle($proc.MainWindowHandle)

function Find-ById($root, $id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

$card = Find-ById $win 'CardKey4'
$card.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Milliseconds 500

$combo = Find-ById $win 'ActionTypeCombo'
$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 700

$listItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItem)
$target = $null
foreach ($item in $items) { if ($item.Current.Name -eq '설정 안 됨') { $target = $item; break } }
if (-not $target) { Write-Output 'NO_NONE_ITEM'; exit 1 }

$target.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Start-Sleep -Milliseconds 900

$json = Get-Content $settings -Raw | ConvertFrom-Json
Write-Output ("KEY4_TYPE=" + $json.Bindings.KEY4.Type)
Write-Output ("KEY4_SUMMARY=" + (Find-ById $win 'CardKey4').Current.Name)

$card2 = Find-ById $win 'CardKey4'
Write-Output ("STATUS=" + (Find-ById $win 'EditorStatus'))
