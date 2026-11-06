[CmdletBinding()]
param(
    [switch]$SelfTest,
    [switch]$ScanOnly,
    [string]$ScreenshotPath
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$serverPath = Join-Path $root "runtime\bin\llama-server.exe"
$modelsDir = Join-Path $root "models"
$logDir = Join-Path $root "logs"

function Get-ModelCatalog {
    New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null
    $files = Get-ChildItem -LiteralPath $modelsDir -File -Filter "*.gguf" -ErrorAction SilentlyContinue |
        Sort-Object Name

    $main = @()
    $mmproj = @()
    $draft = @()

    foreach ($file in $files) {
        $name = $file.Name.ToLowerInvariant()
        $item = [pscustomobject]@{
            Name = $file.Name
            BaseName = $file.BaseName
            FullName = $file.FullName
            SizeGB = [math]::Round($file.Length / 1GB, 2)
        }

        if ($name -match "^mmproj|mmproj") {
            $mmproj += $item
        }
        elseif ($name -match "^mtp[-_.]|^draft[-_.]") {
            $draft += $item
        }
        else {
            $main += $item
        }
    }

    return [pscustomobject]@{
        Main = @($main)
        Mmproj = @($mmproj)
        Draft = @($draft)
    }
}

function Get-MatchScore {
    param(
        [string]$MainName,
        [string]$CandidateName
    )

    $mainText = $MainName.ToLowerInvariant()
    $candidateText = $CandidateName.ToLowerInvariant()
    $score = 0

    foreach ($family in @("gemma", "qwen", "llama", "deepseek", "mistral", "glm", "minicpm", "lfm")) {
        if ($mainText.Contains($family) -and $candidateText.Contains($family)) {
            $score += 100
        }
    }

    if ($mainText.Contains("gemma") -and $candidateText -match "^mmproj-f16$") {
        $score += 80
    }

    $stop = @(
        "gguf", "model", "models", "mmproj", "mtp", "draft",
        "f16", "f32", "bf16", "q2", "q3", "q4", "q5", "q6",
        "q8", "iq1", "iq2", "iq3", "iq4", "k", "xl", "xxs",
        "xs", "s", "m", "l", "ud", "it", "instruct", "chat"
    )

    $mainTokens = @(
        [regex]::Matches($mainText, "[a-z0-9]+") |
            ForEach-Object { $_.Value } |
            Where-Object { $_.Length -ge 3 -and $_ -notin $stop }
    )

    foreach ($token in $mainTokens) {
        if ($candidateText.Contains($token)) {
            $score += 10
        }
    }

    return $score
}

function Find-MatchingAuxiliary {
    param(
        [object]$Main,
        [object[]]$Candidates
    )

    if (-not $Main -or -not $Candidates -or $Candidates.Count -eq 0) {
        return $null
    }

    $ranked = $Candidates |
        ForEach-Object {
            [pscustomobject]@{
                Item = $_
                Score = Get-MatchScore -MainName $Main.BaseName -CandidateName $_.BaseName
            }
        } |
        Sort-Object Score -Descending

    $best = $ranked | Select-Object -First 1
    if ($best -and $best.Score -gt 0) {
        return $best.Item
    }

    return $null
}

function Get-FriendlyModelName {
    param([object]$Model)

    $name = $Model.BaseName
    if ($name -match "(?i)gemma.*26B") {
        return "Gemma 4 - 26B A4B"
    }
    if ($name -match "(?i)qwen.*27B") {
        return "Qwen 3.8 - 27B"
    }
    if ($name.Length -gt 30) {
        return $name.Substring(0, 27) + "..."
    }
    return $name
}

$catalog = Get-ModelCatalog

if ($ScanOnly) {
    $catalog |
        ConvertTo-Json -Depth 5
    return
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
try {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class LlamaDpiAwareness {
    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
"@
    [void][LlamaDpiAwareness]::SetProcessDPIAware()
}
catch {
}
[System.Windows.Forms.Application]::EnableVisualStyles()

$script:serverProcess = $null
$script:stdoutPath = $null
$script:stderrPath = $null
$script:lastStdoutLength = 0
$script:lastStderrLength = 0
$script:startedAt = $null
$script:suspendPresetChange = $false

$form = New-Object System.Windows.Forms.Form
$form.Text = "Llama V100 + RTX 5070 模型组合控制台"
$form.StartPosition = "CenterScreen"
$workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
$initialWidth = [Math]::Max(940, [Math]::Min(1180, $workArea.Width - 50))
$initialHeight = [Math]::Max(650, [Math]::Min(800, $workArea.Height - 50))
$form.ClientSize = New-Object System.Drawing.Size($initialWidth, $initialHeight)
$form.MinimumSize = New-Object System.Drawing.Size(940, 650)
$form.BackColor = [System.Drawing.Color]::FromArgb(245, 247, 250)
$form.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9)
$form.AutoScaleMode = [System.Windows.Forms.AutoScaleMode]::None

$header = New-Object System.Windows.Forms.Panel
$header.Dock = "Top"
$header.Height = 72
$header.BackColor = [System.Drawing.Color]::FromArgb(28, 38, 49)
$form.Controls.Add($header)

$title = New-Object System.Windows.Forms.Label
$title.Text = "Llama Dual GPU"
$title.ForeColor = [System.Drawing.Color]::White
$title.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 18, [System.Drawing.FontStyle]::Bold)
$title.AutoSize = $true
$title.Location = New-Object System.Drawing.Point(24, 13)
$header.Controls.Add($title)

$subtitle = New-Object System.Windows.Forms.Label
$subtitle.Text = "主模型 + 视觉投影器 + MTP 草稿模型"
$subtitle.ForeColor = [System.Drawing.Color]::FromArgb(152, 168, 184)
$subtitle.AutoSize = $true
$subtitle.Location = New-Object System.Drawing.Point(300, 28)
$header.Controls.Add($subtitle)

$headerBadge = New-Object System.Windows.Forms.Label
$headerBadge.Text = "v0.8"
$headerBadge.ForeColor = [System.Drawing.Color]::FromArgb(58, 220, 180)
$headerBadge.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9, [System.Drawing.FontStyle]::Bold)
$headerBadge.TextAlign = [System.Drawing.ContentAlignment]::MiddleRight
$headerBadge.Size = New-Object System.Drawing.Size(80, 30)
$headerBadge.Dock = "Right"
$header.Controls.Add($headerBadge)

