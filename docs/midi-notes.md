# M-VAVE SMC-Mixer

Notes on the MIDI behavior of the M-VAVE SMC-Mixer, gathered while building the C# library in this repo.
There is no official MIDI implementation chart, so everything below comes from experiments with a real device on
Windows, plus a few third-party sources. Each item says how well it is confirmed.

- **Confirmed** = observed on the device in this project.
- **Inferred** = follows a pattern from confirmed data, not tested directly.
- **Third-party** = from someone else's docs or code, not tested here.

## Layout

The mixer has 8 channel strips, each with a knob on top, a fader, and four buttons: **M**, **S**, **R** and a **square**
button. Along the bottom is a transport row of 11 buttons: `Play`, `Pause`, `Record`, `Rewind`, `Forward`, `«`, `»`,
and four arrows (up, down, left, right). There are also `BT` and `SHIFT` buttons that send no MIDI.

On Windows it shows up as two input ports (`SMC-Mixer`, `MIDIIN2 (SMC-Mixer)`) and two output ports (`SMC-Mixer`,
`MIDIOUT2 (SMC-Mixer)`). LED notes are accepted on the **second** output port, `MIDIOUT2` (confirmed).

## Modes

The mixer has two modes. Switching modes is done on the device, not over MIDI.

| Mode | Faders | Knobs | Buttons | LEDs controllable |
|---|---|---|---|---|
| **DAW mode** (used by this library) | Pitch bend | Relative CC | Notes | **Yes** |
| **CC mode** | Absolute CC | Absolute CC | CC (127/0), overlapping CC numbers | No (see below) |

