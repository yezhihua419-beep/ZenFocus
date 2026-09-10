$file = "D:\github\chanjing\src\ChanJing.App\App.xaml.cs"
$lines = [System.IO.File]::ReadAllLines($file)
$newLines = New-Object System.Collections.Generic.List[string]
$inserted = $false

for ($i = 0; $i -lt $lines.Count; $i++) {
    if (-not $inserted -and $lines[$i] -match 'var domains = ChanJing\.Core\.Services\.HostsBlocker\.GetPreAppliedDomains\(\)') {
        $indent = "                "
        $newLines.Add($indent + "if (AppServices.Blocklist.IsManualShieldActive())")
        $newLines.Add($indent + "{")
        $newLines.Add($indent + "    LogAction(`"专注开始`", `"手动屏蔽已启用，跳过重复写hosts`");")
        $newLines.Add($indent + "}")
        $newLines.Add($indent + "else")
        $newLines.Add($indent + "{")
        $inserted = $true
        Write-Output "在第$($i+1)行前插入手动屏蔽判断"
    }
    $newLines.Add($lines[$i])
}

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllLines($file, $newLines, $utf8Bom)
