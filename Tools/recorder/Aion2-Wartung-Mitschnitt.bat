@echo off
rem Mitschnitt des Netzwerkverkehrs beim Start von Aion 2 (Wartungsmodus-Analyse)
net session >nul 2>&1
if errorlevel 1 (
  echo Starte neu mit Administratorrechten ...
  powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
set "OUT=%~dp0lauf-%DATE:~6,4%-%DATE:~3,2%-%DATE:~0,2%_%TIME:~0,2%-%TIME:~3,2%"
set "OUT=%OUT: =0%"
mkdir "%OUT%"
ipconfig /flushdns >nul
pktmon stop >nul 2>&1
pktmon filter remove >nul 2>&1
pktmon start --capture --pkt-size 1024 --file-name "%OUT%\mitschnitt.etl" --file-size 1024
start "poll" /min powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0poll.ps1" -Out "%OUT%"
echo.
echo ==============================================================
echo  Mitschnitt laeuft. Jetzt Aion 2 / Launcher starten,
echo  Region-Auswahl versuchen, bis die Wartungsmeldung kommt.
echo  Dann hier eine Taste druecken zum Beenden.
echo ==============================================================
pause >nul
pktmon stop
taskkill /fi "WINDOWTITLE eq poll*" /f >nul 2>&1
powershell -NoProfile -Command "Get-CimInstance Win32_Process -Filter \"CommandLine like '%%poll.ps1%%'\" | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }"
pktmon etl2pcap "%OUT%\mitschnitt.etl" --out "%OUT%\mitschnitt.pcapng"
pktmon etl2txt "%OUT%\mitschnitt.etl" --out "%OUT%\mitschnitt.txt"
echo Fertig: %OUT%
pause
