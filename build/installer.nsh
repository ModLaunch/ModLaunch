; Дополнения к установщику ModHub (NSIS, electron-builder).
;
; До 2.0 ModHub ставился своим окном установки и записывал себя в
; «Приложения» Windows под ключом ModHub. Новый установщик пишет свой ключ,
; поэтому старый убираем, чтобы в списке не было двух ModHub.

!macro customInstall
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\ModHub"
  Delete "$INSTDIR\.modhub-install.json"
  ; 2.2: программа стала ModLaunch — старые ярлыки и exe с прежним именем убираем.
  Delete "$DESKTOP\ModHub.lnk"
  Delete "$SMPROGRAMS\ModHub.lnk"
  Delete "$INSTDIR\ModHub.exe"
!macroend
