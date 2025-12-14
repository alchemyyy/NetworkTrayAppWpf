# NetworkTrayAppWpf
Aims to reimplement Windows 11's network tray app

This project is meant to be paired with [Windhawk's](https://github.com/ramensoftware/windhawk) "Taskbar tray system icon tweaks", which can be used to remove the real network icon (along with all the other "trapped" icons).

Bonus points for running this alongside [EarTrumpet](https://github.com/File-New-Project/EarTrumpet)

Starting this program automatically is left to the end user. I personally have the executable living in my startup folder (run->shell:startup).

This project was a last resort to get around the hellacious "Quick Settings" flyout in Windows 11. I tried everything to reroute or suppress that flyout without deliberately patching the relevant dll's. 

This project was originally written in C++, but I couldn't get the glyph-sourced tray icons to render crisply. I did enjoy the 8MB total worth of RAM that app used though..

I also had this written up in WinUI 3, but packaging and distribution resulted in a ~250MB wad of junk, so I ended up with WPF.

## TODO's and Limitations

I don't have layered icons working yet, which is seemingly how Windows 11 renders the "missing" signal bars (for example, on poor Wifi signal), and how it performs the Wifi connecting animation. Note, apparently these icons are made by underlaying another glyph at 30% brightness to the normal glyph.

The speed at which the flyouts show up is not me, its a Windows limitation due to lazy-loading. I tried a couple solutions for precaching the flyouts, but they didn't help.

Among other reasons, I have the default flyout set to the old Winodws 10 flyout because it doesn't break context menu's for tray apps. This is also not me. It is an existing bug in Windows 11. Tray context menus will render behind the taskbar after transitioning from a "Windows 11" style flyout.