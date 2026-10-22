# Controlling Home Assistant lights with the SMC-Mixer

`SmcMixer.HomeAssistant` is a Linux worker that turns the mixer into a physical control panel for Home Assistant
lights. It reads the mixer over MIDI (Bluetooth or USB), and calls Home Assistant's REST API.

By default (see [`appsettings.json`](../SmcMixer.HomeAssistant/appsettings.json)):

| Control | Does |
|---|---|
| Fader | Sets the light's brightness. The bottom of the fader's travel switches the light off. |
| Knob | Steps the light's color temperature, 100 K per click. |
| Button | Toggles the light on and off. |

It reconnects by itself when the mixer disconnects and comes back, and a light that is unavailable only produces a warning in
the log, it never stops the worker.

Contents: [Requirements](#requirements) · [1. Connect the mixer](#1-connect-the-mixer) ·
[2. Create a Home Assistant token](#2-create-a-home-assistant-token) · [3. Configure](#3-configure) ·
[4. Try it](#4-try-it) · [5. Deploy as a service](#5-deploy-as-a-service) · [Behavior](#behavior) ·
[Troubleshooting](#troubleshooting)

## Requirements

- A Linux machine with ALSA (`libasound2`, on most desktops and servers) and, for Bluetooth, BlueZ 5.50 or newer.
  This is usually the machine that runs Home Assistant, but it doesn't have to be.
- The [.NET 10 SDK](https://dotnet.microsoft.com/download) to build it. The published worker needs the .NET 10 runtime.
- Home Assistant reachable from that machine, and permission to create a long-lived access token.
- Your user in the `audio` group, which owns `/dev/snd/seq`: `sudo usermod -aG audio $USER`, then log out and in.
- The mixer in **DAW mode**: hold SHIFT and press the `«` button. In the other mode (CC mode) the controls send different
  messages and nothing will match.

## 1. Connect the mixer

### Bluetooth

Turn on the mixer's Bluetooth (the `BT` button), then pair it once with `bluetoothctl`:

```
bluetoothctl
[bluetooth]# scan on
```

Wait for a device named `SMC-Mixer` and note its address. To tell it from the many other devices around, scan only for
BLE-MIDI devices:

```
[bluetooth]# menu scan
[bluetooth]# uuids 03B80E5A-EDE8-4B33-A751-6CE34EC4C700
[bluetooth]# back
[bluetooth]# scan on
```

Then, using its address:

```
[bluetooth]# scan off
[bluetooth]# pair AA:BB:CC:DD:EE:FF        # answer "yes" when it asks "Accept pairing"
[bluetooth]# trust AA:BB:CC:DD:EE:FF       # so it reconnects on its own
[bluetooth]# connect AA:BB:CC:DD:EE:FF
```

Pairing is needed: without it the mixer hangs up as soon as the connection is up. Because the device is trusted, turning the
mixer off and on again reconnects it without any further steps.

### USB

Plug it in. Nothing else is needed. USB exposes two MIDI ports; the worker finds them by name.

### Check it

With the mixer connected, list the MIDI ports and watch the controls from the console app:

```
aconnect -l                                 # a client named SMC-Mixer should be listed
dotnet run --project SmcMixer               # move something: "Fader 1  46%", "Knob 3  +1", "Mute 2  pressed"
```

The names printed here are the ones used in the configuration below. If the controls appear as `(unmapped)`, or nothing
appears, see [Troubleshooting](#troubleshooting).

## 2. Create a Home Assistant token

1. In Home Assistant, open your profile (bottom of the sidebar) and the **Security** tab.
2. Under **Long-lived access tokens**, choose **Create token**, name it `smc-mixer` and copy it. It is shown only once.
3. Save it in a file only you can read. This prompt keeps it out of your shell history:

```
mkdir -p ~/.config/smc-ha && umask 077 && read -rsp "Paste token: " T && printf '%s' "$T" > ~/.config/smc-ha/token && unset T && echo
```

Check it works (this should print `200`):

```
curl -s -o /dev/null -w "%{http_code}\n" -H "Authorization: Bearer $(cat ~/.config/smc-ha/token)" http://localhost:8123/api/
```

## 3. Configure

Everything is in [`SmcMixer.HomeAssistant/appsettings.json`](../SmcMixer.HomeAssistant/appsettings.json), section
`HomeAssistant`:

```json
"HomeAssistant": {
  "BaseUrl": "http://localhost:8123",
  "TokenFile": "~/.config/smc-ha/token",
  "MinCallIntervalMs": 100,
  "KelvinPerTick": 100,
  "Mappings": [
    { "Control": "Fader 1", "Action": "Brightness",       "Entity": "light.bedroom" },
    { "Control": "Knob 1",  "Action": "ColorTemperature", "Entity": "light.bedroom" },
    { "Control": "Mute 1",  "Action": "Toggle",           "Entity": "light.bedroom" }
  ]
}
```

| Setting | Default | Meaning |
|---|---|---|
| `BaseUrl` | `http://localhost:8123` | Where Home Assistant is. |
| `TokenFile` | `~/.config/smc-ha/token` | File holding the long-lived token. A leading `~` is your home directory. |
| `MinCallIntervalMs` | `100` | Faders and knobs send dozens of messages a second. Each light gets at most one Home Assistant call per this interval, always with the latest value. |
| `KelvinPerTick` | `100` | How far one knob click moves the color temperature. |
| `Mappings` | | Which control drives which light. Add as many as you like, including several controls for one light. |

### Mappings

Each mapping has a `Control`, an `Action` and a light `Entity`.

| Action | Use with | What it does |
|---|---|---|
| `Brightness` | A fader | Brightness 1-255. Below about 2% of the fader's travel, the light is switched off. |
| `ColorTemperature` | A knob | Steps the color temperature by `KelvinPerTick` per click, within the bulb's own range. |
| `Toggle` | A button | Toggles the light when the button is pressed (not on release). |

An action paired with the wrong kind of control (a `Brightness` on a button, say) is ignored.

**Control names** are matched without regard to case:

| Name | Controls |
|---|---|
| `Fader 1` ... `Fader 8` | The eight faders |
| `Knob 1` ... `Knob 8` | The eight endless knobs |
| `Mute 1` ... `Mute 8` | The **M** button of each strip |
| `Solo 1` ... `Solo 8` | The **S** button of each strip |
| `Rec 1` ... `Rec 8` | The **R** button of each strip |
| `Select 1` ... `Select 8` | The square button of each strip |
| `Play`, `Pause`, `Record`, `Rewind`, `Forward` | Transport buttons |
| `Prev`, `Next` | The `«` and `»` buttons |
| `Up`, `Down`, `Left`, `Right` | The four arrow buttons |

**Finding a light's entity id:** in Home Assistant, open *Developer tools*, *States* and filter for `light.`. Or from a
terminal:

```
curl -s -H "Authorization: Bearer $(cat ~/.config/smc-ha/token)" http://localhost:8123/api/states \
  | python3 -c "import json,sys; [print(e['entity_id'], e['state']) for e in json.load(sys.stdin) if e['entity_id'].startswith('light.')]"
```

### Using HTTPS

If Home Assistant sits behind a reverse proxy with its own certificate authority (Caddy's internal CA, for example),
`https://...` will fail with a certificate error until this machine trusts that CA. When the worker runs on the same
machine as Home Assistant, the simplest option is `http://localhost:8123`.

## 4. Try it

Run the worker in the foreground with the mixer connected:

```
dotnet run --project SmcMixer.HomeAssistant
```

You should see `Connected to SMC-Mixer Bluetooth`. Move a fader and the light follows. Stop it with Ctrl+C.

## 5. Deploy as a service

A systemd *user* service starts the worker at boot, restarts it if it crashes, and lets it wait for the mixer.

```
dotnet publish SmcMixer.HomeAssistant -c Release -o ~/smc-ha
mkdir -p ~/.config/systemd/user
cp SmcMixer.HomeAssistant/smc-ha.service ~/.config/systemd/user/
sudo loginctl enable-linger $USER          # run at boot, without anyone logged in
systemctl --user daemon-reload
systemctl --user enable --now smc-ha.service
```

The unit runs `dotnet ~/smc-ha/SmcMixer.HomeAssistant.dll`, so it uses the **published copy** in `~/smc-ha`, not the
source tree.

Day to day:

```
systemctl --user status smc-ha        # is it running?
journalctl --user -u smc-ha -f        # follow the log
systemctl --user restart smc-ha       # after changing the configuration
```

**To change the configuration**, edit `appsettings.json`, then publish again and restart:

```
dotnet publish SmcMixer.HomeAssistant -c Release -o ~/smc-ha && systemctl --user restart smc-ha
```

(Or edit `~/smc-ha/appsettings.json` directly and restart, which is quicker but will be overwritten by the next publish.)

**To remove it:**

```
systemctl --user disable --now smc-ha.service
rm ~/.config/systemd/user/smc-ha.service && rm -r ~/smc-ha
```

## Behavior

- **Faders aren't motorized.** If you change a light from the app or elsewhere, the fader stays where it was, and the light
  jumps to the fader's position the next time you touch it.
- **Knobs are relative.** A knob starts from the light's current color temperature when the worker starts and then counts
  clicks, staying within the bulb's range.
- **Turning a knob on a light that is off switches it on** at the new temperature, because that is what Home Assistant does
  for a color change.
- **The worker doesn't drive the mixer's LEDs.** Over Bluetooth the mixer offers a single MIDI port, so LED feedback isn't
  something this relies on.
- **If a light or Home Assistant is unavailable,** the failed call is logged as a warning and later calls work again as soon
  as it is back.
- **If the mixer disconnects** (switched off, out of range, Bluetooth drops), the log shows `Mixer disconnected, waiting for
  it to come back`, and `Connected to ...` when it returns.

## Troubleshooting

**`Permission denied` on `/dev/snd/seq`**
Your user must be in the `audio` group: `sudo usermod -aG audio $USER`. A new login picks it up. For the *service* there is
one more catch: `systemd --user` is started once and keeps running, so it has the groups from when it started. If you added
yourself to the group after that, restart it:

```
sudo systemctl restart user@$(id -u).service
```

You can check with `grep Groups /proc/$(pgrep -u $USER -x systemd | head -1)/status`; the list must include the id of
`audio` (`getent group audio`).

**Nothing happens when I move a control**
Run `dotnet run --project SmcMixer` with the worker stopped and look at what is printed.
- Controls print as named lines (`Fader 1 ...`): the mixer works, so check the `Control` and `Entity` names in `appsettings.json`.
- Lines say `(unmapped)`: the mixer is in CC mode. Hold SHIFT and press `«` to switch to DAW mode.
- Only `Midi Through Port-0` is listed: the mixer isn't connected. Run `bluetoothctl connect <address>`, and check it is
  switched on and not connected to a phone or another computer.

**The log says `Waiting for the SMC-Mixer to connect`, and `Connected` never follows**
The same as above: the mixer isn't connected. The worker keeps trying every few seconds and connects when it appears.

**The mixer connects, then hangs up straight away**
It wasn't paired. Pair it as described in [Bluetooth](#bluetooth), and answer `yes` to the pairing prompt. This has to be
typed into a real `bluetoothctl` session; a script that pipes commands can't answer it.

**`Home Assistant call for light.xyz failed: ... 401 (Unauthorized)`**
The token is wrong or was deleted. Create a new one and save it as in [step 2](#2-create-a-home-assistant-token).

**The worker logs no errors, but the light doesn't react**
Check the entity id as described in [Mappings](#mappings). Home Assistant accepts a call for a light that doesn't exist and
only mentions it in its own log (*Settings*, *System*, *Logs*). The light itself may also be `unavailable` in Home
Assistant, for example switched off at the wall.

**`... failed: The SSL connection could not be established`**
`BaseUrl` is an `https://` address whose certificate this machine doesn't trust. See [Using HTTPS](#using-https).

**`Home Assistant token file not found`**
The path in `TokenFile` doesn't exist for the user the service runs as. The service runs as you, so `~` is your home.

**The service starts, but not at boot**
Lingering must be on: `loginctl show-user $USER -p Linger` should print `Linger=yes`; if not, run
`sudo loginctl enable-linger $USER`.
