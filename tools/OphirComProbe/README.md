# Ophir COM Probe

This utility checks the official Ophir COM automation path without Visual Studio or a compiler.

Run it from the delivery package:

```powershell
.\tools\LaserEnergyMonitor.OphirComProbe.exe --seconds 10
```

For pulse-triggered Ophir sensors, fire at least one safe test pulse during this 10-second probe window. A zero-sample report is expected if no pulse reaches the sensor.

Optional serial selection:

```powershell
.\tools\LaserEnergyMonitor.OphirComProbe.exe --seconds 10 --serial SERIAL_NUMBER
```

What it checks:

- x86 process and STA COM apartment
- COM ProgID `OphirLMMeasurement.CoLMMeasurement`
- registered COM DLL path and file version
- nearby `firmware` folder and `FU4A*.hex`, `FU4B*.hex`, `FU4F*.ttf` files
- `GetVersion` and `GetDriverVersion`
- `ScanUSB`
- `OpenUSBDevice`
- `IsSensorExists`
- `StartStream`
- `GetData`
- `StopStream`, `Close`, `CloseAll`

The report is written next to the utility:

```text
tools\output\ophir-com-probe\ophir-com-probe-YYYYMMDD-HHMMSS.txt
```

Send this report back together with the application `ophir-smoke-test-*.txt` and `application.log`.
