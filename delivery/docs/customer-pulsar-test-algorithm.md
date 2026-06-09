# Ophir Pulsar: алгоритм проверки у заказчика

## Подготовка

1. Подключить контроллер Ophir Pulsar по USB.
2. Убедиться, что датчик подключен к контроллеру и готов к измерению.
3. Открыть фирменное ПО Ophir / StarLab и проверить, что Pulsar виден с серийным номером.
4. Закрыть фирменное ПО Ophir / StarLab перед запуском теста в `Laser Energy Monitor`.
5. Убедиться, что установлен x86 runtime:
   - `OphirLMMeasurement.CoLMMeasurement` для основного COM API;
   - `OphirFastX` только если нужен fallback `Ophir Pulsar ActiveX (legacy)`.

## Проверка Windows USB

1. Запустить `app\LaserEnergyMonitor.Wpf.exe`.
2. Нажать `USB Devices`.
3. Проверить отчет `usb-inventory-*.txt`.
4. В отчете обратить внимание на строки с `Ophir`, `Pulsar`, `Jungo`, `WinDriver`, `StarLab`.

Эта проверка не использует Ophir SDK. Она нужна, чтобы отделить видимость устройства в Windows от проблем vendor runtime.

## Проверка Pulsar через COM

1. В поле `Ophir / Pulsar-4` выбрать `Ophir LMMeasurement SDK`.
2. Нажать `Self-Test`.
3. Нажать `Ophir Smoke-Test`.

COM-путь использует последовательность:

1. `ScanUSB`
2. `OpenUSBDevice`
3. `IsSensorExists`
4. `StartStream`
5. `GetData`
6. `StopStream`
7. `Close`

## Когда использовать legacy ActiveX

Если фирменная программа Ophir видит Pulsar, но `Ophir LMMeasurement SDK` показывает `ScanUSB result count: 0`, повторите проверку с источником `Ophir Pulsar ActiveX (legacy)`.

Legacy Pulsar путь использует последовательность:

1. `OpenUSB`
2. `GetNumberOfDevices`
3. `GetDeviceHandle`
4. `IsChannelExists`
5. `StartCS2`
6. `GetData`

## Нормальный результат

- для COM: `COM registration` и `COM activation` = `PASS`;
- для COM: `USB scan` находит хотя бы одно устройство;
- для legacy: `ActiveX registration`, `ActiveX activation` и `USB open` = `PASS`;
- `Pulsar scan` / `ScanUSB`: найдено хотя бы одно устройство.
- `Sensor detection`: найден активный канал.
- `Ophir Smoke-Test` получает samples или хотя бы показывает, что поток стартовал без critical fault.

## Если ошибка на ScanUSB или OpenUSB

Если COM runtime найден и активирован, но `ScanUSB` возвращает ноль устройств, проверьте:

- закрыта ли StarLab / фирменная программа Ophir;
- установлен ли полный vendor automation package, а не только отдельная DLL;
- соответствует ли runtime архитектуре `x86`;
- виден ли Pulsar в `USB Devices`;
- является ли устройство старым Pulsar, которому нужен fallback `Ophir Pulsar ActiveX (legacy)`.

Если отчет показывает, что ActiveX найден и активирован, но падает `OpenUSB`, приложение уже дошло до legacy vendor USB layer. В этом случае нужно проверить:

- закрыта ли StarLab / фирменная программа Ophir;
- не запущен ли второй экземпляр `OphirFastX`;
- установлен ли driver Pulsar, а не только OCX;
- соответствует ли runtime архитектуре `x86`;
- виден ли Pulsar в `USB Devices`.

## Что вернуть разработчику

- полный текст окна `USB Devices`;
- полный текст окна `Self-Test`;
- полный текст окна `Ophir Smoke-Test`;
- `usb-inventory-*.txt`;
- `hardware-self-test-*.txt`;
- `ophir-smoke-test-*.txt`;
- `application.log`;
- файлы из `ophir-captures`, если папка появилась;
- модель Pulsar, модель датчика и серийный номер, который показывает фирменная программа.
