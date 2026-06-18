# StarLab Log Simulator

Generates a StarLab-like `Data_log.txt` file for local testing of the `StarLab Log File` acquisition mode.

Default behavior:

```powershell
LaserEnergyMonitor.StarLabLogSimulator.exe
```

The simulator writes one sample per second and flushes three rows every three seconds. By default the file is created under the tool output directory:

```text
output\starlab-log-simulator\Data_log.txt
```

Useful options:

```powershell
LaserEnergyMonitor.StarLabLogSimulator.exe --path "C:\Users\user\Documents\StarLab\Data_log.txt"
LaserEnergyMonitor.StarLabLogSimulator.exe --seconds 120 --base-mj 4.0 --noise-uj 120 --modulation-uj 250
```
