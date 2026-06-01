# Настройка Ophir

Интеграция Ophir перед началом любого живого захвата выполняет предварительную проверку.
Поддерживаются два семейства API вендора, потому что старые устройства Pulsar не обязательно возвращаются вызовом `OphirLMMeasurement.ScanUSB`.

## Требуемое состояние машины

- На машине должен быть установлен подходящий automation package от вендора Ophir.
- Для современного backend должен быть зарегистрирован COM ProgID `OphirLMMeasurement.CoLMMeasurement`.
- Для старых устройств Pulsar должен быть зарегистрирован x86 ActiveX-контрол `OphirFastX`. Приложение распознаёт `OPHIRFASTX.OphirFastXCtrl.1` и `OPHIRFASTXBeta.OphirFastXCtrl.1`.
- Установленный runtime от вендора должен соответствовать целевой архитектуре приложения `x86`.

## Что делает текущий адаптер

- `Ophir LMMeasurement SDK` использует `ScanUSB`, `OpenUSBDevice`, `StartStream` и `GetData`.
- `Ophir Pulsar ActiveX (legacy)` использует последовательность `OpenUSB`, `GetNumberOfDevices`, `GetDeviceHandle`, `StartCS2` и `GetData`.
- Если нужный ProgID отсутствует, приложение выдаёт понятную ошибку о неподготовленном окружении вместо общей COM-ошибки активации.
- Если выбранный runtime присутствует, приложение может создать COM-объект, проверить видимость по USB и запустить короткий smoke-test против выбранного реального источника.
- Если устройство видно, приложение может попробовать короткий живой захват и при необходимости сохранить сырые выборки в CSV для последующего повторного воспроизведения.

## Выбор источника в приложении

WPF-приложение читает настройки по каждому источнику из `src/LaserEnergyMonitor.Wpf/App.config`.

Активный источник выбирается в интерфейсе:

- `Simulated Ophir` для автономной проверки рабочих сценариев.
- `Ophir LMMeasurement SDK` для устройств, которые возвращаются вызовом `OphirLMMeasurement.ScanUSB`.
- `Ophir Pulsar ActiveX (legacy)` для старых устройств Pulsar, включая случай `Pulsar FU1.27`, когда фирменная утилита видит устройство, но `ScanUSB` возвращает ноль устройств.
- `Ophir Replay Capture` появляется, когда `MeasurementSources.OphirReplayPath` указывает на существующий CSV с захватом.

`Ophir Smoke-Test` проверяет выбранный legacy-источник, если выбран `Ophir Pulsar ActiveX (legacy)`. Для режима симуляции или replay используется `Ophir LMMeasurement SDK`.

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
