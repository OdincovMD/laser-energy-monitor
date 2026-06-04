# Ophir Pulsar ActiveX validation notes

## Local vendor documentation checked

The local vendor materials were checked under:

- `Ophir Automation Examples\ActiveX legacy\OphirFastX ActiveX Control.doc`
- `Ophir Automation Examples\ActiveX legacy\OphirFastX Pulsar demos\Pyroelectric and PDEnergy\CSDemo\CSDemo\Form1.cs`
- `Ophir Automation Examples\ActiveX legacy\OphirFastX Pulsar demos\Thermopile and Photodiode\CSDemo\CSDemo\Form1.cs`
- `Ophir Automation Examples\Com object\Console Demo\Console Demo\Program.cs`

## What the vendor API expects

For the modern COM object, the sample sequence is:

1. `ScanUSB`
2. `OpenUSBDevice`
3. select an active channel
4. `StartStream`
5. poll `GetData`
6. `StopStream`
7. `Close`

For legacy Pulsar devices through `OphirFastX`, the documented setup is:

1. host the ActiveX control in a windowed ActiveX container
2. `OpenUSB`
3. `GetNumberOfDevices`
4. `GetDeviceHandle`
5. `EnableDisableChannelForCS`
6. `StartCS` or `StartCS2`
7. collect measurements
8. `StopCS`
9. `CloseUSB`

The documentation explicitly describes `StartCS2` plus `GetData`. It was added for environments where delivering the full data payload through an ActiveX event can hang the client. The beta `OphirFastXBeta` notes also describe polling the control every 50 ms and using only `StartCS2` plus `GetData` for measurement collection.

## Current application behavior

The application keeps two real Ophir choices:

- `Ophir LMMeasurement SDK` for devices returned by `OphirLMMeasurement.ScanUSB`.
- `Ophir Pulsar ActiveX (legacy)` for Pulsar devices visible in Ophir software but not returned by `ScanUSB`.

The legacy path now follows the vendor constraints more closely:

- it runs on an STA worker thread;
- under `.NET Framework` it first tries to host the ActiveX control in a hidden WinForms window;
- the STA worker pumps WinForms messages while the control is alive;
- it uses `OpenUSB`, `GetNumberOfDevices`, `GetDeviceHandle`, `IsChannelExists`, `EnableDisableChannelForCS`, `StartCS2`, `GetData`, `StopCS`, `CloseUSB`;
- the poll interval defaults to 50 ms, matching the vendor beta polling guidance.

## Interpretation of the customer failure

The customer report failed at `OpenUSB` with `HRESULT=0x8000FFFF (E_UNEXPECTED)`.

That happened before device selection, channel selection, `StartCS2`, or `GetData`. It means the registered ActiveX object was found and activated, but the vendor USB layer could not be initialized.

Most likely causes to check on the target machine:

- StarLab or another Ophir tool is open and already holds the device;
- another `OphirFastX` instance is active;
- the Pulsar driver was not installed, or only the OCX was registered;
- the installed vendor package does not match the app's x86 runtime;
- the target machine has the normal `OphirFastX` control but its USB driver stack is incomplete;
- the device is visible to Windows but not available to the legacy ActiveX runtime.

## Expected next target-machine result

On the next target test, select `Ophir Pulsar ActiveX (legacy)` and run `Ophir Smoke-Test`.

Before the smoke-test, run `USB Devices` and keep `usb-inventory-*.txt`. This report is independent from the Ophir SDK and shows what Windows exposes through USB / PnP.

The useful successful path is:

- `ActiveX registration`: `PASS`
- `ActiveX activation`: `PASS`, with activation mode shown
- `USB open`: `PASS`
- `Pulsar scan`: `PASS`, with at least one detected device
- `Sensor detection`: `PASS`, with an active channel
- `Stream probe`: `PASS` or live samples reported by the smoke-test

If `OpenUSB` still fails, the application-side API sequence is already aligned with the vendor documentation; the remaining issue is target-machine Ophir runtime/driver/exclusive-device access.