$sidebar = New-Object System.Windows.Forms.Panel
$sidebar.Left = 0
$sidebar.Top = 72
$sidebar.Width = 230
$sidebar.Height = 728
$sidebar.Anchor = "Top,Bottom,Left"
$sidebar.BackColor = [System.Drawing.Color]::FromArgb(35, 47, 60)
$form.Controls.Add($sidebar)

$presetTitle = New-Object System.Windows.Forms.Label
$presetTitle.Text = "模型预设"
$presetTitle.ForeColor = [System.Drawing.Color]::White
$presetTitle.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 11, [System.Drawing.FontStyle]::Bold)
$presetTitle.Location = New-Object System.Drawing.Point(18, 20)
$presetTitle.Size = New-Object System.Drawing.Size(190, 30)
$sidebar.Controls.Add($presetTitle)

$presetPanel = New-Object System.Windows.Forms.FlowLayoutPanel
$presetPanel.Left = 14
$presetPanel.Top = 56
$presetPanel.Width = 202
$presetPanel.Height = 330
$presetPanel.FlowDirection = "TopDown"
$presetPanel.WrapContents = $false
$presetPanel.AutoScroll = $true
$sidebar.Controls.Add($presetPanel)
$modelToolTip = New-Object System.Windows.Forms.ToolTip

$refreshModelsButton = New-Object System.Windows.Forms.Button
$refreshModelsButton.Text = "刷新模型"
$refreshModelsButton.Left = 18
$refreshModelsButton.Top = 402
$refreshModelsButton.Width = 194
$refreshModelsButton.Height = 36
$refreshModelsButton.FlatStyle = "Flat"
$refreshModelsButton.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(72, 89, 106)
$refreshModelsButton.BackColor = [System.Drawing.Color]::FromArgb(44, 58, 72)
$refreshModelsButton.ForeColor = [System.Drawing.Color]::White
$sidebar.Controls.Add($refreshModelsButton)

$openModelsButton = New-Object System.Windows.Forms.Button
$openModelsButton.Text = "打开模型目录"
$openModelsButton.Left = 18
$openModelsButton.Top = 446
$openModelsButton.Width = 194
$openModelsButton.Height = 36
$openModelsButton.FlatStyle = "Flat"
$openModelsButton.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(72, 89, 106)
$openModelsButton.BackColor = [System.Drawing.Color]::FromArgb(44, 58, 72)
$openModelsButton.ForeColor = [System.Drawing.Color]::White
$sidebar.Controls.Add($openModelsButton)

$sidebarStatus = New-Object System.Windows.Forms.Label
$sidebarStatus.Text = "未启动"
$sidebarStatus.ForeColor = [System.Drawing.Color]::FromArgb(160, 180, 196)
$sidebarStatus.Left = 18
$sidebarStatus.Top = 510
$sidebarStatus.Width = 194
$sidebarStatus.Height = 150
$sidebarStatus.Anchor = "Bottom,Left"
$sidebar.Controls.Add($sidebarStatus)

$content = New-Object System.Windows.Forms.Panel
$content.Left = 250
$content.Top = 92
$content.Width = 900
$content.Height = 688
$content.Anchor = "Top,Bottom,Left,Right"
$content.AutoScroll = $true
$form.Controls.Add($content)

$modelGroup = New-Object System.Windows.Forms.GroupBox
$modelGroup.Text = "模型组合"
$modelGroup.Left = 0
$modelGroup.Top = 0
$modelGroup.Width = 900
$modelGroup.Height = 220
$modelGroup.Anchor = "Top,Left,Right"
$modelGroup.BackColor = [System.Drawing.Color]::White
$content.Controls.Add($modelGroup)

