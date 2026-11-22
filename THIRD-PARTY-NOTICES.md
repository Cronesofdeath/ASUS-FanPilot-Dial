# Third-party components

## Original project

[Karmel0x/AsusFanControl](https://github.com/Karmel0x/AsusFanControl), copyright 2023 Karmel0x, is licensed under MIT. FanPilot builds on its ASUS diagnostic/PWM control approach. The original notice is retained in LICENSE.txt.

## ASUS library

`AsusWinIO64.dll` is an ASUS component. It is not part of this repository or release archive and is not covered by this project's MIT license. Obtain an authorized local copy from your ASUS installation. The upstream project identifies ASUS System Analysis in the driver store as a source; the exact location varies by installed driver.

The adapter currently supports the inspected version **1.0.28.0**, pinned to:

```text
dac0a7765bbdd3b66ed9b1222bb7fa630b5abc195e5bce4758aa05ae880b2166
```

Other versions require compatibility review; do not remove the hash check to make an unknown DLL load.

## Microsoft Sysinternals PsExec

Obtain PsExec directly from [Microsoft](https://learn.microsoft.com/en-us/sysinternals/downloads/psexec). Microsoft’s [licensing FAQ](https://learn.microsoft.com/en-us/sysinternals/license-faq) disallows third-party redistribution, including free distribution. See the [license terms](https://learn.microsoft.com/en-us/sysinternals/license-terms).

Supported copy: **PsExec 2.43**, pinned to:

```text
078163d5c16f64caa5a14784323fd51451b8c831c73396b967b4e35e6879937b
```

Hardware launch uses `-accepteula -nobanner -s -d`. Review the Microsoft terms before using hardware mode. Setup itself does not execute PsExec or accept its license. A release must not embed PsExec or include it beside the EXE.

## NVIDIA monitoring

GPU monitoring calls read-only NVML functions from an installed NVIDIA driver. No NVIDIA library is copied or distributed. AMD and Intel monitoring backends are not implemented.

## Windows and .NET

The application uses installed Windows and .NET Framework components. Developer formatting uses Roslyn supplied by a locally installed .NET SDK; that SDK is not part of the application package.
