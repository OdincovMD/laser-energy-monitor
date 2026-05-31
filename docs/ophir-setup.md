# Настройка Ophir

Интеграция Ophir перед началом любого живого захвата выполняет предварительную проверку.

## Требуемое состояние машины

- На машине должен быть установлен automation package от вендора Ophir.
- Должен быть зарегистрирован COM ProgID `OphirLMMeasurement.CoLMMeasurement`.
- Установленный runtime от вендора должен соответствовать целевой архитектуре приложения `x86`.

## Что делает текущий адаптер

- `Initialize()` проверяет, зарегистрирован ли `OphirLMMeasurement.CoLMMeasurement`.
- Если ProgID отсутствует, приложение выдает понятную ошибку о неподготовленном окружении вместо общей COM-ошибки активации.
- Если ProgID присутствует, приложение может создать COM-объект, проверить видимость по USB и запустить короткий smoke-test против реального источника SDK.
- Если устройство видно, приложение может попробовать короткий живой захват и при необходимости сохранить сырые выборки в CSV для последующего повторного воспроизведения.

## Выбор источника в приложении

WPF-приложение читает настройки по каждому источнику из `src/LaserEnergyMonitor.Wpf/App.config`.

Активный источник выбирается в интерфейсе:

- `Simulated Ophir` для автономной проверки рабочих сценариев.
- `Ophir SDK` для диагностики реального Pulsar-4 и живого захвата.
- `Ophir Replay Capture` появляется, когда `MeasurementSources.OphirReplayPath` указывает на существующий CSV с захватом.

`Ophir Smoke-Test` всегда принудительно использует реальный путь `Ophir SDK`, даже если в UI выбран режим симуляции, чтобы напрямую проверить установленный runtime и подключенный Pulsar-4.

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
