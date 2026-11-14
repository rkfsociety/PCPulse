# PCPulse

**PCPulse** — единая кроссплатформенная оболочка (хаб) для экосистемы утилит `*Pulse` (**Windows** и **Linux**).

Каждая программа экосистемы (`MemPulse`, `DiskPulse` и будущие проекты) может устанавливаться и работать самостоятельно, а через **PCPulse** можно выбрать, какие модули установить и использовать **внутри одного окна** без открытия новых окон и без постоянного запуска от имени администратора.

## Возможности базы

- Единый интерфейс на `.NET 10` и `Avalonia 12.1.6` (тёмная тема, поддержка Windows и Linux).
- Разделы в одном окне: **Обзор**, динамический список активных модулей, **Модули**, **Настройки**, **О программе**.
- Установка, обновление, включение/отключение и удаление модулей из GitHub Releases с предварительной проверкой `.manifest.json` и проверкой `SHA-256`, а также локальная установка из `.zip`.
- Изолированная загрузка модулей через `AssemblyLoadContext` с защитой от падения хоста при сбое отдельного модуля.
- Встроенный оверлей диалогов и безопасный запуск объявленных `helperExecutable` с повышением прав только по требованию (UAC / `pkexec`).
- Значок в системном трее и ручная проверка обновлений самого `PCPulse`.

## Структура проекта

- `src/PCPulse.Contracts` — публичный контракт модулей (`IPulseModule`, `IPulseHostContext`, `1.0.0`).
- `src/PCPulse.Core` — каталог, загрузка/выгрузка модулей, проверки `SHA-256`, защита путей, настройки, логи, обновления.
- `src/PCPulse.App` — кроссплатформенное графическое приложение `PCPulse`.
- `src/PCPulse.SampleModule` — эталонный модуль для проверки совместимости и тестов.
- `tests/PCPulse.Tests` — автоматические тесты (`xUnit`).

## Пакет контрактов для модулей (`MemPulse`, `DiskPulse` и др.)

Внешние репозитории модулей подключают `PCPulse.Contracts` (`1.0.0`) из релизов этого репозитория по тегу `contracts-v1.0.0`:

```text
https://github.com/rkfsociety/PCPulse/releases/download/contracts-v1.0.0/PCPulse.Contracts.1.0.0.nupkg
```

Требования к пакету модуля в GitHub Releases:
- `<id>-module-<version>.manifest.json` (копия `module.json`)
- `<id>-module-<version>-<rid>.zip` (`win-x64`, `linux-x64`, `linux-arm64` или `any`)
- `<id>-module-<version>-<rid>.zip.sha256`

## Сборка и проверка

```powershell
dotnet build PCPulse.sln -c Release --warnaserror
dotnet test PCPulse.sln -c Release
dotnet pack src/PCPulse.Contracts/PCPulse.Contracts.csproj -c Release -o dist
./scripts/publish-windows.ps1
```

Smoke-тест запуска окна:

```powershell
dotnet run --project src/PCPulse.App/PCPulse.App.csproj -c Release -- --startup-smoke-test
```

**Лицензия:** MIT.
