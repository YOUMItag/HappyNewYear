@echo off
setlocal enabledelayedexpansion
cd /d "%~dp0"
chcp 65001 >nul

echo ============================================================
echo   HappyNewYear  ->  GitHub
echo   This script commits the project and pushes it to GitHub.
echo ============================================================
echo.

git --version >nul 2>&1
if errorlevel 1 (
  echo [ERROR] Git is not installed. Get it from https://git-scm.com/download/win
  pause
  exit /b 1
)

rem ---------- 1. author identity (commit history is public) ----------
echo Step 1/4  Set the author name and email used in commits.
echo           Use your GitHub noreply email if you want to hide it.
echo.
set "GIT_NAME="
set "GIT_EMAIL="
set /p GIT_NAME=  Your name or nickname: 
set /p GIT_EMAIL=  Your email (e.g. 12345678+user@users.noreply.github.com): 
if "%GIT_NAME%"=="" set "GIT_NAME=HappyNewYear Dev"
if "%GIT_EMAIL%"=="" set "GIT_EMAIL=noreply@example.com"
git config user.name "%GIT_NAME%"
git config user.email "%GIT_EMAIL%"
echo.

rem ---------- 2. repository + commit ----------
echo Step 2/4  Create the local repository and commit.
if not exist ".git" (
  git init -b main
) else (
  git branch -M main
)
git add -A
git commit -m "feat: family-title match-3 game demo (rules, board generation, family tree panel, automated checks)"
if errorlevel 1 echo   (nothing new to commit - continuing)
echo.

rem ---------- 3. remote ----------
echo Step 3/4  Remote repository address.
echo           Create an EMPTY repo on github.com first (no README, no .gitignore).
echo.
set "REMOTE="
set /p REMOTE=  https://github.com/<your-name>/<repo>.git : 
if "%REMOTE%"=="" (
  echo [ERROR] empty address, stop.
  pause
  exit /b 1
)
git remote remove origin >nul 2>&1
git remote add origin "%REMOTE%"
echo.

rem ---------- 4. push ----------
echo Step 4/4  Push. A browser window may open for GitHub sign-in.
echo.
git push -u origin main
if errorlevel 1 (
  echo.
  echo [FAILED] Push did not succeed. Common reasons:
  echo   1^) The repo was created with a README, so the remote already has commits.
  echo      Fix: git pull --rebase origin main   then run this script again.
  echo   2^) Not signed in / no permission for that repository.
) else (
  echo.
  echo [DONE] Open %REMOTE% in your browser to check the files.
)
echo.
pause
