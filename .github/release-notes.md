## Download

**Mozaic-{{VERSION}}-win-x64.zip** — unpack it to a folder of your choice (for example
`%LOCALAPPDATA%\Programs\Mozaic`) and run `Mozaic.exe`. A tray icon appears and the shortcut guide opens.
To start Mozaic with Windows, use *Start with Windows* in the tray menu or on the *Startup* page of the settings.

| File | What it is |
|---|---|
| `Mozaic.exe` | The window manager. Native exe, no .NET runtime needed. |
| `MozaicSettings.exe` | Settings app and keyboard shortcut guide. Needs the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) — Windows offers to install it on first start if it is missing. |
| `mozaic-msg.exe` | Command-line control (the `i3-msg` equivalent). |

Requirements: Windows 11, x64. No administrator rights needed.

**"Windows protected your PC"**: the exes are not code-signed, so SmartScreen may warn on first start. Click
*More info* → *Run anyway*.

**First steps**: `$mod` is **Alt**. Hold ``Alt+` `` to see every shortcut on a keyboard (add Shift for the second
layer). `Alt+Enter` opens a terminal, `Alt+d` the launcher, `Alt+F1` lists every shortcut, `Alt+h/j/k/l` moves
focus (add Shift to move the window), `Alt+1…0` switches workspace. Exiting Mozaic (`Alt+Shift+e` or the tray
menu) always shows every window again and makes every window opaque.
