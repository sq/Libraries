@REM startchatbots number_of_bots send_rate
@FOR /L %%i in (1,1,%1) DO @start "ChatBot%%i" /MIN .\TelnetChatBot\bin\release\TelnetChatBot.exe %2 %3 %4 %5 %6 %7 %8 %9