# KT02H20 USB Audio DSP Protocol

## Device
- **VID:PID** = `0x31B2:0x0111` (KT02H20 run mode)
- **Composite**: USB Audio class + HID interface 3
- **Linux devnode**: `/dev/hidrawN`
- **All DSP access via 11-byte HID reports** on HID interface

## HID Packet Format
Every packet is 11 bytes, led by report ID `0x4B`:

### Read Register - `0x52` ('R')
```
TX: 4B  [addr:LE32]  52  00  00  00  00  00
RX: 4B  [addr:LE32]  52  00  [value:LE32]
```
- Used for addresses ≤ 0x100 (all 256 DSP registers, 0x00-0xFF)
- Response echoes address, then 4-byte LE value

### Write Register - `0x57` ('W')
```
TX: 4B  [addr:LE32]  57  00  [value:LE32]
RX: 4B  [addr:LE32]  57  00  [status:LE32]
```
- Device ACKs writes with status `0x03` in response
- Read-back after write confirms value was stored

### Read Extended - `0x08`
```
TX: 4B  [addr:LE32]  08  00  00  00  00  00
RX: 4B  [value:LE32]  ...
```
- Used for addresses > 0x100 (extended space); value returned at bytes 1-4
- App supports a bulk-read loop over consecutive addresses

### Handshake - `0x43` ('C')
```
TX: 4B  00 00 00 00  43  00  00 00 00 00
RX: 4B  ...  [bytes 7-10 = 0x03 or 0x4F]
```
- Sent by the app on connect (OPEN); response validates the device

## Register Map

### Chip Info (strings, LE32 ASCII)
| Addr | Content |
|------|---------|
| 0x00-0x07 | Version (e.g. `"1.0.17"`) |
| 0x01 | **Protocol** version / model code — **bit9 (0x0200) = single-DAC model** (app hides DACL/DACR, shows one "DAC" control) |
| 0x08-0x0D | Date string |
| 0x40-0x47 | USB Manufacturer string (writable via Config tab) |
| 0x48-0x4F | USB Product name string (writable) |
| 0x50-0x56 | USB Serial number string (writable) |
| 0x5B | `[PID:16][VID:16]` packed — USB VID/PID (writable) |
| 0xE1 | Magic: 0x12345678 (device-side, not used by the app) |

### Volume (PGA + Digital Gain)
| Addr | Description |
|------|-------------|
| 0x3A | A_ADC analog PGA gain — index into {0, -6, 8, 14, 20, 26, 32, 44 dB} |
| 0x3B | A_DAC analog PGA gain — index: 0=mute, i=1..15 → (1.5·(i−1) − 18) dB |
| 0x65 | DIG_ADC digital gain, byte0 = signed gain_dB × 2 (0.5 dB steps) |
| 0x66 | Digital DAC gain (model-dependent, see flags 0x01 bit9): single-DAC models — byte0 = DIG_DAC; stereo models — byte0 = DIG_DACL, byte1 = DIG_DACR |

A_DAC table: mute, −18, −16.5, −15, −13.5, −12, −10.5, −9, −7.5, **−6 (idx 9)**,
−4.5, −3, −1.5, 0, +1.5, +3 dB.
Digital gain bytes: signed, value = round(gain_dB × 2), e.g. 0 dB → 0x00, −6 dB → 0xF4.
Sliders mirror the spinboxes (slider = dB × 10).

### EQ Filters
Each channel block spans 12 registers: enable at base+0, 5 bands (2 regs each) at base+2.
**ADC EQ:** enable = 0x18 (bit0), bands = 0x1A-0x23 — band N = (0x1A + N*2)
**DAC EQ:** enable = 0x24 (bit0), bands = 0x26-0x2F — band N = (0x26 + N*2)

Each band uses 2 registers, encoding format:
```
A_reg (32-bit):  [freq_Hz:16]  [gain_×10:16]
                 hi16 = frequency in Hz, directly
                 lo16 = signed gain × 10  (e.g., 0x0021 = +3.3 dB)

B_reg (32-bit):  [type:3] [reserved:13]  [Q×1000:16]
                 bits 16-18 = filter type (0=P, 1=LPF, 2=HPF, 3=LS, 4=HS)
                 bits 0-15  = Q × 1000  (e.g., 0x02C3 = 0.707)
```

### DRC (Dynamic Range Control)

The app's DRC page has 5 submodules: NoiseGate, Limiter, Compander (Compressor),
Expander, AGC. **Only NoiseGate and Limiter are written to the device** — the
Compander/Expander/AGC UI handlers are empty stubs (curve graph only), so their
device registers (if any) are unknown. App writes:
**0x71-0x73:** NoiseGate (live)
**0x78-0x79:** Limiter (live)

#### NoiseGate (0x71-0x73)
```
0x71 = [EN+flags:8]  [GateVol:8]  [THhigh:8]  [THlow:8]
       byte3 bit7 = EN (1=ON)
       byte3 bits 0-6 = flags (value 0x3C)
       byte2 = 256 + GateVol_dB      (range: -256 .. -1 dB, typical -60..0)
       byte1 = 256 + THhigh_dB       (same encoding)
       byte0 = 256 + THlow_dB        (same encoding)

0x72 = [RT_ms:16]  [AT_ms:16]
       hi16 = Release time (ms)
       lo16 = Attack time (ms)

0x73 = [noiseT_ms:16]  [hold:16]
       hi16 = Noise gate time (ms)
       lo16 = 0x000A (hold constant)
```

#### Limiter (0x78-0x79)
```
0x78 = [EN+SOFT:8]  [reserved:16]  [threshold:8]
       byte3 bit 7 = EN (1=ON)
       byte3 bit 6 = SOFT knee (1=ON)
       byte0 = 256 + threshold_dB   (e.g., 0xFA=250 → -6 dB)

0x79 = [RT_ms:16]  [AT_ms:16]
       hi16 = Release time (ms)
       lo16 = Attack time (ms)
```

## Python Tool
```bash
python3 ktmicro.py state           # full DSP state
python3 ktmicro.py ng              # NoiseGate read/set
python3 ktmicro.py lim             # Limiter read/set
python3 ktmicro.py vol             # Volume read/set (PGA + digital gains)
python3 ktmicro.py eq <adc|dac>    # EQ bands read/set, --enable/--disable
python3 ktmicro.py read <addr>     # raw register read
python3 ktmicro.py write <addr> <val>  # raw register write
python3 ktmicro.py dump            # dump all 256 registers
```

