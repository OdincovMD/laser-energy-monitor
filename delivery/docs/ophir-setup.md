# Настройка Ophir

Интеграция Ophir перед началом любого живого захвата выполняет предварительную проверку.
Основной поддерживаемый путь - COM API `OphirLMMeasurement.CoLMMeasurement`.
Отдельный legacy-путь `OphirFastX` оставлен только для старых Pulsar-устройств, которые видны в фирменной программе Ophir, но не возвращаются через `OphirLMMeasurement.ScanUSB`.

## Требуемое состояние машины

- На машине должен быть установлен подходящий automation package от вендора Ophir.
- Для основного COM backend должен быть зарегистрирован ProgID `OphirLMMeasurement.CoLMMeasurement`.
- Для старых устройств Pulsar, которые не видны через `ScanUSB`, может дополнительно понадобиться x86 ActiveX-контрол `OphirFastX`. Приложение распознаёт `OPHIRFASTX.OphirFastXCtrl.1` и `OPHIRFASTXBeta.OphirFastXCtrl.1`.
- Установленный runtime от вендора должен соответствовать целевой архитектуре приложения `x86`.

## Что делает текущий адаптер

- `Ophir LMMeasurement SDK` использует COM-последовательность `ScanUSB`, `OpenUSBDevice`, `IsSensorExists`, `StartStream`, `GetData`, `StopStream`, `Close`.
- `Ophir Pulsar ActiveX (legacy)` использует последовательность `OpenUSB`, `GetNumberOfDevices`, `GetDeviceHandle`, `StartCS2` и `GetData` только как fallback для старых Pulsar.
- Если нужный ProgID отсутствует, приложение выдаёт понятную ошибку о неподготовленном окружении вместо общей COM-ошибки активации.
- Если выбранный runtime присутствует, приложение может создать COM-объект, проверить видимость по USB и запустить короткий smoke-test против выбранного реального источника.
- Если устройство видно, приложение может попробовать короткий живой захват и при необходимости сохранить сырые выборки в CSV для последующего повторного воспроизведения.

## Выбор источника в приложении

WPF-приложение читает настройки по каждому источнику из `src/LaserEnergyMonitor.Wpf/App.config`.

Активный источник выбирается в интерфейсе:

- `Simulated Ophir` для автономной проверки рабочих сценариев.
- `Ophir LMMeasurement SDK` - основной вариант для устройств, которые возвращаются вызовом `OphirLMMeasurement.ScanUSB`.
- `Ophir Pulsar ActiveX (legacy)` для старых устройств Pulsar, включая случай `Pulsar FU1.27`, когда фирменная утилита видит устройство, но `ScanUSB` возвращает ноль устройств.
- `Ophir Replay Capture` появляется, когда `MeasurementSources.OphirReplayPath` указывает на существующий CSV с захватом.

`Ophir Smoke-Test` проверяет выбранный реальный источник. Для `Ophir LMMeasurement SDK` это COM self-check и поток `StartStream` / `GetData`; для `Ophir Pulsar ActiveX (legacy)` это legacy-последовательность `OpenUSB` / `StartCS2` / `GetData`.

Актуальные настройки:

- `MeasurementSources.OphirSerialNumber`
- `MeasurementSources.OphirPreferredChannel`
- `MeasurementSources.OphirPollIntervalMs`
- `MeasurementSources.OphirTimestampStrategy`
- `MeasurementSources.OphirCaptureDirectory`
- `MeasurementSources.OphirReplayPath`
- `MeasurementSources.OphirReplaySpeedMultiplier`
- `MeasurementSources.OphirSmokeTestDurationMs`

Перед первой живой проверкой на целевой машине смотрите [ophir-integration-checklist.md](./ophir-integration-checklist.md).
