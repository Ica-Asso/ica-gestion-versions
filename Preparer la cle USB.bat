@echo off
rem ============================================================
rem  Prépare L'ICA Gestion pour l'installer sur un autre PC (clé USB).
rem  Version autonome : aucun .NET à installer sur le PC de destination.
rem ============================================================
cd /d "%~dp0"
set "SORTIE=Pour la cle USB"
echo Preparation de L ICA Gestion pour la cle USB (1 a 2 minutes)...
if exist "%SORTIE%" rmdir /s /q "%SORTIE%"
rem Publication dans un dossier temporaire : l'apostrophe de « Gestion l'ICA » gene dotnet publish.
set "TMPPUB=%TEMP%\ICA-publication"
if exist "%TMPPUB%" rmdir /s /q "%TMPPUB%"
dotnet publish src\ICA.Desktop\ICA.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "%TMPPUB%" > publication.log 2>&1
if not "%errorlevel%"=="0" goto erreur
xcopy /e /i /y /q "%TMPPUB%\*" "%SORTIE%\Application\" >nul
if exist "src\ICA.Desktop\google-client.json" (copy /y "src\ICA.Desktop\google-client.json" "%SORTIE%\Application\" >nul) else (echo ATTENTION : google-client.json absent, la connexion Google ne marchera pas.)
copy /y "deploy\Installer L'ICA Gestion.bat" "%SORTIE%\" >nul
copy /y "deploy\LISEZ-MOI cle USB.txt" "%SORTIE%\" >nul
echo.
echo Termine ! Copiez le dossier %SORTIE% sur la cle USB.
echo Chez elle : double-cliquez sur Installer L ICA Gestion.bat.
explorer "%SORTIE%"
pause
exit /b 0
:erreur
echo.
echo La preparation a echoue. Le detail est dans publication.log
pause
exit /b 1
