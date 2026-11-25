param([string]$Out)
# Alle 2 s: TCP-Verbindungen mit Prozessname + DNS-Cache -> Textdatei
while ($true) {
  $t = Get-Date -Format "HH:mm:ss.fff"
  try {
    Get-NetTCPConnection -ErrorAction SilentlyContinue |
      Where-Object { $_.RemoteAddress -notin @('127.0.0.1','::1','0.0.0.0','::') } |
      ForEach-Object {
        $p = (Get-Process -Id $_.OwningProcess -ErrorAction SilentlyContinue).ProcessName
        "$t TCP $p $($_.State) $($_.LocalAddress):$($_.LocalPort) -> $($_.RemoteAddress):$($_.RemotePort)"
      } | Add-Content -Path "$Out\conns.txt"
    Get-DnsClientCache -ErrorAction SilentlyContinue |
      ForEach-Object { "$t DNS $($_.Entry) $($_.Data)" } | Add-Content -Path "$Out\dns.txt"
  } catch {}
  Start-Sleep -Seconds 2
}
