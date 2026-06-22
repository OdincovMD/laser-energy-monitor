# Laser Energy Monitor: комплект поставки

## Что входит

Комплект поставки содержит Release-сборку `LaserEnergyMonitor.Wpf.exe` для `.NET Framework 4.8` и `x86`.

Основные файлы:

- `LaserEnergyMonitor.Wpf.exe`
- `LaserEnergyMonitor.Wpf.exe.config`
- библиотеки `LaserEnergyMonitor.*.dll`
- библиотеки `DocumentFormat.OpenXml*.dll`
- документация по первому запуску BeamGage и Ophir через StarLab log
- инструмент `StarLabLogSimulator` для локальной проверки чтения `Data_log.txt`

## Поддерживаемые источники

- Beam: `BeamGage SDK`, затем `Scan`, выбор физического source и `Connect`.
- Ophir: `StarLab Log File`, чтение `Data_log.txt`, который пишет официальная программа StarLab.

Старые варианты прямого Ophir COM/ActiveX, replay и встроенные симуляции из приложения удалены.

## Что нужно для запуска

- Windows с `.NET Framework 4.8`.
- Для BeamGage: установленный BeamGage Professional с automation-библиотеками.
- Для Ophir: установленный StarLab, подключенный датчик и включенное логирование результатов.
- Права на запись в каталог приложения или выбранный `Output Path`.

## Как запускать

1. Распаковать комплект в локальный каталог.
2. Запустить `LaserEnergyMonitor.Wpf.exe`.
3. В Beam нажать `Scan`, выбрать нужный источник и нажать `Connect`.
4. В Ophir выбрать `StarLab Log File` и указать `Data_log.txt`.
5. Выполнить `Self-Test`.
6. Запустить измерительную сессию.

## Артефакты рядом с приложением

- `application.log`
- `measurement-session.xlsx`
- `measurement-session.RawData.csv`
- `measurement-session.Events.csv`
- `measurement-session.Summary.csv`
- `measurement-session.Stationary.csv`
- `hardware-self-test-*.txt`
- `usb-inventory-*.txt`
- `beamgage-smoke-test-*.txt`

## Проверено локально

- `dotnet build LaserEnergyMonitor.sln -p:Platform=x86 --no-restore`
- `dotnet test LaserEnergyMonitor.sln -p:Platform=x86 --no-restore`
- актуальные unit-тесты: 23 passed

## Что проверить на целевом ПК

- BeamGage видит физические источники после `Scan`.
- Выбранный BeamGage source подключается через `Connect`.
- StarLab пишет `Data_log.txt`.
- `StarLab Log File` в приложении видит файл, заголовок и колонку энергии.
- В UI и Excel значения Ophir меняются во время сессии.
