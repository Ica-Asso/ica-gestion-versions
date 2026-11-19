@echo off
rem ============================================================
rem  Installe L'ICA Gestion sur cet ordinateur (sans droits administrateur).
rem ============================================================
cd /d "%~dp0"
set "CIBLE=%LOCALAPPDATA%\Programs\L-ICA Gestion"
echo Installation de L ICA Gestion...
if not exist "Application\ICA-Gestion.exe" goto introuvable
taskkill /im ICA-Gestion.exe /f >nul 2>nul
if not exist "%CIBLE%" mkdir "%CIBLE%"
xcopy "Application\*" "%CIBLE%\" /e /i /y /q >nul
if not "%errorlevel%"=="0" goto erreur
powershell -NoProfile -ExecutionPolicy Bypass -Command "$w = New-Object -ComObject WScript.Shell; foreach ($d in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('Programs'))) { $s = $w.CreateShortcut((Join-Path $d 'L''ICA Gestion.lnk')); $s.TargetPath = Join-Path $env:LOCALAPPDATA 'Programs\L-ICA Gestion\ICA-Gestion.exe'; $s.WorkingDirectory = Join-Path $env:LOCALAPPDATA 'Programs\L-ICA Gestion'; $s.Description = 'L''ICA Gestion'; $s.Save() }"
echo.
echo L ICA Gestion est installee. Un raccourci a ete cree sur le Bureau.
echo (Les donnees ne sont pas touchees si l application etait deja installee.)
start "" "%CIBLE%\ICA-Gestion.exe"
pause
exit /b 0
:introuvable
echo Le dossier Application est introuvable a cote de ce fichier.
pause
exit /b 1
:erreur
echo La copie a echoue. Fermez L ICA Gestion si elle est ouverte, puis recommencez.
pause
exit /b 1
