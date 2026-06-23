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
- Ophir: чтение `Data_log.txt`, который пишет официальная программа StarLab.

Старые варианты прямого Ophir COM/ActiveX, replay, встроенные симуляции и диагностические кнопки из основного окна удалены.

## Что нужно для запуска

- Windows с `.NET Framework 4.8`.
- Для BeamGage: установленный BeamGage Professional с automation-библиотеками.
- Для Ophir: установленный StarLab, подключенный датчик и включенное логирование результатов.
- Права на запись в каталог приложения или выбранный `Output Path`.

## Как запускать

1. Распаковать комплект в локальный каталог.
2. Запустить `LaserEnergyMonitor.Wpf.exe`.
3. В Beam нажать `Scan`, выбрать нужный источник и нажать `Connect`.
4. В Ophir указать файл `Data_log.txt`.
5. Проверить `Window`, `Enter %`, `Exit %` и `Output Path`.
6. Нажать `Start`.
7. После серии нажать `Stop`.

## Артефакты рядом с приложением

- `application.log`
- `measurement-session.xlsx`
- `measurement-session.RawData.csv`
- `measurement-session.Events.csv`
- `measurement-session.Summary.csv`
- `measurement-session.Stationary.csv`

## Что проверить на целевом ПК

- BeamGage видит физические источники после `Scan`.
- Выбранный BeamGage source подключается через `Connect`.
- StarLab пишет `Data_log.txt`.
- В UI меняются значения Beam и Ophir.
- В Excel значения Ophir меняются во время сессии.
