Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$reportsDir = 'G:\cft\dist\data\reports'
if (Test-Path $reportsDir) { Remove-Item -Recurse -Force $reportsDir }

$exe = 'G:\cft\dist\OptTool.exe'
$p = Start-Process $exe -PassThru
Start-Sleep -Seconds 8

$root = [System.Windows.Automation.AutomationElement]::RootElement
$pidCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p.Id)
$win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

# 导航到「报告」
$nameCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::NameProperty, '报告')
$typeCond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::ListItem)
$nav = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
    (New-Object System.Windows.Automation.AndCondition($nameCond, $typeCond)))
$sel = $null
$nav.TryGetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern, [ref]$sel) | Out-Null
$sel.Select()
Start-Sleep -Seconds 6

foreach ($btnName in @('导出 HTML', '导出 TXT', '导出 JSON')) {
    $bName = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $btnName)
    $bType = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $btn = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.AndCondition($bName, $bType)))

    $inv = $null
    if ($btn.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$inv)) {
        $inv.Invoke()
    }
    Start-Sleep -Seconds 2
    "$btnName clicked, alive=$(-not $p.HasExited)"
}

Start-Sleep -Seconds 2
"--- files in $reportsDir ---"
Get-ChildItem $reportsDir -File | ForEach-Object {
    "$($_.Name)  $($_.Length) bytes"
}

$html = Get-ChildItem $reportsDir -Filter *.html | Select-Object -First 1
if ($html) {
    $content = Get-Content $html.FullName -Raw -Encoding UTF8
    "HTML has <html>: $($content.Contains('<html'))"
    "HTML has inline style: $($content.Contains('<style>'))"
}

$json = Get-ChildItem $reportsDir -Filter *.json | Select-Object -First 1
if ($json) {
    try {
        $doc = Get-Content $json.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
        "JSON parses, Os = $($doc.Os), Cpu = $($doc.Cpu)"
    } catch {
        "JSON parse failed: $($_.Exception.Message)"
    }
}

Stop-Process -Id $p.Id -Force
"done"
