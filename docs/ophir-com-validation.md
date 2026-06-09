# Ophir COM validation notes

## Local vendor documentation checked

The local vendor materials were checked under:

- `Ophir Automation Examples\Com object\OphirLMMeasurement COM Object.doc`
- `Ophir Automation Examples\Com object\Console Demo\Console Demo\Program.cs`
- `Ophir Automation Examples\Com object\Csharp_Demo\Form1.cs`

## API family

The primary Ophir integration uses the COM automation object:

- ProgID: `OphirLMMeasurement.CoLMMeasurement`
- Application source option: `Ophir LMMeasurement SDK`
- Application runtime backend: `OphirRuntimeBackend.LmMeasurement`

The application is built for `x86`, so the matching x86 vendor COM runtime must be installed and registered on the target machine.

## Vendor sample sequence

The vendor console sample uses this COM sequence:

1. create `OphirLMMeasurementLib.CoLMMeasurement`
2. `ScanUSB`
3. `OpenUSBDevice`
4. `GetWavelengths`
5. `GetRanges`
6. `StartStream`
7. poll `GetData`
8. `StopStream`
9. `Close`

## Application sequence

The application follows the same COM surface through late binding:

1. resolve ProgID `OphirLMMeasurement.CoLMMeasurement`
2. activate the COM object
3. `ScanUSB`
4. `OpenUSBDevice`
5. `IsSensorExists` on channels `0..3`
6. `StartStream`
7. poll `GetData`
8. `StopStream`
9. `Close`
10. `CloseAll`

The application does not require a compile-time interop assembly for this path.

## Diagnostic interpretation

If `COM registration` fails, the ProgID is not registered for the runtime visible to the x86 application.

If `COM activation` fails, the ProgID exists but the vendor runtime cannot instantiate the object. Check installation integrity and x86/x64 registration.

If `ScanUSB result count` is zero, the COM runtime is available but does not see a device. Check that StarLab or another Ophir program is closed, the USB driver is installed, the device is visible in Windows Device Manager, and the installed vendor package supports the connected device through `OphirLMMeasurement`.

If `OpenUSBDevice`, `IsSensorExists`, `StartStream`, or `GetData` fails, the application has already reached the vendor COM layer. Preserve the `Ophir Smoke-Test` report and `application.log` before changing drivers or runtime packages.

For old Pulsar devices that are visible in Ophir software but consistently absent from `OphirLMMeasurement.ScanUSB`, use the separate `Ophir Pulsar ActiveX (legacy)` source and the ActiveX validation notes.
