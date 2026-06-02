# BeamGage Vendor Probe

Standalone BeamGage automation probe. It does not reference the Laser Energy Monitor session, synchronization, Excel, logging, or watchdog layers.

Default flow:

```text
new AutomatedBeamGage(instance, true)
new AutomationFrameEvents(_bg.ResultsPriorityFrame).OnNewFrame += NewFrameFunction
_bg.DataSource.Start()
read _bg.FrameInfoResults.ID and _bg.PowerEnergyResults.Total in NewFrameFunction
```

Run:

```powershell
dotnet run --project tools\BeamGageVendorProbe\BeamGageVendorProbe.csproj -- --seconds 10
```

For the closest possible match to the official sample's passive wait after event subscription:

```powershell
dotnet run --project tools\BeamGageVendorProbe\BeamGageVendorProbe.csproj -- --seconds 10 --no-start
```
