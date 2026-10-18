; ModScript X — установщик для Windows (NSIS 3).
; Сначала: dotnet publish -c Release -r win-x64 -o Setup/out
; Затем:   makensis Setup/modscript.nsi  →  Setup/ModScriptX-Setup-1.0.0.exe
; Ставится без прав администратора в %LOCALAPPDATA%\Programs\ModScript X,
; добавляет папку в PATH пользователя и регистрирует файлы .modx.

; Программа 64-битная — установщик тоже.
Target amd64-unicode
!define NAME "ModScript X"
!define VERSION "1.0.0"
!define KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\ModScriptX"

Name "${NAME} ${VERSION}"
OutFile "ModScriptX-Setup-${VERSION}.exe"
InstallDir "$LOCALAPPDATA\Programs\${NAME}"
InstallDirRegKey HKCU "${KEY}" "InstallLocation"
RequestExecutionLevel user
SetCompressor /SOLID lzma
Icon "..\..\ModLaunch\Assets\icon.ico"
UninstallIcon "..\..\ModLaunch\Assets\icon.ico"
VIProductVersion "${VERSION}.0"
VIAddVersionKey "ProductName" "${NAME}"
VIAddVersionKey "FileDescription" "${NAME} Setup"
VIAddVersionKey "FileVersion" "${VERSION}"
VIAddVersionKey "ProductVersion" "${VERSION}"
VIAddVersionKey "CompanyName" "ModLaunch"
VIAddVersionKey "LegalCopyright" "ModLaunch"

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "StrFunc.nsh"
!include "WinMessages.nsh"
${StrStr}
${UnStrRep}

!define MUI_ICON "..\..\ModLaunch\Assets\icon.ico"
!define MUI_UNICON "..\..\ModLaunch\Assets\icon.ico"
!define MUI_FINISHPAGE_RUN
!define MUI_FINISHPAGE_RUN_FUNCTION OpenPrompt
!define MUI_FINISHPAGE_RUN_TEXT "$(RunText)"
!define MUI_FINISHPAGE_SHOWREADME "$INSTDIR\Examples"
!define MUI_FINISHPAGE_SHOWREADME_TEXT "$(ExamplesText)"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"

LangString RunText ${LANG_ENGLISH} "Open a command prompt with ModScript X"
LangString RunText ${LANG_RUSSIAN} "Открыть командную строку с ModScript X"
LangString ExamplesText ${LANG_ENGLISH} "Show example .modx scripts"
LangString ExamplesText ${LANG_RUSSIAN} "Показать примеры .modx"
LangString TypeName ${LANG_ENGLISH} "ModScript X script"
LangString TypeName ${LANG_RUSSIAN} "Скрипт ModScript X"
LangString CheckVerb ${LANG_ENGLISH} "Check with ModScript X"
LangString CheckVerb ${LANG_RUSSIAN} "Проверить в ModScript X"

Function OpenPrompt
  SetOutPath "$INSTDIR\Examples"
  Exec `"$WINDIR\System32\cmd.exe" /k set "PATH=$INSTDIR;%PATH%" && modscript`
FunctionEnd

Function .onInit
  !insertmacro MUI_LANGDLL_DISPLAY
FunctionEnd

Section "Install"
  SetOutPath "$INSTDIR"
  File "out\modscript.exe"
  File "..\README.md"
  SetOutPath "$INSTDIR\Examples"
  File "out\Examples\*.modx"

  ; PATH пользователя: modscript доступен из любой командной строки.
  ReadRegStr $0 HKCU "Environment" "Path"
  ${StrStr} $1 "$0" "$INSTDIR"
  ${If} $1 == ""
    ${If} $0 == ""
      WriteRegExpandStr HKCU "Environment" "Path" "$INSTDIR"
    ${Else}
      WriteRegExpandStr HKCU "Environment" "Path" "$0;$INSTDIR"
    ${EndIf}
    SendMessage ${HWND_BROADCAST} ${WM_SETTINGCHANGE} 0 "STR:Environment" /TIMEOUT=5000
  ${EndIf}

  ; Файлы .modx: открываются в Блокноте, в меню — «Проверить в ModScript X».
  WriteRegStr HKCU "Software\Classes\.modx" "" "ModScriptX.modx"
  WriteRegStr HKCU "Software\Classes\ModScriptX.modx" "" "$(TypeName)"
  WriteRegStr HKCU "Software\Classes\ModScriptX.modx\DefaultIcon" "" "$INSTDIR\modscript.exe,0"
  WriteRegStr HKCU "Software\Classes\ModScriptX.modx\shell\open\command" "" '"$WINDIR\System32\notepad.exe" "%1"'
  WriteRegStr HKCU "Software\Classes\ModScriptX.modx\shell\check" "" "$(CheckVerb)"
  WriteRegStr HKCU "Software\Classes\ModScriptX.modx\shell\check\command" "" '"$INSTDIR\modscript.exe" build "%1"'
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'

  CreateDirectory "$SMPROGRAMS\${NAME}"
  CreateShortcut "$SMPROGRAMS\${NAME}\${NAME}.lnk" "$WINDIR\System32\cmd.exe" '/k set "PATH=$INSTDIR;%PATH%" && modscript' "$INSTDIR\modscript.exe" 0
  CreateShortcut "$SMPROGRAMS\${NAME}\Examples.lnk" "$INSTDIR\Examples"
  CreateShortcut "$SMPROGRAMS\${NAME}\Uninstall ${NAME}.lnk" "$INSTDIR\uninstall.exe"

  WriteUninstaller "$INSTDIR\uninstall.exe"
  WriteRegStr HKCU "${KEY}" "DisplayName" "${NAME}"
  WriteRegStr HKCU "${KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${KEY}" "Publisher" "ModLaunch"
  WriteRegStr HKCU "${KEY}" "DisplayIcon" "$INSTDIR\modscript.exe"
  WriteRegStr HKCU "${KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${KEY}" "UninstallString" '"$INSTDIR\uninstall.exe"'
  WriteRegDWORD HKCU "${KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${KEY}" "NoRepair" 1
SectionEnd

Section "Uninstall"
  ReadRegStr $0 HKCU "Environment" "Path"
  ${UnStrRep} $0 "$0" ";$INSTDIR" ""
  ${UnStrRep} $0 "$0" "$INSTDIR;" ""
  ${UnStrRep} $0 "$0" "$INSTDIR" ""
  ${If} $0 == ""
    DeleteRegValue HKCU "Environment" "Path"
  ${Else}
    WriteRegExpandStr HKCU "Environment" "Path" "$0"
  ${EndIf}
  SendMessage ${HWND_BROADCAST} ${WM_SETTINGCHANGE} 0 "STR:Environment" /TIMEOUT=5000

  DeleteRegKey HKCU "Software\Classes\ModScriptX.modx"
  ReadRegStr $1 HKCU "Software\Classes\.modx" ""
  ${If} $1 == "ModScriptX.modx"
    DeleteRegKey HKCU "Software\Classes\.modx"
  ${EndIf}
  System::Call 'shell32::SHChangeNotify(i 0x08000000, i 0, p 0, p 0)'

  RMDir /r "$SMPROGRAMS\${NAME}"
  Delete "$INSTDIR\modscript.exe"
  Delete "$INSTDIR\README.md"
  RMDir /r "$INSTDIR\Examples"
  Delete "$INSTDIR\uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${KEY}"
SectionEnd
