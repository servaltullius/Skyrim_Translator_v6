@echo off
rem Connects the translator's tools (tullius) to Antigravity: adds them to its MCP settings
rem (%USERPROFILE%\.gemini\config\mcp_config.json), keeping the other servers and a copy of the old file.
rem ASCII only: cmd reads this file in the system code page.
call "%~dp0tullius.cmd" mcp-config --install antigravity
echo.
pause
