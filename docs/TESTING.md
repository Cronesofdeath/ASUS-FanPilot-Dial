# Tests

Run `./test.ps1` in PowerShell on Windows with an interactive desktop. Use `-Suites Unit`, `-Suites Integration` or `-Suites Ui` for targeted runs; `-SkipBuild` reuses the current application. Outputs stay in ignored `bin` and `test-output` directories.

Unit tests inject fan and GPU adapters and advance a synthetic clock. They cover interpolation, constraints, response timing, separate mappings, sensor loss, cooling intervention, partial failures, calibration completion/cancellation/timeouts and table validation. Calibration tests cover full-duty maximum-RPM measurement, settling, timeout and cancellation.

Process tests start a simulated bridge, authenticate a real named pipe, verify that curves advance without UI polling, and exercise disconnect, heartbeat expiry, parent exit and calibration cancellation. Native ASUS imports are not loaded. UI tests use the demo worker and check native window controls, tray, keyboard chords, flicker, curve editing, mapping validation, apply/off, calibration cancel, persistence and calibrated graph axes. Screenshots are rendered to `test-output/ui`.

These tests establish software behavior under simulated conditions. They do not certify a laptop/driver combination, fan rated maximum, thermal protection or BSOD immunity. Real thermal/load and crash recovery testing requires supervised device-specific validation and has not been run by this automated suite.

Hosted Windows CI builds the app, runs the unit and bridge suites, and packages the result. Run the full desktop suite in an interactive session before preparing a release.
