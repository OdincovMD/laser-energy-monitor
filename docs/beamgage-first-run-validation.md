# Первичная проверка BeamGage

## Цель

Подтвердить, что приложение видит BeamGage physical source, подключает выбранный source и получает значения во время сессии.

## Проверка

1. Запустите `LaserEnergyMonitor.Wpf.exe`.
2. Нажмите `Scan`.
3. Выберите нужный физический источник.
4. Нажмите `Connect`.
5. Укажите StarLab `Data_log.txt` для Ophir.
6. Нажмите `Start`.
7. Проверьте, что значение Beam обновляется.
8. Нажмите `Stop`.
9. Проверьте итоговый Excel и `measurement-session.RawData.csv`.

## Что вернуть при проблеме

- `application.log`
- `measurement-session.xlsx`
- `measurement-session.RawData.csv`
- название выбранного BeamGage source
- скриншот BeamGage Professional, если источник виден там, но не виден в приложении
