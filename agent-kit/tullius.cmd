@echo off
rem The agent mode's commands are the translator's own (TulliusTranslator.exe agent ...). A window program
rem is not waited for by PowerShell or cmd, so this starts it and waits, passing its output and exit code on.
rem ASCII only: cmd reads this file in the system code page.
if not exist "%~dp0..\TulliusTranslator.exe" (
  echo Failed: TulliusTranslator.exe is not in the folder above agent-kit. Keep agent-kit inside the translator folder.
  exit /b 3
)
start "" /wait /b "%~dp0..\TulliusTranslator.exe" agent %*
exit /b %ERRORLEVEL%
