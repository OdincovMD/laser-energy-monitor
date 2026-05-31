# Laser Energy Monitor

`Laser Energy Monitor` — настольное приложение для сбора, синхронизации и экспорта результатов измерений с поддержкой симуляции и интеграций с оборудованием `BeamGage` и `Ophir`.

Проект распространяется по лицензии [MIT](LICENSE).

## Что внутри

- `src/LaserEnergyMonitor.Wpf` — WPF-приложение;
- `src/LaserEnergyMonitor.Application` — слой оркестрации сценариев;
- `src/LaserEnergyMonitor.Domain` — доменная модель и алгоритмы;
- `src/LaserEnergyMonitor.Infrastructure*` — интеграции, экспорт и логирование;
- `tests/LaserEnergyMonitor.Tests` — unit-тесты;
- `docs/` — техническая и пользовательская документация;
- `delivery/` — материалы для комплекта поставки.

## Быстрый старт

1. Откройте `LaserEnergyMonitor.sln` в Visual Studio 2022 или соберите решение через MSBuild.
2. Выберите конфигурацию `Release|x86`.
3. Убедитесь, что установлены зависимости для нужного режима работы:
   - для симуляции дополнительные runtime от вендора не нужны;
   - для реального `BeamGage` и `Ophir` нужны соответствующие компоненты вендора.
4. Запустите `LaserEnergyMonitor.Wpf`.

## Сборка

Приложение ориентировано на `.NET Framework 4.8` и `x86`.

Типичный сценарий сборки:

```powershell
msbuild LaserEnergyMonitor.sln /p:Configuration=Release /p:Platform=x86
```

## Документация

- [Структура проекта](docs/project-structure.md)
- [Архитектура и стек](docs/architecture-and-stack.md)
- [Статус реализации](docs/implementation-status.md)
- [Комплект поставки](docs/release-bundle-notes.md)
- [Первичная проверка Ophir](docs/ophir-first-run-validation.md)
- [Как участвовать](CONTRIBUTING.md)
- [Политика безопасности](SECURITY.md)
- [Кодекс поведения](CODE_OF_CONDUCT.md)
- [Чеклист релиза](RELEASE_CHECKLIST.md)
- [Список изменений](RELEASE_NOTES.md)