function Add-FieldLabel {
    param(
        [System.Windows.Forms.Control]$Parent,
        [string]$Text,
        [int]$X,
        [int]$Y,
        [int]$Width = 96
    )

    $label = New-Object System.Windows.Forms.Label
    $label.Text = $Text
    $label.Location = New-Object System.Drawing.Point($X, $Y)
    $label.Size = New-Object System.Drawing.Size($Width, 25)
    $label.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
    $label.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9, [System.Drawing.FontStyle]::Bold)
    $Parent.Controls.Add($label)
    return $label
}

Add-FieldLabel -Parent $modelGroup -Text "主模型" -X 20 -Y 30 -Width 100 | Out-Null

$mainCombo = New-Object System.Windows.Forms.ComboBox
$mainCombo.Left = 126
$mainCombo.Top = 27
$mainCombo.Width = 650
$mainCombo.Height = 30
$mainCombo.DropDownStyle = "DropDownList"
$mainCombo.DisplayMember = "Name"
$mainCombo.Anchor = "Top,Left,Right"
$modelGroup.Controls.Add($mainCombo)

$mainInfo = New-Object System.Windows.Forms.Label
$mainInfo.Left = 126
$mainInfo.Top = 57
$mainInfo.Width = 720
$mainInfo.Height = 24
$mainInfo.ForeColor = [System.Drawing.Color]::FromArgb(92, 104, 116)
$modelGroup.Controls.Add($mainInfo)

Add-FieldLabel -Parent $modelGroup -Text "视觉投影" -X 20 -Y 96 -Width 100 | Out-Null

$mmprojCombo = New-Object System.Windows.Forms.ComboBox
$mmprojCombo.Left = 126
$mmprojCombo.Top = 93
$mmprojCombo.Width = 430
$mmprojCombo.DropDownStyle = "DropDownList"
$mmprojCombo.DisplayMember = "Name"
$modelGroup.Controls.Add($mmprojCombo)

$mmprojOffload = New-Object System.Windows.Forms.CheckBox
$mmprojOffload.Text = "视觉投影器放入 GPU"
$mmprojOffload.Checked = $true
$mmprojOffload.Left = 574
$mmprojOffload.Top = 93
$mmprojOffload.Width = 210
$modelGroup.Controls.Add($mmprojOffload)

Add-FieldLabel -Parent $modelGroup -Text "MTP" -X 20 -Y 138 -Width 100 | Out-Null

$draftCombo = New-Object System.Windows.Forms.ComboBox
$draftCombo.Left = 126
$draftCombo.Top = 135
$draftCombo.Width = 430
$draftCombo.DropDownStyle = "DropDownList"
$draftCombo.DisplayMember = "Name"
$modelGroup.Controls.Add($draftCombo)

$draftHint = New-Object System.Windows.Forms.Label
$draftHint.Left = 574
$draftHint.Top = 135
$draftHint.Width = 290
$draftHint.Height = 25
$draftHint.Text = "MTP 使用 --spec-type draft-mtp"
$draftHint.ForeColor = [System.Drawing.Color]::FromArgb(92, 104, 116)
$modelGroup.Controls.Add($draftHint)

$embeddedMtp = New-Object System.Windows.Forms.CheckBox
$embeddedMtp.Text = "主模型内置 MTP"
$embeddedMtp.Left = 574
$embeddedMtp.Top = 164
$embeddedMtp.Width = 230
$embeddedMtp.Height = 25
$modelGroup.Controls.Add($embeddedMtp)

$totalInfo = New-Object System.Windows.Forms.Label
$totalInfo.Left = 126
$totalInfo.Top = 188
$totalInfo.Width = 720
$totalInfo.Height = 25
$totalInfo.Text = "组合显存估算：0 GB"
$totalInfo.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9, [System.Drawing.FontStyle]::Bold)
$totalInfo.ForeColor = [System.Drawing.Color]::FromArgb(34, 126, 104)
$modelGroup.Controls.Add($totalInfo)

$computeGroup = New-Object System.Windows.Forms.GroupBox
$computeGroup.Text = "计算与显存"
$computeGroup.Left = 0
$computeGroup.Top = 232
$computeGroup.Width = 900
$computeGroup.Height = 150
$computeGroup.Anchor = "Top,Left,Right"
$computeGroup.BackColor = [System.Drawing.Color]::White
$content.Controls.Add($computeGroup)

Add-FieldLabel -Parent $computeGroup -Text "GPU 顺序" -X 20 -Y 28 -Width 76 | Out-Null

$gpuOrder = New-Object System.Windows.Forms.ComboBox
$gpuOrder.Left = 100
$gpuOrder.Top = 25
$gpuOrder.Width = 190
$gpuOrder.DropDownStyle = "DropDownList"
[void]$gpuOrder.Items.Add("V100 优先 (0,1)")
[void]$gpuOrder.Items.Add("5070 优先 (1,0)")
$gpuOrder.SelectedIndex = 0
$computeGroup.Controls.Add($gpuOrder)

Add-FieldLabel -Parent $computeGroup -Text "V100%" -X 320 -Y 28 -Width 80 | Out-Null

$splitTrack = New-Object System.Windows.Forms.TrackBar
$splitTrack.Left = 400
$splitTrack.Top = 17
$splitTrack.Width = 300
$splitTrack.Minimum = 50
$splitTrack.Maximum = 85
$splitTrack.Value = 68
$splitTrack.TickFrequency = 5
$computeGroup.Controls.Add($splitTrack)

