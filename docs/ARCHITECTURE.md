# Experimental architecture

The WPF UI owns preferences, profile selection and mode-source observation. The SYSTEM worker owns all ASUS sensor reads, curve calculations, checked writes and reset attempts. A SID-restricted pipe and per-session token authenticate commands; a shared mutex prevents normal/experimental fan workers running together.

`ModeProfiles` stores cloned per-mode curve settings and per-profile shared/independent response sources. Preferences migrate legacy curves into three independent copies. `GHelperModeSource` performs bounded, read-only JSON reads and checks for a running GHelper process. It never changes G-Helper, firmware performance modes or CPU/GPU power settings.

`MainWindowController` coordinates dashboard controls and profile changes. `ModeTracker` confirms a changed mode after two readings during active control and marks the source unavailable after three missed readings. A missing source or unexpected error stops linked curves. Turning following off enables manual cooling selection. An off controller never re-arms on a mode change or source recovery. Mode changes cancel calibration.

`FanSession` owns the worker connection, serializes commands through shutdown, and records pending firmware reset before requesting manual control or calibration. If the connection is lost, a fresh worker must acknowledge an explicit reset before the record can clear. The dashboard uses the session state to present errors and recovery actions.

`CurveEditorController` separately tracks the profile being edited. Drafts are kept per mode in memory. Explicit profile saves update committed curves, and mode switches read only those committed curves. Loading a preset clones its curve into the draft. PresetLibrary validates and persists library mutations and rolls back on a failed write. An optional local personal-curves.json file stays outside the packaged release. Current-mode changes use CURVE and preserve the existing ramp state; initial ENABLECURVE begins at calculated demand rather than full PWM. The worker's monotonic timer remains independent of UI polling.

`MaximumRpmMeasurement` estimates full-duty peaks using median readings and a bounded plateau timer. RPM graphs remain estimates; output is PWM. Thermal intervention, stalls, sensor loss, rejected calls and reset acknowledgments remain under `FanController`. Cached GPU calls never block the fan thread.

`DesktopTheme`, `CurveChart`, `FanTile` and `TrayMenuRenderer` own rendering. WPF loads the embedded Theme.xaml resource dictionary once at the application level. Dashboard, editor and settings resolve the same control styles. No browser runtime is added. Public packages exclude vendor binaries, test executables, logs and local reference material.

Fan control uses fixed numbered indices rather than inferred physical CPU/GPU identities. Older identity fields are retained only for settings compatibility. Shared control requests the higher relative demand for every fan; independent control uses the chosen sensor for Fan 1 and Fan 2. Secondary windows have no dashboard owner; each has its own taskbar presence and frame controls. Global application exit disposes them explicitly.
