param([string]$Root = "", [string]$Exe = "")
if (-not $Root) { $Root = Split-Path $PSScriptRoot -Parent }
$ErrorActionPreference = "Stop"
Start-Transcript -Path (Join-Path $Root "build/av-portable.log") -Force | Out-Null
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class AvUiP {
  [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint f);
}
"@
[AvUiP]::SetProcessDpiAwarenessContext([IntPtr](-4)) | Out-Null
[AvUiP]::SetThreadExecutionState([uint32]2147483651) | Out-Null
if (Get-Process LogonUI -ErrorAction SilentlyContinue) { throw "workstation locked - unlock before running" }

if (-not $Exe) { $Exe = Join-Path $Root "src\YgoMDStartingHandRecorder\bin\Release\net11.0-windows10.0.19041.0\win-x64\publish\YgoMDStartingHandRecorder.exe" }
if (-not (Test-Path $Exe)) { throw "published exe missing: $Exe" }
$leaf = "ygo-av-portable-" + [System.Guid]::NewGuid().ToString("N").Substring(0, 8)
$tmp = Join-Path $env:TEMP $leaf
New-Item -ItemType Directory -Path $tmp | Out-Null
Copy-Item $Exe (Join-Path $tmp "YgoMDStartingHandRecorder.exe")
"portable dir: $tmp"

$sample = Get-ChildItem (Join-Path $Root "samples\own") -Filter "*005109*" | Select-Object -First 1
if (-not $sample) { throw "smoke sample missing" }
$img = [System.Drawing.Image]::FromFile($sample.FullName)
$wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$aspect = $img.Width / $img.Height
$w = $wa.Width; $h = [int]($w / $aspect)
if ($h -gt $wa.Height) { $h = $wa.Height; $w = [int]($h * $aspect) }
$viewer = New-Object System.Windows.Forms.Form
$viewer.Text = "YGO smoke viewer"
$viewer.ClientSize = New-Object System.Drawing.Size $w, $h
$viewer.StartPosition = "Manual"
$viewer.Location = New-Object System.Drawing.Point $wa.X, $wa.Y
$pb = New-Object System.Windows.Forms.PictureBox
$pb.Dock = "Fill"; $pb.SizeMode = "StretchImage"; $pb.Image = $img
$viewer.Controls.Add($pb)
$viewer.Show()
[System.Windows.Forms.Application]::DoEvents()

function Pump([int]$ms) {
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $ms) { [System.Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 60 }
}
function BtnV($win, [string]$name) {
    $bc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    foreach ($b in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $bc)) {
        if ($b.Current.Name -eq $name -and $b.Current.BoundingRectangle.Width -gt 0) { return $b }
    }
    return $null
}
function Wait-Text($win, [string]$pattern, [int]$timeoutMs) {
    $tc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        try {
            foreach ($t in $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $tc)) {
                if ($t.Current.Name -like $pattern) { return $t }
            }
        } catch { }
        Pump 200
    }
    return $null
}
function ListItems($win) {
    $lc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem)
    return $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $lc)
}

# no args + working dir outside repo => portable root = exe dir, embedded card table
$env:YGO_AUTOSAVE = Join-Path $tmp "captures"
$env:YGO_CARD_DATA = Join-Path $tmp "carddata"
$p = Start-Process -FilePath (Join-Path $tmp "YgoMDStartingHandRecorder.exe") -WorkingDirectory $tmp -PassThru
trap { "TRAP: $_" | Out-Host; if ($p) { Stop-Process -Id $p.Id -ErrorAction SilentlyContinue }; if ($viewer) { $viewer.Close() }; [AvUiP]::SetThreadExecutionState([uint32]2147483648) | Out-Null; break }

$auto = [System.Windows.Automation.AutomationElement]::RootElement
$wc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window)
$win = $null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 60000 -and -not $win) {
    try { foreach ($x in $auto.FindAll([System.Windows.Automation.TreeScope]::Children, $wc)) { if ($x.Current.Name -eq "YGO 起手记录" -and $x.Current.ProcessId -eq $p.Id) { $win = $x; break } } } catch { }
    if (-not $win) { Pump 300 }
}
if (-not $win) { throw "portable window not found (single-file extract?)" }
"portable window up"
# P12: card table no longer embedded → startup prompt → download with progress → storage meta
$prompt = Wait-Text $win "缺少卡片数据" 15000
if (-not $prompt) { throw "missing-card prompt not shown" }
$dl = BtnV $win "下载"
if (-not $dl) { throw "prompt download button missing" }
$dl.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$upd = Wait-Text $win "卡表已更新*" 180000
if (-not $upd) { throw "card download not finished" }
"card data downloaded: $($upd.Current.Name)"
$today = Get-Date -Format "yyyy-MM-dd"
$meta = Wait-Text $win "$today *" 8000
if (-not $meta) { throw "storage update time not recorded" }
"storage update time: $($meta.Current.Name)"
if (-not (Test-Path (Join-Path $tmp "carddata\cards.json"))) { throw "cards.json not under portable data dir" }
if (-not (Test-Path (Join-Path $tmp "carddata\cards.meta.json"))) { throw "cards meta not written" }
(BtnV $win "捕获").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Pump 400
$ready = Wait-Text $win "就绪*" 120000
if (-not $ready) { throw "model not ready after download in 120s" }
"card table ready after download"

$b = BtnV $win "窗口选择"
if (-not $b) { throw "window select missing" }
$b.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Pump 400
$found = $null
foreach ($it in (ListItems $win)) { if ($it.Current.Name -eq "YGO smoke viewer") { $found = $it; break } }
if (-not $found) { throw "viewer not in window list" }
$found.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
Pump 400
$cap = $null
$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($sw.ElapsedMilliseconds -lt 10000 -and -not $cap) {
    $cb = BtnV $win "先手截图"
    if ($cb -and $cb.Current.IsEnabled) { $cap = $cb } else { Pump 200 }
}
if (-not $cap) { throw "capture button not enabled" }
$cap.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$done = Wait-Text $win "已保存并入库*" 60000
if (-not $done) { throw "capture not saved" }
"capture: $($done.Current.Name)"
$path = Wait-Text $win "*$leaf*" 8000
if (-not $path) { throw "capture path not under portable dir" }
"portable root ok: $($path.Current.Name)"

$db = Join-Path $tmp "records\ygo.db"
if (-not (Test-Path $db)) { throw "portable db not created at exe dir" }
"portable db: $db"
$recExe = Join-Path $Root "tests\Ygo.Records.Smoke\bin\Debug\net11.0-windows10.0.19041.0\Ygo.Records.Smoke.exe"
$v = & $recExe verify $db
"db: $v"
if ($LASTEXITCODE -ne 0) { throw "db verify failed" }

$rb = BtnV $win "记录"
$rb.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
$n = Wait-Text $win "共 1 条记录*" 8000
if (-not $n) { throw "records row missing in portable mode" }
"portable records: 1 row"
# P9: import/export record-set buttons present in release too (no dev/release split)
if (-not (BtnV $win "导入记录集")) { throw "release build should keep 导入记录集" }
if (-not (BtnV $win "导出记录集")) { throw "release build should keep 导出记录集" }
"release toolbar ok (import + export present)"

Stop-Process -Id $p.Id -ErrorAction SilentlyContinue
Pump 800
$viewer.Close()
$img.Dispose()
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
[AvUiP]::SetThreadExecutionState([uint32]2147483648) | Out-Null
"AV PORTABLE SMOKE PASS"