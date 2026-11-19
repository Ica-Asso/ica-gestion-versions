@echo off
chcp 65001 >nul
rem ============================================================
rem  Publie une nouvelle version de L'ICA Gestion.
rem  Les ordinateurs de l'association la proposeront d'eux-memes ("Mettre a jour").
rem  Prepare aussi le dossier "Pour la cle USB" avec cette version.
rem ============================================================
cd /d "%~dp0"
set /p ACTUELLE=<VERSION
echo Version actuelle : %ACTUELLE%
set /p NOUVELLE=Nouvelle version (ex. 0.2.0) : 
if "%NOUVELLE%"=="" goto fin
set /p NOTES=En une phrase, ce qui change : 
>VERSION echo %NOUVELLE%

set "SORTIE=Pour la cle USB"
echo.
echo Construction de la version %NOUVELLE% (1 a 2 minutes)...
if exist "%SORTIE%" rmdir /s /q "%SORTIE%"
rem Publication dans un dossier temporaire : l'apostrophe de « Gestion l'ICA » gene dotnet publish.
set "TMPPUB=%TEMP%\ICA-publication"
if exist "%TMPPUB%" rmdir /s /q "%TMPPUB%"
dotnet publish src\ICA.Desktop\ICA.Desktop.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o "%TMPPUB%" > publication.log 2>&1
if not "%errorlevel%"=="0" goto erreur
xcopy /e /i /y /q "%TMPPUB%\*" "%SORTIE%\Application\" >nul
if exist "src\ICA.Desktop\google-client.json" (copy /y "src\ICA.Desktop\google-client.json" "%SORTIE%\Application\" >nul) else (echo ATTENTION : google-client.json absent.)
copy /y "deploy\Installer L'ICA Gestion.bat" "%SORTIE%\" >nul
copy /y "deploy\LISEZ-MOI cle USB.txt" "%SORTIE%\" >nul

rem Avec l'outil GitHub (gh) installe et connecte : publication automatique.
where gh >nul 2>&1
if "%errorlevel%"=="0" (
  gh release create v%NOUVELLE% "%SORTIE%\Application\ICA-Gestion.exe" --repo Ica-Asso/ica-gestion-versions --title "Version %NOUVELLE%" --notes "%NOTES%"
  if not errorlevel 1 (
    echo.
    echo Version %NOUVELLE% publiee ! Les ordinateurs la proposeront dans les prochaines heures.
    pause
    exit /b 0
  )
)

rem Sinon : on ouvre la page GitHub, il reste a deposer le fichier.
echo.
echo Derniere etape sur la page GitHub qui s'ouvre :
echo   1. Glissez le fichier ICA-Gestion.exe (dossier qui s'ouvre) dans "Attach binaries"
echo   2. Cliquez "Publish release"
explorer "%SORTIE%\Application"
start "" "https://github.com/Ica-Asso/ica-gestion-versions/releases/new?tag=v%NOUVELLE%&title=Version%%20%NOUVELLE%"
echo.
echo Texte a coller dans la description : %NOTES%
pause
exit /b 0

:erreur
>VERSION echo %ACTUELLE%
echo.
echo La construction a echoue. Le detail est dans publication.log
pause
exit /b 1
:fin
exit /b 0
