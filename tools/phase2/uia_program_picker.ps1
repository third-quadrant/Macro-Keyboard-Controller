Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$settings = Join-Path $env:LOCALAPPDATA 'Fireworm\settings.json'
$backup = Join-Path $env:TEMP 'fireworm-settings-backup.json'
Copy-Item $settings $backup -Force

$proc = Get-Process Fireworm.App -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $proc) { Write-Output 'NO_WINDOW'; exit 1 }

$AE = [System.Windows.Automation.AutomationElement]
$win = $AE::FromHandle($proc.MainWindowHandle)

function Find-ById($root, $id) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

$combo = Find-ById $win 'InstalledProgramCombo'
if (-not $combo) { Write-Output 'NO_PROGRAM_COMBO'; exit 1 }

$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Start-Sleep -Milliseconds 1200

$listItem = New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
$items = $combo.FindAll([System.Windows.Automation.TreeScope]::Descendants, $listItem)
Write-Output ("PROGRAM_COUNT=" + $items.Count)

$names = @()
for ($i = 0; $i -lt [Math]::Min(5, $items.Count); $i++) { $names += $items[$i].Current.Name }
Write-Output ("FIRST=" + ($names -join ' | '))

if ($items.Count -gt 0) {
    $items[0].GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 700

    $pathBox = Find-ById $win 'LaunchPathBox'
    $value = $pathBox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    Write-Output ("PATH_BOX=" + $value)

    $json = Get-Content $settings -Raw | ConvertFrom-Json
    $key1 = $json.Bindings | Where-Object { $_.Key -eq 'KEY1' }
    Write-Output ("SETTINGS_KEY1_TYPE=" + $key1.Action.Type)
    Write-Output ("SETTINGS_KEY1_PATH=" + $key1.Action.Path)
}

$combo.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Collapse()
