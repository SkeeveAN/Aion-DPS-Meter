@echo off
set "OUT=%USERPROFILE%\Desktop\aion2\json\aion2-%DATE:~6,4%-%DATE:~3,2%-%DATE:~0,2%_%TIME:~0,2%-%TIME:~3,2%.jsonl"
set "OUT=%OUT: =0%"
echo Aufnahme mit Debug-Punkten nach: %OUT%
echo Bei einem Ton: kurz notieren, was du gerade getan hast (die Uhrzeit steht in der .points.jsonl neben der Aufnahme).
echo Zum Beenden stop tippen + Enter (oder Fenster schliessen). Ein einzelnes Enter beendet NICHT.
start /b /wait "" "%LOCALAPPDATA%\AionDpsMeter\current\Aion DPS.exe" aion2-record "%OUT%" --points
echo.
echo Fertig. Datei: %OUT%
pause
