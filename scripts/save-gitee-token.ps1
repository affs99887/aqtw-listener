# Stores a Gitee personal access token for scripts/publish-gitee.ps1. The token is encrypted with
# Windows DPAPI for the current user, so only this Windows account on this machine can read it.
$ErrorActionPreference = 'Stop'
$folder = Join-Path $env:USERPROFILE '.aqtw-release'
$path = Join-Path $folder 'gitee-token.txt'
New-Item -ItemType Directory -Force $folder | Out-Null
$secure = Read-Host -AsSecureString '粘贴 Gitee 私人令牌后按回车（输入时不显示）'
$plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
try {
    $user = Invoke-RestMethod -UseBasicParsing -Uri ('https://gitee.com/api/v5/user?access_token=' + [Uri]::EscapeDataString($plain))
} catch {
    throw '令牌无效或无法连接 Gitee：' + $_.Exception.Message
} finally {
    $plain = $null
}
$secure | ConvertFrom-SecureString | Set-Content -LiteralPath $path -Encoding ASCII
Write-Host ('已保存。Gitee 用户名：' + $user.login)
