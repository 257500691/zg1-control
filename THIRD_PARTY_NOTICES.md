# Third-party notices

## ktmicro-tools

- Project: <https://github.com/gxcreator/ktmicro-tools>
- Reference commit: `39e620d8cd68c4afb711b91153b4ae91042adc32`
- Copyright (c) 2026, Nikita S.
- License: BSD 3-Clause; see [the complete notice](licenses/ktmicro-tools-BSD-3-Clause.txt).

The KTMicro register mapping, signed half-dB digital gain encoding, PGA tables, EQ field encoding, and noise-gate/limiter parameter definitions informed this Windows adaptation. A copy of the upstream protocol notes is included in `source/KT_USB_PROTOCOL.md` under the same notice.

## devicePEQ

- Project: <https://github.com/jeromeof/devicePEQ>
- Reference: `devicePEQ/ktmicroUsbHidHandler.js`
- Copyright 2024 Jerome O'Flaherty.
- The complete upstream permission and disclaimer are retained in [licenses/devicePEQ-license.txt](licenses/devicePEQ-license.txt).

The KTMicro configuration commit framing (report `0x4B`, address 0, command `0x53`) was cross-checked against this implementation. This application does not invoke its complete EQ-push or clear workflow.

## ZG1 Control adaptation

The C# HID transport, Chinese Windows Forms interface, masked field updates, staged edits, automatic parameter snapshots, conflict detection, readback checks and simulated tests are part of this Windows adaptation. The screenshots show this application's demo mode.

Neither upstream project, its authors, nor the device manufacturer endorses this application. Original upstream license terms remain applicable to their respective material.