$splitValue = New-Object System.Windows.Forms.Label
$splitValue.Left = 708
$splitValue.Top = 24
$splitValue.Width = 150
$splitValue.Height = 28
$splitValue.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 10, [System.Drawing.FontStyle]::Bold)
$splitValue.ForeColor = [System.Drawing.Color]::FromArgb(25, 108, 175)
$computeGroup.Controls.Add($splitValue)

Add-FieldLabel -Parent $computeGroup -Text "上下文" -X 20 -Y 87 -Width 76 | Out-Null

$contextCombo = New-Object System.Windows.Forms.ComboBox
$contextCombo.Left = 100
$contextCombo.Top = 84
$contextCombo.Width = 130
$contextCombo.DropDownStyle = "DropDownList"
[void]$contextCombo.Items.Add("4096")
[void]$contextCombo.Items.Add("8192")
[void]$contextCombo.Items.Add("16384")
[void]$contextCombo.Items.Add("32768")
$contextCombo.SelectedIndex = 1
$computeGroup.Controls.Add($contextCombo)

Add-FieldLabel -Parent $computeGroup -Text "端口" -X 258 -Y 87 -Width 50 | Out-Null

$portBox = New-Object System.Windows.Forms.NumericUpDown
$portBox.Left = 312
$portBox.Top = 84
$portBox.Width = 90
$portBox.Minimum = 1024
$portBox.Maximum = 65535
$portBox.Value = 8080
$computeGroup.Controls.Add($portBox)

$openAfterStart = New-Object System.Windows.Forms.CheckBox
$openAfterStart.Text = "启动后打开 WebUI"
$openAfterStart.Checked = $true
$openAfterStart.Left = 430
$openAfterStart.Top = 84
$openAfterStart.Width = 240
$computeGroup.Controls.Add($openAfterStart)

$commandPreview = New-Object System.Windows.Forms.TextBox
$commandPreview.Left = 0
$commandPreview.Top = 394
$commandPreview.Width = 900
$commandPreview.Height = 58
$commandPreview.Multiline = $true
$commandPreview.ReadOnly = $true
$commandPreview.ScrollBars = "Horizontal"
$commandPreview.BackColor = [System.Drawing.Color]::FromArgb(238, 242, 246)
$commandPreview.Font = New-Object System.Drawing.Font("Cascadia Mono", 8)
$commandPreview.Anchor = "Top,Left,Right"
$content.Controls.Add($commandPreview)

$startButton = New-Object System.Windows.Forms.Button
$startButton.Text = "启动组合"
$startButton.Left = 0
$startButton.Top = 462
$startButton.Width = 130
$startButton.Height = 38
$startButton.BackColor = [System.Drawing.Color]::FromArgb(20, 123, 100)
$startButton.ForeColor = [System.Drawing.Color]::White
$startButton.FlatStyle = "Flat"
$startButton.FlatAppearance.BorderSize = 0
$content.Controls.Add($startButton)

$stopButton = New-Object System.Windows.Forms.Button
$stopButton.Text = "停止"
$stopButton.Left = 140
$stopButton.Top = 462
$stopButton.Width = 100
$stopButton.Height = 38
$stopButton.Enabled = $false
$stopButton.FlatStyle = "Flat"
$content.Controls.Add($stopButton)

$devicesButton = New-Object System.Windows.Forms.Button
$devicesButton.Text = "检测设备"
$devicesButton.Left = 250
$devicesButton.Top = 462
$devicesButton.Width = 120
$devicesButton.Height = 38
$devicesButton.FlatStyle = "Flat"
$content.Controls.Add($devicesButton)

$webButton = New-Object System.Windows.Forms.Button
$webButton.Text = "打开 WebUI"
$webButton.Left = 380
$webButton.Top = 462
$webButton.Width = 120
$webButton.Height = 38
$webButton.FlatStyle = "Flat"
$content.Controls.Add($webButton)

$logsButton = New-Object System.Windows.Forms.Button
$logsButton.Text = "打开日志目录"
$logsButton.Left = 510
$logsButton.Top = 462
$logsButton.Width = 130
$logsButton.Height = 38
$logsButton.FlatStyle = "Flat"
$content.Controls.Add($logsButton)

$statusLabel = New-Object System.Windows.Forms.Label
$statusLabel.Text = "未启动"
$statusLabel.Left = 0
$statusLabel.Top = 508
$statusLabel.Width = 900
$statusLabel.Height = 24
$statusLabel.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", 9, [System.Drawing.FontStyle]::Bold)
$statusLabel.ForeColor = [System.Drawing.Color]::FromArgb(60, 72, 84)
$content.Controls.Add($statusLabel)

$logBox = New-Object System.Windows.Forms.RichTextBox
$logBox.Left = 0
$logBox.Top = 537
$logBox.Width = 900
$logBox.Height = 151
$logBox.Anchor = "Top,Bottom,Left,Right"
$logBox.ReadOnly = $true
$logBox.BackColor = [System.Drawing.Color]::FromArgb(23, 29, 36)
$logBox.ForeColor = [System.Drawing.Color]::FromArgb(216, 225, 232)
$logBox.Font = New-Object System.Drawing.Font("Cascadia Mono", 8)
$content.Controls.Add($logBox)

