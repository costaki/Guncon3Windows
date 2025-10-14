# Guncon3Windows

> Use **GunCon 3** on Windows — Calibration tool integrated

✅ Stable Release (v1.0.0)

---

## Overview
This fork provides a **fully working implementation** of GunCon 3 for Windows, with built-in calibration, digitalized analog stick, and hot recalibration support.

This version is **ready to use out of the box** — no manual setup or coding required.

---

## Features
- 🔧 **Automatic calibration on first launch**
- 🎯 **Hot recalibration** anytime with **F12**
- 🧭 **Five-point calibration system** (four corners + center)
- 🎮 **Digitalized analog stick** (LUp / LDown / LLeft / LRight)
- 🖱️ **RawInput / tether feeders**
- 💾 **Local configuration files** (`mapping.txt`, `calibration_rect.txt`)
- 🖥️ Works with most PC arcade and emulator lightgun setups

---

## Downloads
➡️ Get everything from the **[Releases](https://github.com/gameotaku79/Guncon3Windows/releases/latest)** page:

- 🕹️ `Guncon3Console.zip` – main executable  
- 🔌 `Guncon3.Windows.driver.zip` – GunCon 3 Windows driver  
- 🧩 `tether.setup.exe` – TetherScript HID Virtual Driver Kit (archived)

---

## Requirements
1. Install **GunCon 3 Windows Driver** (included in Releases).  
2. Install **TetherScript HID Virtual Driver Kit** *(archived, discontinued)* — run as Administrator and reboot.  
3. Launch **Guncon3Console.exe**  
   - The app will **auto-calibrate on first run**.  
   - You can recalibrate anytime with **F12** (no restart required).

---

## Button mapping
Button and stick mappings are defined in the text file **`mapping.txt`**, located next to the executable.

Each line follows the format  
`DEVICE.COMMAND = GUNCOMMAND`  
(e.g. `KEYBOARD.30 = C1`, `MOUSE.Left = Trigger`).

To see all available keyboard key codes, run:

```
Guncon3Console.exe keys
```

This will list every possible key and its numeric code (for example, `Keyboard.33` = `F`).

You can edit `mapping.txt` manually to change your key assignments.

A copy of all keycode mappings is also included in **`keycodes.txt`** for convenience.

See also: [keycodes.txt](docs/keycodes.txt)

---

## Notes
- Recalibrate after changing monitor or resolution.  
- Multi-monitor setups are supported.  
- All code and binaries licensed under **GPL-2.0**, inherited from the original work by [sonik-br](https://github.com/sonik-br/GunconUSB).

---

© 2025 gameotaku79  
Fork of [sonik-br/GunconUSB](https://github.com/sonik-br/GunconUSB)
