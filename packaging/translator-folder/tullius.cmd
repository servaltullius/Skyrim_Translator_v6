@echo off
rem The translator folder's own tullius.cmd, for an app that opened this folder rather than agent-kit:
rem the same command as agent-kit\tullius.cmd (the projects are agent-kit's either way).
rem ASCII only: cmd reads this file in the system code page.
if not exist "%~dp0TulliusTranslator.exe" (
  echo Failed: TulliusTranslator.exe is not in this folder. Keep this file beside the translator.
  exit /b 3
)
start "" /wait /b "%~dp0TulliusTranslator.exe" agent %*
exit /b %ERRORLEVEL%
