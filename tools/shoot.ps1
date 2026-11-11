<#
.SYNOPSIS
  Chụp ảnh từng trang của Toolbox ở theme sáng, tối và máy trắng.

.DESCRIPTION
  Chạy Toolbox.exe với TOOLBOX_DATA trỏ vào BẢN SAO dữ liệu (không bao giờ đụng dữ liệu thật), điều khiển
  cửa sổ bằng UI Automation (bấm mục trong thanh trái và tab của workspace theo tên hiển thị), rồi chụp
  cửa sổ ra PNG ở thư mục ngoài repo.

  Ảnh đặt tên: <theme>-<trang>.png, ví dụ light-home.png, dark-ws1-Chạy.png, blank-light-library.png.

.PARAMETER Source
  Thư mục dữ liệu để sao chép. Mặc định %APPDATA%\Toolbox. Chỉ đọc.

.PARAMETER Out
  Thư mục lưu ảnh. Mặc định %TEMP%\toolbox-shots. Không được nằm trong repo.

.PARAMETER Exe
  Toolbox.exe cần chạy. Mặc định bản Debug rồi Release của repo.

.EXAMPLE
  pwsh -NoProfile -File tools/shoot.ps1
  pwsh -NoProfile -File tools/shoot.ps1 -Source D:\mau-du-lieu -Out D:\anh