How to switch:
- **To DAW mode:** SHIFT + the `«` button (confirmed, this is what was done here). The `[>]` LED state indicates the mode.
- **To CC mode:** SHIFT + the `>` button at the bottom right (third-party: the
  [Omnisphere guide](https://support.spectrasonics.net/manual/Omnisphere3HW/3/en/topic/setting-up-the-smc-mixer)
  says the `[>]` LED is lit while CC# mode is active).

Third-party sources also describe a CubeSuite or MidiSuite editor from M-VAVE that can reassign CC numbers and set knobs
to absolute or relative; this was not tried here.

## DAW mode (confirmed unless noted)

Everything is on **MIDI channel 1** unless stated. The scheme is Mackie-style.

| Control | Message | Details |
|---|---|---|
| Fader N (1-8) | Pitch bend on **channel N** | 7-bit steps shifted into 14 bits: values 0, 128, ..., 16256. Full scale is 16256. Fader 1 and 8 tested; 2-7 inferred. |
| Knob N (1-8) | CC **15 + N** (16-23) | **Relative**. `1..63` = clockwise ticks, `65..127` = counter-clockwise (`value - 64`) ticks. Knobs 1 and 8 tested; 2-7 inferred. |
| Buttons | Note On, velocity 127 on press, velocity 0 on release | Notes below. |

### Button notes (input and LED output use the same notes)

| Notes | Buttons | LED color |
|---|---|---|
| 0-7 | **R** of strips 1-8 | Red |
| 8-15 | **S** of strips 1-8 | Blue |
| 16-23 | **M** of strips 1-8 | Orange |
| 24-31 | **Square** of strips 1-8 | White |
| 91 | Rewind | White |
| 92 | Forward | White |
| 93 | Pause | Red |
| 94 | Play | Green |
| 95 | Record | Red |
| 46, 47 | `«`, `»` | No controllable LED |
| 96, 97, 98, 99 | Up, Down, Left, Right | No controllable LED |

Notes on this table:
- The strip rows were found with an LED scan: lighting each note 0-31 in turn lit R 1-8, then S 1-8, M 1-8 and square 1-8, left to right.
- The assignment of 46/47 and 96-99 comes from the order the buttons were pressed, not a labeled test (inferred). They match the Mackie layout.
- The "square" button is called `Select` in the code. The name is a guess.
- `«`, `»` and the arrows light only while held, driven by the mixer itself. They ignore MIDI.

## LED control (confirmed)

- Send **Note On** to the second output port (`MIDIOUT2 (SMC-Mixer)`), channel 1.
- **Velocity 0 = off**, any velocity 1-127 = on. Velocity does **not** select a color; each LED has one fixed color (table above).
- LEDs **latch**: they stay on until you turn them off. Closing the MIDI port can reset them, so keep it open while using them.
- **Note 115** turns all eight M (mute) LEDs on (velocity 127) or off (velocity 0) at once.
- LEDs do nothing in CC mode.
- Scanning other notes in 88-127 found nothing else beyond the notes above, but notes 96-127 were not scanned thoroughly.

## CC mode (partly confirmed)

In this mode every control sends Control Change messages on channel 1, so nothing needs mode-specific decoding by note.
This mode has a serious problem: **button CC numbers overlap knob and fader CC numbers**.

| CC | Control | Status |
|---|---|---|
| 30-37 | Knobs 1-8, **absolute** (0-127) | Confirmed |
| 40-47 | Faders 1-8, absolute (0-127) | Confirmed |
| 20-27 | **M** buttons of strips 1-8 | Strip 3 confirmed (CC 22); rest inferred |
| 28-35 | **S** buttons of strips 1-8 | Strip 3 confirmed (CC 30); rest inferred |
| 36-43 | **R** buttons of strips 1-8 | Strips 1-2 confirmed (CC 36, 37); rest inferred |
| 44-51 | **Square** buttons of strips 1-8 | Strips 1-2 confirmed (CC 44, 45); rest inferred |
| 52-62 | Transport row, left to right: Play, Pause, Record, Rewind, Forward, `«`, `»`, Up, Down, Left, Right | Play/Pause/Record/Rewind confirmed; rest inferred |

Buttons send value **127 on press and 0 on release**. The overlap, for example:
- CC 30 is both Knob 1 and strip 3's **S** button.
- CC 36 and 37 are both Knobs 7 and 8 and strips 1 and 2's **R** buttons.
- CC 40-47 are the faders and also the square/R buttons of several strips.

You can only tell them apart by behavior: a button jumps straight to 127 and then to 0, while a knob or fader moves in small steps. A fader flicked fast to its maximum, or a knob that starts at exactly 127, can be mistaken for a button press. An earlier version of this project used that heuristic. It was removed in favor of DAW mode, where there is no overlap.

Third-party sources say LED feedback does not work reliably in CC mode ("the mixer doesn't respond to Loopy Pro's feedback"). Probing in CC mode here also found no LED response.

## Third-party sources

- [Loopy Pro wiki: SMC Mixer](https://wiki.loopypro.com/SMC-Mixer): describes the two modes (DAW and CC), says LEDs use hard-coded note mappings that are the same in both modes, that faders send pitch bend and encoders a different protocol in DAW mode, and that LED feedback is unreliable. In CC mode the encoder type is set in CubeSuite (`CC` = absolute, `CW` = relative).
- [Omnisphere hardware guide: SMC-Mixer](https://support.spectrasonics.net/manual/Omnisphere3HW/3/en/topic/setting-up-the-smc-mixer): lists the CC-mode numbers for knobs (CC 30-37) and sliders (CC 40-47), plus Up/Down arrows as CC 59/60. That agrees with the inferred transport order here (Up = 52 + 7 = 59, Down = 60), which supports that part of the CC-mode table.
- [degtev/smc_mixer_control](https://github.com/degtev/smc_mixer_control) (`static/devices/smc_mixer.yaml`): a device profile listing LED notes 0-31 in four rows plus transport notes. Its row-to-button assignment did not match the physical layout, so it was only used as a hint. The scan here is authoritative. Its transport notes (Play 94, Pause 93, Record 95, Previous 91, Next 92) were partly wrong: 91/92 are Rewind/Forward on this device.
- [Mixxx manual: M-Vave SMC-Mixer](https://manual.mixxx.org/2.5/en/hardware/controllers/mvave_smc_mixer): says the mixer can connect over Bluetooth, USB-C or both, and recommends USB-C only for Mixxx.

## Not known

- Whether notes other than those above do anything (notes 96-127 were not fully scanned, only to about 99 and note 115).
- Whether fader values, knob ticks or button behavior differ over Bluetooth.
- Whether the mode can be read or set over MIDI (SysEx). No SysEx was found or tried.
- Exact names for the square button. `Select` is a guess.
