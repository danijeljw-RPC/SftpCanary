Import-Module Posh-SSH

# --- Config ---
$server   = ''
$port     = 22
$username = ''
$password = ''   # for quick test only; avoid plain text in real scripts

# --- Build credential ---
$securePassword = ConvertTo-SecureString $password -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential($username, $securePassword)

# --- Try to open an SFTP session ---
try {
    $session = New-SFTPSession -ComputerName $server -Port $port -Credential $cred -AcceptKey -ErrorAction Stop
    Write-Host "SFTP connection SUCCESS. Session ID: $($session.SessionId)"

    # Try listing root directory as a basic sanity check
    $items = Get-SFTPChildItem -SessionId $session.SessionId -Path '/'
    Write-Host "Directory listing from '/':"
    $items | Select-Object Name, Length, LastWriteTime | Format-Table

} catch {
    Write-Host "SFTP connection FAILED:" -ForegroundColor Red
    Write-Host $_.Exception.Message
} finally {
    if ($session) {
        Remove-SFTPSession -SessionId $session.SessionId -ErrorAction SilentlyContinue
    }
}
