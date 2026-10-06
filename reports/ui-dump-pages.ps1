Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$exe = 'G:\cft\dist\OptTool.exe'
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 8

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

function Nav-To($tag) {
    $n = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $tag)
    $t = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::ListItem)
    $item = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.AndCondition($n, $t)))
    $s = $null
    $item.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$s) | Out-Null
    $s.Select()
}

$tText = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Text)

foreach ($page in @('清理释放', '优化调整', '撤销中心', '设置')) {
    Nav-To $page
    Start-Sleep -Seconds 7
    "==================== $page ==================== "
    $texts = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tText)
    foreach ($x in $texts) {
        $v = $x.Current.Name
        if (-not [string]::IsNullOrWhiteSpace($v)) { "  $v" }
    }
}

Stop-Process -Id $p.Id -Force
