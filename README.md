# NetworkTrayAppWpf
Aims to reimplement Windows 11's network tray app

<img width="293" height="187" alt="Screenshot 2025-12-24 191002" src="https://github.com/user-attachments/assets/6ba69c6d-81c4-40ce-a75f-6e21ccc6cf55" />
<img width="403" height="267" alt="Screenshot 2025-12-24 191103" src="https://github.com/user-attachments/assets/ad9d6d9d-e91a-41aa-925e-04d0e2665b85" />
<img width="200" height="400" alt="Screenshot 2025-12-24 191139" src="https://github.com/user-attachments/assets/16922573-40a0-4f97-ad54-fe73b145dcf6" />

This project is meant to be paired with [Windhawk's](https://github.com/ramensoftware/windhawk) "Taskbar tray system icon tweaks", which can be used to remove the real network icon (along with all the other "trapped" icons).

Bonus points for running this alongside [EarTrumpet](https://github.com/File-New-Project/EarTrumpet)

This project was a last resort to get around the hellacious "Quick Settings" flyout in Windows 11. I tried everything to reroute or suppress that flyout without deliberately patching the relevant dll's. It was originally written in C++, but I couldn't get the glyph-sourced tray icons to render crisply. I did enjoy the 8MB total worth of RAM that app used though.. I also had this written up in WinUI 3, but packaging and distribution resulted in a ~250MB wad of junk, so I ended up with WPF.

Among other reasons, I have the default flyout set to the old Winodws 10 flyout because it doesn't break context menu's for tray apps. This is an existing bug in Windows 11. Tray context menus will render behind the taskbar after transitioning from a "Windows 11" style flyout.
