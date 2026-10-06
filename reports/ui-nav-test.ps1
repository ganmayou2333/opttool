Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = 'G:\cft\dist\OptTool.exe'
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 10

"After 10s alive: $(-not $p.HasExited) (pid $($p.Id))"

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
"Window found: $($null -ne $win)"
"Window title: $($win.Current.Name)"

$labels = @('体检仪表盘', '清理释放', '优化调整', '游戏环境', '撤销中心', '报告', '设置')

foreach ($label in $labels) {
    $nameCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $label)
    $typeCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $and = New-Object System.Windows.Automation.AndCondition($nameCond, $typeCond)

    $item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $and)

    if ($null -eq $item) {
        "$label : NAV ITEM NOT FOUND"
        continue
    }

    try {
        $pattern = $null
        if ($item.TryGetCurrentPattern(
                [System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) {
            $pattern.Select()
        }
        else {
            $inv = $null
            if ($item.TryGetCurrentPattern(
                    [System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
                $inv.Invoke()
            }
        }

        Start-Sleep -Seconds 3
        "$label : selected, alive=$(-not $p.HasExited)"
    }
    catch {
        "$label : ERROR $($_.Exception.Message)"
    }
}

"Final alive: $(-not $p.HasExited)"

if (-not $p.HasExited) {
    Stop-Process -Id $p.Id -Force
    "Process stopped by test."
}
