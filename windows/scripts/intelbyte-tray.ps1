# intelbyte system-tray supervisor.
#
# Shows a shield icon near the clock and starts the hidden background shield.
# Right-click for status, pause/resume, restart, open config/log, and quit.
# Launched by `intelbyte tray` (hidden, no console). Talks to the shield only
# through the same files the CLI uses (status.json + paused.flag), so it stays
# in sync with `intelbyte status` / `intelbyte pause` on the command line.
#
# Windows PowerShell 5.1 (powershell.exe) runs STA by default, which WinForms
# needs for the message loop.
param(
  [string]$NodeExe = "node",
  [string]$BinJs
)

$ErrorActionPreference = 'SilentlyContinue'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$cfgDir     = Join-Path $env:APPDATA 'intelbyte'
$statusFile = Join-Path $cfgDir 'status.json'
$pauseFlag  = Join-Path $cfgDir 'paused.flag'
$logFile    = Join-Path $cfgDir 'shield.log'
if (-not (Test-Path $cfgDir)) { New-Item -ItemType Directory -Force -Path $cfgDir | Out-Null }

function Invoke-Ib([string[]]$cmdArgs) {
  try { & $NodeExe $BinJs @cmdArgs | Out-Null } catch {}
}

# Start the hidden background shield if it isn't already running.
Invoke-Ib @('start')

# ---- tray icon ------------------------------------------------------------
$notify = New-Object System.Windows.Forms.NotifyIcon
$notify.Icon = [System.Drawing.SystemIcons]::Shield
$notify.Text = 'intelbyte — starting…'
$notify.Visible = $true

$menu = New-Object System.Windows.Forms.ContextMenuStrip

$miTitle = $menu.Items.Add('intelbyte shield')
$miTitle.Enabled = $false

$miStatus = $menu.Items.Add('status: …')
$miStatus.Enabled = $false

$menu.Items.Add('-') | Out-Null

$miPause = New-Object System.Windows.Forms.ToolStripMenuItem
$miPause.Text = 'Pause masking'
$miPause.Add_Click({
  if (Test-Path $pauseFlag) {
    Remove-Item -Force $pauseFlag
  } else {
    Set-Content -Path $pauseFlag -Value (Get-Date).ToString('o')
  }
})
$menu.Items.Add($miPause) | Out-Null

$miRestart = New-Object System.Windows.Forms.ToolStripMenuItem
$miRestart.Text = 'Restart shield'
$miRestart.Add_Click({ Invoke-Ib @('restart') })
$menu.Items.Add($miRestart) | Out-Null

$miFolder = New-Object System.Windows.Forms.ToolStripMenuItem
$miFolder.Text = 'Open config folder'
$miFolder.Add_Click({ Start-Process explorer.exe $cfgDir })
$menu.Items.Add($miFolder) | Out-Null

$miLog = New-Object System.Windows.Forms.ToolStripMenuItem
$miLog.Text = 'Open shield log'
$miLog.Add_Click({ if (Test-Path $logFile) { Start-Process notepad.exe $logFile } })
$menu.Items.Add($miLog) | Out-Null

$menu.Items.Add('-') | Out-Null

$miQuit = New-Object System.Windows.Forms.ToolStripMenuItem
$miQuit.Text = 'Quit (stop shield)'
$miQuit.Add_Click({
  Invoke-Ib @('stop')
  $notify.Visible = $false
  $timer.Stop()
  [System.Windows.Forms.Application]::Exit()
})
$menu.Items.Add($miQuit) | Out-Null

$notify.ContextMenuStrip = $menu

# ---- status polling -------------------------------------------------------
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 2500
$timer.Add_Tick({
  $paused = Test-Path $pauseFlag
  $miPause.Text = if ($paused) { 'Resume masking' } else { 'Pause masking' }

  $running = $false
  $conn = 0
  $apps = '?'
  if (Test-Path $statusFile) {
    try {
      $st = Get-Content $statusFile -Raw | ConvertFrom-Json
      $running = [bool]$st.running
      if ($st.connected) { $conn = @($st.connected).Count }
      if ($null -ne $st.apps) { $apps = $st.apps }
    } catch {}
  }

  $state = if (-not $running) { 'stopped' } elseif ($paused) { 'paused' } else { "on · $conn app(s) masked" }
  $miStatus.Text = "status: $state"
  $tip = "intelbyte — $state (wired: $apps)"
  if ($tip.Length -gt 63) { $tip = $tip.Substring(0, 63) }  # NotifyIcon tooltip cap
  $notify.Text = $tip
})
$timer.Start()

# Double-click the tray icon → status balloon.
$notify.Add_MouseDoubleClick({
  $notify.BalloonTipTitle = 'intelbyte'
  $notify.BalloonTipText = $miStatus.Text
  $notify.ShowBalloonTip(3000)
})

[System.Windows.Forms.Application]::Run()
