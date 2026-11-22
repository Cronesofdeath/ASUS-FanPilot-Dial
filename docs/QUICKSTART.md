# ASUS FanPilot Dial v1.0.0 Experimental

**Credit to Karmel0x.** FanPilot builds on AsusFanControl, which showed how to read fan speeds and set PWM duty through the ASUS diagnostic interface. [Original AsusFanControl project](https://github.com/Karmel0x/AsusFanControl)

Numbered Fan 1 / Fan 2 curves for Silent, Balanced and Turbo cooling profiles. G-Helper remains responsible for the laptop's performance mode, power limits and other settings. FanPilot only sends fan-control commands; it does not write G-Helper's configuration or call performance-mode/power-setting APIs.

Extract the full release ZIP before opening **ASUS FanPilot Dial.exe**. Exit any other fan-control utility that is actively overriding fan speeds. FanPilot stores preferences and calibration in your Windows user profile. For a hardware-free preview, launch the EXE with `--demo`. Demo settings are separate.

FanPilot uses its own settings and an exclusive hardware lock to keep two FanPilot workers from controlling the fans at the same time.

## Use with G-Helper

1. Keep normal G-Helper running. Open **ASUS FanPilot Dial.exe**. Startup always leaves fan control off.
2. Use the dashboard dropdown to select **Follow G-Helper** or pick **Silent**, **Balanced**, or **Turbo** directly. **Follow G-Helper** uses G-Helper's saved performance mode.
3. Open **Edit curves**. Use the **Silent / Balanced / Turbo** tabs to choose a profile. Drag points on either fan graph, or select a point and open **Edit point…** to enter exact values. The header distinguishes the current profile from a preview. Editing a tab does not switch the laptop's performance mode.
4. For the current profile, click **Start curve**. For a future profile, click **Save for Silent**, **Save for Balanced**, or **Save for Turbo**; it is stored without changing the current fan behavior. **Apply changes** updates a running current profile.
5. While curve control is running, a confirmed G-Helper mode change selects that mode's saved curves. Switching modes while fan control is off leaves it off.

To use a different cooling profile while keeping G-Helper in Balanced, choose the desired cooling profile in FanPilot's dashboard, then start its curve. The dashboard shows laptop performance and FanPilot cooling side by side. Selecting Turbo *cooling* here never requests Turbo *performance* from G-Helper.

Your existing curve is copied into three independent profiles during migration; no new untested thermal presets replace it. Edit the copies as desired. Profile points, fan shift and response delays are independent. Shared fan control defaults on: both fans follow the hotter component and the higher relative curve demand. No CPU/GPU fan assignment or confirmation is needed. Turn shared control off to choose a temperature source independently for each numbered fan; these choices belong to the profile. The existing measured RPM scale and keyboard shortcut are preserved. Switching tabs keeps drafts in memory. Use **Save for Silent**, **Save for Balanced**, or **Save for Turbo** or **Apply changes** to commit edits. Automatic mode changes use the last saved curve for that mode.

## What mode following reads

FanPilot reads only `performance_mode` from `%APPDATA%\GHelper\config.json`, and requires G-Helper to be running. Balanced=0, Turbo=1 and Silent=2 match G-Helper's published identifiers. This is its saved selection, not independent firmware telemetry. Other utilities changing firmware modes without updating G-Helper's config cannot be detected by this reader. Unsupported/custom mode identifiers are reported as unavailable.

During active control, two consecutive readings confirm a changed mode. Three unavailable readings stop a linked curve and return control to firmware. Unexpected mode-reader errors also stop linked curve control. Source recovery never automatically re-enables fans. A mode change during maximum-RPM measurement cancels that measurement and resets control. Fixed PWM mode retains its shared percentage across performance-mode changes.

## Saved presets

Select a preset from the dropdown to load it into the current draft. Click **Save as preset…** to save a named copy. **Update** replaces the selected preset after confirmation. **Rename** preserves its curve. **Delete** asks for confirmation. Preset changes are saved locally and take effect on the fans when you start or apply the draft. The first successful edit of the preset library imports any bundled personal presets into local settings, where they can be edited or deleted.

The two sliders under **Temperature preview** set hypothetical CPU and GPU temperatures. Each fan shows requested PWM and estimated RPM. The graph headers show measured live RPM. Response delays and thermal protections affect the real output.

## Curves and RPM

The curve axis uses estimated RPM derived from the measured maximum of each fan. Drag a dot, or select a point and enter its temperature/RPM before clicking **Update point**. Actual hardware output remains bounded 40–100% PWM, so actual RPM can differ from the plotted estimate. No artificial RPM margin or device-specific RPM ceiling is added. An observed maximum is not a manufacturer-rated maximum.

Fan curve calibration requires a readable NVIDIA GPU temperature. Fixed PWM is available on compatible ASUS hardware without it. The RPM editor requires a valid measured scale. Existing calibration is reused. **Advanced tuning → RPM calibration → Measure max RPM** starts an explicit full-duty measurement, bounded to 90 seconds, with temperature/stall monitoring and firmware reset before publication. It uses three-reading medians and waits for a 15-second plateau, rather than the old thirteen-step sweep. Cancellation/failure retains the previous scale. Start requires CPU <70 °C and GPU <65 °C; CPU ≥75 °C, GPU ≥70 °C or sensor loss aborts measurement. Calibration does not run automatically.

**Starting a curve no longer forces full speed.** It uses the target calculated from current temperatures immediately. Subsequent changes use the profile's up/down response delays and rate limits. Full PWM remains available when the curve requests it, during explicit calibration, and for thermal intervention.

In shared control, the hotter CPU/GPU temperature is used automatically. In independent control, each fan follows its selected source, and fan shift blends that temperature toward the hotter component. The worker evaluates and refreshes fan commands about once per second independently of the window. Both actual fan RPMs remain visible.

## Stop and recovery

Click **Stop curve** in the editor or use the dashboard’s **Stop curve** or **Stop fixed PWM** action to return to firmware control. Minimizing keeps control running. Settings and the curve editor are independent taskbar windows with their own minimize and close buttons. Minimizing or hiding the dashboard leaves them open; exiting the application closes all windows. The tray menu’s **Exit FanPilot** waits for a confirmed firmware reset. If the helper disconnects before acknowledgment, use **Retry reset** to reconnect and request firmware control for every fan. **Exit anyway** leaves the warning in place for the next launch. CPU ≥90 °C or GPU ≥85 °C immediately requests full cooling. The curve resumes automatically once CPU is ≤85 °C and GPU is ≤80 °C, when available. CPU ≥95 °C or GPU ≥90 °C for five seconds after full duty has been commanded triggers a firmware handoff. CPU ≥98 °C or GPU ≥93 °C triggers an immediate handoff. After a handoff, restart the curve explicitly. The dashboard shows **Firmware reset unconfirmed** until the driver acknowledges the reset. Invalid sensors, persistent stalls and rejected driver writes trigger reset attempts. GPU loss stops curve control unless CPU fallback was explicitly selected.

A driver acknowledgment does not independently prove physical firmware state. Driver/kernel hangs, forced termination and power loss can defeat software recovery. The automated suite does not establish thermal safety, universal compatibility or BSOD immunity. Automated release testing uses simulated fans. The project targets compatible ASUS laptops using the supported library. The FX506LH is the development device. Each additional hardware combination needs verification of sensor readings, fan response and firmware handoff.

## Appearance

Open **Settings → General → Palette** to choose Carbon, Midnight Blue, Sandstone, or Graphite. The selection applies across Dial windows and the tray menu. **Animations** controls gauge motion and the Advanced tuning panel transition.

## Start with Windows

**Start with Windows** opens FanPilot in the tray using your Windows account. Opening the dashboard or using the fan-control shortcut starts the hardware helper and may show a UAC prompt. If you previously enabled the scheduled task, FanPilot offers to remove it and preserve your startup preference.

## Requirements and files

Windows 10/11 x64, .NET Framework 4.7.2+, compatible ASUS System Control Interface, and hash-verified local ASUS library / Microsoft PsExec dependencies. Vendor binaries are not bundled. For a fresh setup, use the included dependency script and the supported files described in the third-party notices. Experimental settings/logs/startup files use `%LOCALAPPDATA%\FanPilotDial`. NVIDIA NVML currently supplies GPU temperature; Radeon/Intel providers are not implemented. No internet is required after setup.

Built on the diagnostic/PWM approach in [Karmel0x/AsusFanControl](https://github.com/Karmel0x/AsusFanControl), with its MIT notice retained. G-Helper is a separate application; neither it nor its drivers are embedded. See the included third-party notices. Automated fan operations use the simulator.
