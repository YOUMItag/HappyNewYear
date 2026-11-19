@echo off
setlocal EnableExtensions
cd /d "%~dp0"

echo ============================================================
echo   HappyNewYear  --  GitHub  (push only)
echo ============================================================
echo.

git --version >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Git is not installed. https://git-scm.com/download/win
  pause
  exit /b 1
)

if not exist ".git" (
  echo [ERROR] No .git here. Run this file from D:\Unity\HappyNewYear
  pause
  exit /b 1
)

rem ---------- repository address ----------
rem Priority: 1) command line argument  2) repo-url.txt  3) built-in default
set "REMOTE=%~1"
if "%REMOTE%"=="" if exist "repo-url.txt" set /p REMOTE=<repo-url.txt
if "%REMOTE%"=="" set "REMOTE=https://github.com/YOUMItag/HappyNewYear.git"

echo Repository: %REMOTE%
echo.
echo Before pushing, the repository MUST already exist and be EMPTY
echo (created on github.com without README / gitignore / license).
echo.
set "OK="
set /p OK=Press Enter to push, or type a different URL then Enter: 
if not "%OK%"=="" set "REMOTE=%OK%"
echo.

git remote remove origin >nul 2>&1
git remote add origin "%REMOTE%"

echo ---- local status ----
git log --oneline -1
echo.
echo ---- pushing ----
git push -u origin main

if errorlevel 1 (
  echo.
  echo [FAILED] Common causes:
  echo   1^) The repo already has commits ^(a README was created with it^).
  echo      Fix:  git pull --rebase origin main   then run this file again.
  echo   2^) Not signed in, or no permission to that repository.
  echo   3^) Wrong address ^(owner or repo name typo^).
) else (
  echo.
  echo [DONE] Open the repository page in your browser to check the files.
)
echo.
pause
