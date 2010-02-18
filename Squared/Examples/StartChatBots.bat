@REM startchatbots number_of_bots send_rate
@ECHO OFF
FOR /L %%i in (1,1,%1) DO (
  ping -n 1 localhost > NUL
  start "ChatBot%%i" /MIN .\TelnetChatBot\bin\x86\release\TelnetChatBot.exe %2 %3 %4 %5 %6 %7 %8 %9
)