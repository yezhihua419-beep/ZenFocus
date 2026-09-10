$file = "D:\github\chanjing\src\ChanJing.App\App.xaml.cs"
$text = [System.IO.File]::ReadAllText($file)
$text = $text -replace "`r`n", "`n"

$old = @"
            try
            {
                var domains = ChanJing.Core.Services.HostsBlocker.GetPreAppliedDomains();
                if (domains.Count > 0)
                {
                    ChanJing.Core.Services.HostsBlocker.Apply(domains);
                    LogAction("专注开始", `$"自动应用网站屏蔽（{domains.Count}个域名）");
                }
                else
                {
                    LogAction("专注开始", "无预应用网站屏蔽配置");
                }
"@

$new = @"
            try
            {
                if (AppServices.Blocklist.IsManualShieldActive())
                {
                    LogAction("专注开始", "手动屏蔽已启用，跳过重复写hosts");
                }
                else
                {
                    var domains = ChanJing.Core.Services.HostsBlocker.GetPreAppliedDomains();
                    if (domains.Count > 0)
                    {
                        ChanJing.Core.Services.HostsBlocker.Apply(domains);
                        LogAction("专注开始", `$"自动应用网站屏蔽（{domains.Count}个域名）");
                    }
                    else
                    {
                        LogAction("专注开始", "无预应用网站屏蔽配置");
                    }
                }
"@

if ($text.Contains($old)) {
    $text = $text.Replace($old, $new)
    Write-Output "FocusStarted替换成功"
} else {
    Write-Output "FocusStarted替换失败"
}

$utf8Bom = New-Object System.Text.UTF8Encoding($true)
$text = $text -replace "`n", "`r`n"
[System.IO.File]::WriteAllText($file, $text, $utf8Bom)
