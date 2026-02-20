$content = Get-Content 'Version.cs' -Raw

# 精确匹配 Version 键名，提取引号内的所有内容 (Precisely match the 'Version' key name and extract all content within quotes)
if ($content -match '(?i)Version\s*=\s*"([^"]+)"') {
    $v = $Matches[1]
    
    # 处理 app.manifest 所需的 4 位数字格式 (Handle the 4-digit format required for app.manifest)
    # 取前四位数字，不足补 0 (Take the first 4 numbers, pad with 0 if necessary)
    $numericParts = ($v -split '[^0-9]+') | Where-Object { $_ -ne "" }
    $v4 = [System.Collections.Generic.List[string]]$numericParts
    while ($v4.Count -lt 4) { $v4.Add('0') }
    $manifestVersion = ($v4[0..3]) -join '.'
    
    # 同步 app.manifest
    if (Test-Path 'app.manifest') {
        $m = Get-Content 'app.manifest' -Raw
        $m = $m -replace '(?i)assemblyIdentity\s+version="[^"]+"', "assemblyIdentity version=`"$manifestVersion`""
        $m | Set-Content 'app.manifest' -Encoding UTF8
    }
    
    # 向 MSBuild 输出原始版本号用于生成程序属性 (Output original version to MSBuild for assembly properties)
    Write-Output $v
}
