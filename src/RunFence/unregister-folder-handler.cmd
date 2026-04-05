@echo off
reg delete "HKCU\Software\Classes\Directory\shell\open" /f 2>nul
reg delete "HKCU\Software\Classes\Directory\shell\explore" /f 2>nul
reg delete "HKCU\Software\Classes\Directory\shell" /ve /f 2>nul
reg delete "HKCU\Software\Classes\Folder\shell\open" /f 2>nul
