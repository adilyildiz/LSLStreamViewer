@echo off
title LSL Stream Viewer - Tek Dosya Derleyici
echo =======================================================
echo   LSL Stream Viewer - Tek Dosya (Single-File) Build
echo   Tum DLL'ler EXE icine gomulerek paketlenecektir.
echo =======================================================
echo.
echo Derleme baslatiliyor... Lutfen bekleyin.
echo.

dotnet publish -c Release -r win-x64 --self-contained true -o ./publish

if %ERRORLEVEL% EQU 0 (
    echo.
    echo =======================================================
    echo  [BASARILI] Tek parca EXE basariyla olusturuldu!
    echo.
    echo  Dosya Konumlari:
    echo  1. %~dp0LSLStreamViewer.exe
    echo  2. %~dp0publish\LSLStreamViewer.exe
    echo =======================================================
) else (
    echo.
    echo [HATA] Derleme sirasinda bir sorun olustu!
)

echo.
pause