$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 500

function Quote-Argument {
    param([string]$Value)
    return '"' + $Value.Replace('"', '\"') + '"'
}

function Get-SelectedItem {
    param([System.Windows.Forms.ComboBox]$Combo)
    if ($null -eq $Combo.SelectedItem) {
        return $null
    }
    if ($Combo.SelectedItem.PSObject.Properties.Name -contains "FullName") {
        if ($Combo.SelectedItem.FullName) {
            return $Combo.SelectedItem
        }
    }
    return $null
}

function Set-ComboItems {
    param(
        [System.Windows.Forms.ComboBox]$Combo,
        [object[]]$Items,
        [switch]$AllowNone
    )

    $Combo.BeginUpdate()
    try {
        $Combo.Items.Clear()
        if ($AllowNone) {
            [void]$Combo.Items.Add([pscustomobject]@{
                Name = "不使用"
                BaseName = ""
                FullName = ""
                SizeGB = 0
            })
        }
        foreach ($item in $Items) {
            [void]$Combo.Items.Add($item)
        }
        if ($Combo.Items.Count -gt 0) {
            $Combo.SelectedIndex = 0
        }
    }
    finally {
        $Combo.EndUpdate()
    }
}

function Find-ComboItem {
    param(
        [System.Windows.Forms.ComboBox]$Combo,
        [object]$Item
    )

    if (-not $Item) {
        return
    }
    for ($i = 0; $i -lt $Combo.Items.Count; $i++) {
        $candidate = $Combo.Items[$i]
        if ($candidate.FullName -eq $Item.FullName) {
            $Combo.SelectedIndex = $i
            return
        }
    }
}

