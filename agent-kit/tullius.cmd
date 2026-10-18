@echo off
rem The agent mode's commands are the translator's own (TulliusTranslator.exe agent ...). A window program
rem is not waited for by PowerShell or cmd, so this starts it and waits, passing its output and exit code on.
start "" /wait /b "%~dp0..\TulliusTranslator.exe" agent %*
exit /b %ERRORLEVEL%