#>
[CmdletBinding()]
param(
    [string]$Source = (Join-Path $env:APPDATA 'Toolbox'),
    [string]$Out = (Join-Path $env:TEMP 'toolbox-shots'),
    [string]$Exe
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot

if (-not $Exe) {
    $Exe = @('src\Toolbox\bin\Debug\net48\Toolbox.exe', 'src\Toolbox\bin\Release\net48\Toolbox.exe') |
        ForEach-Object { Join-Path $repo $_ } | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Exe -or -not (Test-Path $Exe)) { throw 'Không thấy Toolbox.exe. Build trước: dotnet build src/Toolbox/Toolbox.csproj' }

$outFull = [IO.Path]::GetFullPath($Out)
if ($outFull.StartsWith([IO.Path]::GetFullPath($repo), [StringComparison]::OrdinalIgnoreCase)) {
    throw "Thư mục ảnh phải nằm ngoài repo: $outFull"
}
New-Item -ItemType Directory -Force $outFull | Out-Null

Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type -Namespace Shoot -Name Win -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool SetForegroundWindow(System.IntPtr h);
[DllImport("user32.dll")] public static extern bool ShowWindow(System.IntPtr h, int cmd);
[DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
[DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, System.UIntPtr extra);
[DllImport("user32.dll")] public static extern bool GetWindowRect(System.IntPtr h, out RECT r);
[StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
'@

function Find-ByName($root, [string]$name) {
    $cond = New-Object Windows.Automation.PropertyCondition ([Windows.Automation.AutomationElement]::NameProperty), $name
    $root.FindFirst([Windows.Automation.TreeScope]::Descendants, $cond)
}

function Click-ByName($window, [string]$name) {
    $el = Find-ByName $window $name
    if (-not $el) { return $false }
    # Chuột thật: RadioButton trong WPF chỉ phát sự kiện Click khi bấm thật, SelectionItemPattern.Select() chỉ đánh dấu.
    $rect = $el.Current.BoundingRectangle
    if ($rect.IsEmpty -or $rect.Width -le 0) { return $false }
    [Shoot.Win]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle) | Out-Null
    [Shoot.Win]::SetCursorPos([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2)) | Out-Null
    Start-Sleep -Milliseconds 80
    [Shoot.Win]::mouse_event(0x02, 0, 0, 0, [UIntPtr]::Zero)   # left down
    [Shoot.Win]::mouse_event(0x04, 0, 0, 0, [UIntPtr]::Zero)   # left up
    Start-Sleep -Milliseconds 900
    return $true
}

function Save-Shot($proc, [string]$file) {
    $proc.Refresh()
    $h = $proc.MainWindowHandle
    [Shoot.Win]::ShowWindow($h, 9) | Out-Null
    [Shoot.Win]::SetForegroundWindow($h) | Out-Null
    Start-Sleep -Milliseconds 400
    $r = New-Object Shoot.Win+RECT
    [Shoot.Win]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $hgt = $r.Bottom - $r.Top
    $bmp = New-Object Drawing.Bitmap $w, $hgt
    $g = [Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, (New-Object Drawing.Size $w, $hgt))
    $g.Dispose()
    $bmp.Save($file, [Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    Write-Host "  $file"
}

function Invoke-Session([string]$label, [string]$data, [bool]$blank) {
    $env:TOOLBOX_DATA = $data
    $proc = Start-Process -FilePath $Exe -PassThru
    try {
        $deadline = (Get-Date).AddSeconds(30)
        do { Start-Sleep -Milliseconds 300; $proc.Refresh() } while ($proc.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline)
        if ($proc.MainWindowHandle -eq 0) { throw "Toolbox không mở cửa sổ ($label)." }
        Start-Sleep -Seconds 2
        $win = [Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)

        $pages = [ordered]@{ home = 'Trang chủ'; library = 'Thư viện'; tasks = 'Tác vụ'; agents = 'Agent' }
        foreach ($key in $pages.Keys) {
            if (Click-ByName $win $pages[$key]) { Save-Shot $proc (Join-Path $outFull "$label-$key.png") }
            else { Write-Warning "Không thấy mục '$($pages[$key])' ($label)." }
        }

        # Palette Ctrl+K
        [Shoot.Win]::SetForegroundWindow($proc.MainWindowHandle) | Out-Null
        [Windows.Forms.SendKeys]::SendWait('^k'); Start-Sleep -Milliseconds 700
        Save-Shot $proc (Join-Path $outFull "$label-palette.png")
        [Windows.Forms.SendKeys]::SendWait('{ESC}'); Start-Sleep -Milliseconds 300

        if (-not $blank) {
            $wsFile = Join-Path $data 'workspaces.json'
            $names = @()
            if (Test-Path $wsFile) {
                $json = Get-Content $wsFile -Raw -Encoding UTF8 | ConvertFrom-Json
                $list = if ($json.workspaces) { $json.workspaces } else { $json }
                $names = @($list | ForEach-Object { $_.name } | Where-Object { $_ })
            }
            $i = 0
            foreach ($name in $names) {
                $i++
                if (-not (Click-ByName $win $name)) { Write-Warning "Không thấy workspace '$name'."; continue }
                foreach ($tab in 'Chạy', 'Tool', 'Workflow', 'Chia sẻ') {
                    if (Click-ByName $win $tab) { Save-Shot $proc (Join-Path $outFull "$label-ws$i-$tab.png") }
                }
            }
        }
    }
    finally {
        if (-not $proc.HasExited) { $proc.CloseMainWindow() | Out-Null; if (-not $proc.WaitForExit(5000)) { $proc.Kill() } }
        Remove-Item Env:TOOLBOX_DATA -ErrorAction SilentlyContinue
    }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('toolbox-shoot-' + [Guid]::NewGuid().ToString('N'))
try {
    foreach ($theme in 'light', 'dark') {
        # Dữ liệu sao: bản copy của $Source với theme ép sẵn trong settings.json.
        $copy = Join-Path $scratch "data-$theme"
        New-Item -ItemType Directory -Force $copy | Out-Null
        if (Test-Path $Source) { Copy-Item -Path (Join-Path $Source '*') -Destination $copy -Recurse -Force -Exclude 'backup', 'runs' }
        $settingsFile = Join-Path $copy 'settings.json'
        $settings = if (Test-Path $settingsFile) { Get-Content $settingsFile -Raw -Encoding UTF8 | ConvertFrom-Json } else { [pscustomobject]@{} }
        $settings | Add-Member -NotePropertyName theme -NotePropertyValue $theme -Force
        $settings | ConvertTo-Json | Set-Content $settingsFile -Encoding UTF8
        Write-Host "[$theme] dữ liệu sao"
        Invoke-Session $theme $copy $false

        # Máy trắng: thư mục dữ liệu rỗng, chỉ có theme.
        $empty = Join-Path $scratch "blank-$theme"
        New-Item -ItemType Directory -Force $empty | Out-Null
        ([pscustomobject]@{ theme = $theme; onboardingDismissed = $false } | ConvertTo-Json) | Set-Content (Join-Path $empty 'settings.json') -Encoding UTF8
        Write-Host "[blank-$theme] máy trắng"
        Invoke-Session "blank-$theme" $empty $true
    }
}
finally {
    Remove-Item -Recurse -Force $scratch -ErrorAction SilentlyContinue
}
Write-Host "Xong. Ảnh ở: $outFull"
