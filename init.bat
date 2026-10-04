@echo off

git init

set "OLD_REMOTE_URL="

FOR /F "delims=" %%i IN ('git config --get remote.origin.url') DO (
    set "OLD_REMOTE_URL=%%i"
)

echo The current URL of the Remote Repository is: %OLD_REMOTE_URL%
echo.

set /p REMOTE_URL="Enter URL of the Remote Repository (e.g. https://github.com/user/repo.git): "
echo.

echo 1 - duockeny
echo 2 - christianlauex
echo 3 - 2300059i
set /p ACCOUNT="Select owner of the Remote Repository (1 - 3): "
echo.

if "%ACCOUNT%"=="1" (
    set GIT_USER_NAME="duockeny"
    set GIT_EMAIL="duockeny@gmail.com"
    set "NEW_REMOTE_URL=%REMOTE_URL%"
) else if "%ACCOUNT%"=="2" (
    set GIT_USER_NAME="christianlauex"
    set GIT_EMAIL="christianlauex@gmail.com"
    set "NEW_REMOTE_URL=%REMOTE_URL:github.com=github.com-christianlauex%"
) else if "%ACCOUNT%"=="3" (
    set GIT_USER_NAME="2300059i"
    set GIT_EMAIL="2300059i@student.tp.edu.sg"
    set "NEW_REMOTE_URL=%REMOTE_URL:github.com=github.com-2300059i%"
) else (
    echo Invalid choice.
    echo.
    pause
    exit
)

git config user.name "%GIT_USER_NAME%"
git config user.email "%GIT_EMAIL%"
git remote add origin "%NEW_REMOTE_URL%"

echo Setup complete.
echo New User Name:  %GIT_USER_NAME%
echo New Email:      %GIT_EMAIL%
echo New Remote URL: %NEW_REMOTE_URL%

git pull

echo.
pause