function Update-CommandPreview {
    $main = Get-SelectedItem $mainCombo
    if (-not $main) {
        $commandPreview.Text = "请选择主模型"
        $totalInfo.Text = "组合显存估算：0 GB"
        return
    }

    $visibleDevices = if ($gpuOrder.SelectedIndex -eq 0) { "0,1" } else { "1,0" }
    $v100 = [int]$splitTrack.Value
    $rtx = 100 - $v100
    $parts = @(
        "CUDA_VISIBLE_DEVICES=$visibleDevices",
        "`"$serverPath`"",
        "-m", (Quote-Argument $main.FullName),
        "--host", "127.0.0.1",
        "--port", "$($portBox.Value)",
        "-ngl", "all",
        "-c", "$($contextCombo.SelectedItem)",
        "--split-mode", "layer",
        "--tensor-split", "$v100,$rtx",
        "--main-gpu", "0",
        "--flash-attn", "off",
        "--parallel", "1",
        "--metrics",
        "-v"
    )

    $mmproj = Get-SelectedItem $mmprojCombo
    $draft = Get-SelectedItem $draftCombo
    $total = [double]$main.SizeGB

    if ($mmproj) {
        $parts += "--mmproj"
        $parts += (Quote-Argument $mmproj.FullName)
        if ($mmprojOffload.Checked) {
            $parts += "--mmproj-offload"
        }
        else {
            $parts += "--no-mmproj-offload"
        }
        $total += [double]$mmproj.SizeGB
    }

    if ($draft) {
        $parts += "--spec-draft-model"
        $parts += (Quote-Argument $draft.FullName)
        $parts += "--spec-type"
        $parts += "draft-mtp"
        $parts += "--spec-draft-ngl"
        $parts += "all"
        $total += [double]$draft.SizeGB
    }
    elseif ($embeddedMtp.Checked) {
        $parts += "--spec-type"
        $parts += "draft-mtp"
        $parts += "--spec-draft-ngl"
        $parts += "all"
    }

    $commandPreview.Text = $parts -join " "
    $totalInfo.Text = "组合 GGUF 文件大小：$([math]::Round($total, 2)) GB"
    $mainInfo.Text = "$($main.Name)  |  $($main.SizeGB) GB"
}

function Select-ModelProfile {
    param([object]$Main)

    if (-not $Main) {
        return
    }

    $script:suspendPresetChange = $true
    try {
        Find-ComboItem -Combo $mainCombo -Item $Main
        $mm = Find-MatchingAuxiliary -Main $Main -Candidates $catalog.Mmproj
        $dr = Find-MatchingAuxiliary -Main $Main -Candidates $catalog.Draft
        if ($mm) {
            Find-ComboItem -Combo $mmprojCombo -Item $mm
        }
        else {
            $mmprojCombo.SelectedIndex = 0
        }
        if ($dr) {
            Find-ComboItem -Combo $draftCombo -Item $dr
            $embeddedMtp.Checked = $false
        }
        else {
            $draftCombo.SelectedIndex = 0
            $embeddedMtp.Checked = ($Main.BaseName -match "(?i)mtp")
        }
    }
    finally {
        $script:suspendPresetChange = $false
    }
    Update-CommandPreview
}

function Refresh-Models {
    $script:catalog = Get-ModelCatalog

    Set-ComboItems -Combo $mainCombo -Items $script:catalog.Main
    Set-ComboItems -Combo $mmprojCombo -Items $script:catalog.Mmproj -AllowNone
    Set-ComboItems -Combo $draftCombo -Items $script:catalog.Draft -AllowNone

    $presetPanel.Controls.Clear()
    foreach ($main in $script:catalog.Main) {
        $button = New-Object System.Windows.Forms.Button
        $button.Text = "$(Get-FriendlyModelName $main)`r`n$($main.SizeGB) GB"
        $button.Width = 188
        $button.Height = 70
        $button.Margin = New-Object System.Windows.Forms.Padding(3, 3, 3, 8)
        $button.TextAlign = [System.Drawing.ContentAlignment]::MiddleLeft
        $button.FlatStyle = "Flat"
        $button.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(67, 86, 103)
        $button.BackColor = [System.Drawing.Color]::FromArgb(44, 58, 72)
        $button.ForeColor = [System.Drawing.Color]::FromArgb(226, 233, 239)
        $button.Tag = $main
        $modelToolTip.SetToolTip($button, $main.Name)
        $button.Add_Click({
            param($sender, $event)
            Select-ModelProfile -Main $sender.Tag
        })
        $presetPanel.Controls.Add($button)
    }

    if ($script:catalog.Main.Count -gt 0) {
        Select-ModelProfile -Main $script:catalog.Main[0]
    }
    else {
        $commandPreview.Text = "models 目录中没有主模型 GGUF"
        $totalInfo.Text = "组合 GGUF 文件大小：0 GB"
    }
}

function Append-Log {
    param([string]$Text)
    if ([string]::IsNullOrEmpty($Text)) {
        return
    }
    $logBox.AppendText($Text)
    if ($logBox.TextLength -gt 200000) {
        $logBox.Select(0, 50000)
        $logBox.SelectedText = ""
    }
    $logBox.SelectionStart = $logBox.TextLength
    $logBox.ScrollToCaret()
}

function Read-NewLog {
    param([string]$Path, [ref]$LastLength)
    if (-not $Path -or -not (Test-Path -LiteralPath $Path)) {
        return ""
    }
    try {
        $stream = [System.IO.File]::Open(
            $Path,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite
        )
        try {
            if ($stream.Length -le $LastLength.Value) {
                return ""
            }
            [void]$stream.Seek($LastLength.Value, [System.IO.SeekOrigin]::Begin)
            $reader = New-Object System.IO.StreamReader($stream)
            $text = $reader.ReadToEnd()
            $LastLength.Value = $stream.Position
            return $text
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        return ""
    }
}

function Set-RunningState {
    param([bool]$Running)
    $startButton.Enabled = -not $Running
    $stopButton.Enabled = $Running
    $mainCombo.Enabled = -not $Running
    $mmprojCombo.Enabled = -not $Running
    $draftCombo.Enabled = -not $Running
    $mmprojOffload.Enabled = -not $Running
    $embeddedMtp.Enabled = -not $Running
    $gpuOrder.Enabled = -not $Running
    $splitTrack.Enabled = -not $Running
    $contextCombo.Enabled = -not $Running
    $portBox.Enabled = -not $Running
    $refreshModelsButton.Enabled = -not $Running
}

function Start-Model {
    if ($script:serverProcess -and -not $script:serverProcess.HasExited) {
        [System.Windows.Forms.MessageBox]::Show("模型服务正在运行。", "提示")
        return
    }
    if (-not (Test-Path -LiteralPath $serverPath)) {
        [System.Windows.Forms.MessageBox]::Show("找不到 llama-server.exe。", "错误")
        return
    }

    $main = Get-SelectedItem $mainCombo
    if (-not $main -or -not (Test-Path -LiteralPath $main.FullName)) {
        [System.Windows.Forms.MessageBox]::Show("请选择有效的主模型。", "错误")
        return
    }

    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    $stamp = Get-Date -Format "yyyyMMdd-HHmmss"
    $script:stdoutPath = Join-Path $logDir "llama-server-$stamp.out.log"
    $script:stderrPath = Join-Path $logDir "llama-server-$stamp.err.log"
    $script:lastStdoutLength = 0
    $script:lastStderrLength = 0
    $script:startedAt = Get-Date

    $visibleDevices = if ($gpuOrder.SelectedIndex -eq 0) { "0,1" } else { "1,0" }
    $v100 = [int]$splitTrack.Value
    $rtx = 100 - $v100
    $context = [int]$contextCombo.SelectedItem
    $port = [int]$portBox.Value

    $argsList = @(
        "-m", (Quote-Argument $main.FullName),
        "--host", "127.0.0.1",
        "--port", "$port",
        "-ngl", "all",
        "-c", "$context",
        "--split-mode", "layer",
        "--tensor-split", "$v100,$rtx",
        "--main-gpu", "0",
        "--flash-attn", "off",
        "--parallel", "1",
        "--metrics",
        "-v"
    )

    $mmproj = Get-SelectedItem $mmprojCombo
    if ($mmproj) {
        $argsList += "--mmproj"
        $argsList += (Quote-Argument $mmproj.FullName)
        if ($mmprojOffload.Checked) {
            $argsList += "--mmproj-offload"
        }
        else {
            $argsList += "--no-mmproj-offload"
        }
    }

    $draft = Get-SelectedItem $draftCombo
    if ($draft) {
        $argsList += "--spec-draft-model"
        $argsList += (Quote-Argument $draft.FullName)
        $argsList += "--spec-type"
        $argsList += "draft-mtp"
        $argsList += "--spec-draft-ngl"
        $argsList += "all"
    }
    elseif ($embeddedMtp.Checked) {
        $argsList += "--spec-type"
        $argsList += "draft-mtp"
        $argsList += "--spec-draft-ngl"
        $argsList += "all"
    }

    $argumentString = $argsList -join " "
    $previousVisible = $env:CUDA_VISIBLE_DEVICES
    $env:CUDA_VISIBLE_DEVICES = $visibleDevices
    try {
        $script:serverProcess = Start-Process `
            -FilePath $serverPath `
            -ArgumentList $argumentString `
            -RedirectStandardOutput $script:stdoutPath `
            -RedirectStandardError $script:stderrPath `
            -WindowStyle Hidden `
            -PassThru
    }
    finally {
        $env:CUDA_VISIBLE_DEVICES = $previousVisible
    }

    $logBox.Clear()
    Append-Log "CUDA_VISIBLE_DEVICES=$visibleDevices`r`n"
    Append-Log "主模型：$($main.Name)`r`n"
    if ($mmproj) {
        Append-Log "视觉投影器：$($mmproj.Name)`r`n"
    }
    if ($draft) {
        Append-Log "MTP 草稿：$($draft.Name)`r`n"
    }
    Append-Log "PID=$($script:serverProcess.Id)`r`n`r`n"
    $statusLabel.Text = "启动中"
    $sidebarStatus.Text = "运行中`r`nPID $($script:serverProcess.Id)"
    Set-RunningState -Running $true
    $timer.Start()

    if ($openAfterStart.Checked) {
        $openTimer = New-Object System.Windows.Forms.Timer
        $openTimer.Interval = 2000
        $openTimer.Tag = $port
        $openTimer.Add_Tick({
            param($sender, $event)
            $sender.Stop()
            Start-Process "http://127.0.0.1:$($sender.Tag)"
            $sender.Dispose()
        })
        $openTimer.Start()
    }
}

function Stop-Model {
    if ($script:serverProcess -and -not $script:serverProcess.HasExited) {
        try {
            Stop-Process -Id $script:serverProcess.Id -Force
        }
        catch {
            Append-Log "停止失败：$($_.Exception.Message)`r`n"
        }
    }
    $statusLabel.Text = "已停止"
    $sidebarStatus.Text = "已停止"
    Set-RunningState -Running $false
    $timer.Stop()
}

function Show-Devices {
    if (-not (Test-Path -LiteralPath $serverPath)) {
        [System.Windows.Forms.MessageBox]::Show("找不到 llama-server.exe。", "错误")
        return
    }
    $visibleDevices = if ($gpuOrder.SelectedIndex -eq 0) { "0,1" } else { "1,0" }
    $previousVisible = $env:CUDA_VISIBLE_DEVICES
    $env:CUDA_VISIBLE_DEVICES = $visibleDevices
    try {
        $output = & $serverPath --list-devices 2>&1 | Out-String
    }
    finally {
        $env:CUDA_VISIBLE_DEVICES = $previousVisible
    }
    $logBox.Clear()
    Append-Log $output
}

function Resize-Layout {
    $contentWidth = [Math]::Max(700, $form.ClientSize.Width - $content.Left - 20)
    $contentHeight = [Math]::Max(540, $form.ClientSize.Height - $content.Top - 20)
    $content.Width = $contentWidth
    $content.Height = $contentHeight

    $modelGroup.Width = $contentWidth
    $computeGroup.Width = $contentWidth
    $commandPreview.Width = $contentWidth
    $statusLabel.Width = $contentWidth
    $logBox.Width = $contentWidth
    $logBox.Height = [Math]::Max(70, $contentHeight - $logBox.Top - 10)

    $mainCombo.Width = [Math]::Max(430, $modelGroup.Width - 150)
    $mainInfo.Width = $mainCombo.Width

    $auxWidth = [Math]::Max(260, [int](($modelGroup.Width - 160) * 0.54))
    $mmprojCombo.Width = $auxWidth
    $draftCombo.Width = $auxWidth
    $mmprojOffload.Left = $mmprojCombo.Left + $mmprojCombo.Width + 15
    $draftHint.Left = $draftCombo.Left + $draftCombo.Width + 15
    $draftHint.Width = [Math]::Max(150, $modelGroup.Width - $draftHint.Left - 15)

    $splitTrack.Width = [Math]::Max(140, $computeGroup.Width - 570)
    $splitValue.Left = $splitTrack.Left + $splitTrack.Width + 10
    $splitValue.Width = [Math]::Max(130, $computeGroup.Width - $splitValue.Left - 12)
}

$mainCombo.Add_SelectedIndexChanged({
    if (-not $script:suspendPresetChange) {
        $main = Get-SelectedItem $mainCombo
        $mm = Find-MatchingAuxiliary -Main $main -Candidates $catalog.Mmproj
        $dr = Find-MatchingAuxiliary -Main $main -Candidates $catalog.Draft
        if ($mm) {
            Find-ComboItem -Combo $mmprojCombo -Item $mm
        }
        else {
            $mmprojCombo.SelectedIndex = 0
        }
        if ($dr) {
            Find-ComboItem -Combo $draftCombo -Item $dr
            $embeddedMtp.Checked = $false
        }
        else {
            $draftCombo.SelectedIndex = 0
            $embeddedMtp.Checked = ($main.BaseName -match "(?i)mtp")
        }
        Update-CommandPreview
    }
})

$mmprojCombo.Add_SelectedIndexChanged({ Update-CommandPreview })
$draftCombo.Add_SelectedIndexChanged({
    if (Get-SelectedItem $draftCombo) {
        $embeddedMtp.Checked = $false
    }
    Update-CommandPreview
})
$mmprojOffload.Add_CheckedChanged({ Update-CommandPreview })
$embeddedMtp.Add_CheckedChanged({ Update-CommandPreview })
$gpuOrder.Add_SelectedIndexChanged({ Update-CommandPreview })
$contextCombo.Add_SelectedIndexChanged({ Update-CommandPreview })
$portBox.Add_ValueChanged({ Update-CommandPreview })

$splitTrack.Add_ValueChanged({
    $v = $splitTrack.Value
    $splitValue.Text = "$v% / $((100 - $v))%"
    Update-CommandPreview
})

$refreshModelsButton.Add_Click({
    $script:catalog = Get-ModelCatalog
    Refresh-Models
})

$openModelsButton.Add_Click({
    New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null
    Start-Process explorer.exe $modelsDir
})

$startButton.Add_Click({ Start-Model })
$stopButton.Add_Click({ Stop-Model })
$devicesButton.Add_Click({ Show-Devices })
$webButton.Add_Click({ Start-Process "http://127.0.0.1:$($portBox.Value)" })
$logsButton.Add_Click({
    New-Item -ItemType Directory -Force -Path $logDir | Out-Null
    Start-Process explorer.exe $logDir
})

$timer.Add_Tick({
    $stdout = Read-NewLog -Path $script:stdoutPath -LastLength ([ref]$script:lastStdoutLength)
    $stderr = Read-NewLog -Path $script:stderrPath -LastLength ([ref]$script:lastStderrLength)
    Append-Log $stdout
    Append-Log $stderr

    if ($script:serverProcess -and $script:serverProcess.HasExited) {
        $timer.Stop()
        $statusLabel.Text = "进程已退出，代码 $($script:serverProcess.ExitCode)"
        $sidebarStatus.Text = "进程已退出"
        Set-RunningState -Running $false
    }
    elseif ($script:serverProcess) {
        $elapsed = [int]((Get-Date) - $script:startedAt).TotalSeconds
        $statusLabel.Text = "运行中，PID $($script:serverProcess.Id)，已运行 $elapsed 秒"
        $sidebarStatus.Text = "运行中`r`nPID $($script:serverProcess.Id)`r`n$elapsed 秒"
    }
})

$form.Add_Resize({
    if ($form.WindowState -ne [System.Windows.Forms.FormWindowState]::Minimized) {
        Resize-Layout
    }
})

$form.Add_FormClosing({
    param($sender, $event)
    if ($script:serverProcess -and -not $script:serverProcess.HasExited) {
        $choice = [System.Windows.Forms.MessageBox]::Show(
            "模型服务正在运行，是否停止并退出？",
            "确认退出",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Question
        )
        if ($choice -eq [System.Windows.Forms.DialogResult]::No) {
            $event.Cancel = $true
            return
        }
        Stop-Model
    }
})

$splitValue.Text = "68% / 32%"
Refresh-Models
Update-CommandPreview
Resize-Layout

if ($ScreenshotPath) {
    $form.Show()
    [System.Windows.Forms.Application]::DoEvents()
    Start-Sleep -Milliseconds 300
    [System.Windows.Forms.Application]::DoEvents()
    $bitmap = New-Object System.Drawing.Bitmap($form.ClientSize.Width, $form.ClientSize.Height)
    $form.DrawToBitmap($bitmap, $form.ClientRectangle)
    $bitmap.Save($ScreenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()
    $form.Close()
    $form.Dispose()
    Write-Output "SCREENSHOT_OK"
    return
}

if ($SelfTest) {
    $profiles = @()
    foreach ($main in $script:catalog.Main) {
        Select-ModelProfile -Main $main
        $profiles += [pscustomobject]@{
            Main = $main.Name
            Mmproj = (Get-SelectedItem $mmprojCombo).Name
            Draft = (Get-SelectedItem $draftCombo).Name
            EmbeddedMtp = $embeddedMtp.Checked
            Command = $commandPreview.Text
        }
    }
    $result = [pscustomobject]@{
        MainModels = $script:catalog.Main.Count
        MmprojFiles = $script:catalog.Mmproj.Count
        DraftFiles = $script:catalog.Draft.Count
        Profiles = $profiles
    }
    $form.Dispose()
    $result | ConvertTo-Json -Depth 4
    Write-Output "SELF_TEST_OK"
    return
}

[void]$form.ShowDialog()
