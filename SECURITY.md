# Security and hardware scope

Hardware access runs as SYSTEM through Microsoft PsExec. SID-restricted named pipes and a random session token limit access. ASUS and PsExec binaries are hash-pinned. These checks and the test suite do not constitute a security audit.

Start with Windows registers a per-user startup entry. FanPilot opens in the tray and requests hardware access when you open the dashboard or use the toggle shortcut. The old elevated task is removed during startup preference migration. For shared deployments, keep elevated executables and dependencies in directories writable only by trusted administrators. There is no protected installer or signing certificate in this experimental build.

CPU and NVIDIA GPU temperature checks do not cover VRM, battery or every hotspot. CPU-only fallback cannot independently monitor GPU heat. The diagnostic interface and firmware behavior depend on the device/driver. A kernel crash, driver hang, power loss or forced termination can defeat software recovery. Use manufacturer-supported controls when compatibility is uncertain. Calibration does not measure electrical or mechanical rated fan limits.

Never post authentication tokens or unreviewed personal logs publicly. Report vulnerabilities privately to the actual repository maintainer, if that repository provides a private reporting channel.
