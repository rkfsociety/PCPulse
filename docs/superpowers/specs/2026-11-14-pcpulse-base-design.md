# Дизайн базы экосистемы PCPulse

**Дата:** 2026-11-14  
**Репозиторий:** `https://github.com/rkfsociety/PCPulse`  
**Ветка работы:** `main` (напрямую, без feature-веток и PR, по правилам репозиториев `*Pulse`)

## 1. Назначение и почему так

`PCPulse` — единая кроссплатформенная оболочка (хаб) для экосистемы утилит `*Pulse` (`MemPulse`, `DiskPulse` и будущих проектов) на **Windows и Linux**.

- Каждая утилита остаётся самостоятельным проектом в собственном репозитории и продолжает устанавливаться и работать отдельно.
- Через `PCPulse` пользователь выбирает, какие `*Pulse`-модули установить, обновить, включить или удалить.
- Все установленные модули работают **внутри одного окна `PCPulse`** без открытия дополнительных окон и запуска отдельных GUI-процессов.

## 2. Цели, не-цели и анти-цели

### Цели
- Заложить чистую основу репозитория `PCPulse`:
  - общий пакет контрактов `PCPulse.Contracts` с независимым версионированием (`1.0.x`) и публикацией `.nupkg` в GitHub Releases;
  - кроссплатформенную оболочку `PCPulse` (`.NET 10` + `Avalonia 12.1.6`) для `win-x64`, `linux-x64`, `linux-arm64`;
  - менеджер модулей (каталог, предварительная проверка `.manifest.json`, установка из GitHub Releases по SHA-256, проверка совместимости контракта, обновление, включение/выключение, удаление);
  - единое окно с боковой навигацией, встроенным оверлеем диалогов и поддержкой трея;
  - инфраструктуру самообновления (per-user без прав администратора/`root`), тестов (включая эталонный тестовый модуль для проверки загрузки/изоляции) и CI/Release workflows (GitHub Actions + Inno Setup для Windows + архив/`install.sh` для Linux).
- Обеспечить стабильный совместимый контракт `1.0` для подготовки `MemPulse`, `DiskPulse` и будущих `*Pulse`-программ.

### Не-цели (на этом этапе)
- Не переносить исходный код и бизнес-логику `MemPulse` и `DiskPulse` в репозиторий `PCPulse`.
- Не встраивать чужие `.exe`-процессы через HWND reparenting (конфликтует с UAC UIPI на Windows и с Wayland на Linux).
- Не требовать прав администратора/`root` при обычном запуске или установке самого `PCPulse`.

### Анти-цели (что считается провалом)
- Модуль может открыть второе окно или системный `MessageBox`.
- Хост падает целиком при ошибке инициализации, загрузки или работы отдельного модуля.
- Конфликт версий зависимостей или `Avalonia`/`PCPulse.Contracts` ломает загрузку модулей.
- Модуль может запустить произвольный исполняемый файл с повышенными правами в обход `helperExecutable` из `module.json`.
- Установка модуля или обновления из сети без проверки целостности (SHA-256) и версии контракта.

## 3. Выбранный подход и альтернативы

Рассмотрены три варианта:
1. **Единая кроссплатформенная оболочка на `.NET 10` + `Avalonia 12.1.6` с динамически загружаемыми модулями (выбран).**  
   Один UI-фреймворк на Windows и Linux (уже проверенный в `MemPulse.Linux`), единый контракт `IPulseModule`, загрузка пакетов модулей в изолированный `AssemblyLoadContext`. Цена: `DiskPulse` (сейчас `.NET 8` + WPF + `requireAdministrator`) и Windows-UI `MemPulse` для интеграции с `PCPulse` адаптируются под общий модуль на `Avalonia` + `.NET 10` с выносом привилегированных действий в headless-helper.
2. **Две оболочки (`WPF` для Windows + `Avalonia` на Linux) с общим логическим контрактом.**  
   Отклонён: удваивает разработку и поддержку самой оболочки и каждого будущего `*Pulse`-модуля (два UI-проекта и два набора пакетов на каждую утилиту).
3. **Лаунчер отдельных `.exe` с встраиванием окон.**  
   Отклонён: не работает надёжно при UAC (UIPI на Windows блокирует встраивание elevated-окна в неадминский хост) и на Linux Wayland.

## 4. Архитектура базы PCPulse

### 4.1. Структура репозитория

```text
PCPulse/
  src/
    Directory.Build.props          # Единая <Version> приложения (0.1.0), LangVersion, Nullable, ImplicitUsings
    PCPulse.Contracts/             # Публичный контракт модулей (net10.0), собственная <Version> (1.0.0)
    PCPulse.Core/                  # Каталог, загрузка/выгрузка модулей, проверки SHA-256, настройки, логи, обновления (net10.0)
    PCPulse.App/                   # Кроссплатформенный Avalonia UI-хост и точка входа (net10.0, WinExe)
    PCPulse.SampleModule/          # Лёгкий эталонный/тестовый модуль (проверка контракта, не входит в пользовательский релиз)
  tests/
    PCPulse.Tests/                 # Юнит- и интеграционные тесты (xUnit, загрузка SampleModule, манифесты, каталог, безопасность путей)
  installer/
    PCPulse.iss                    # Inno Setup скрипт для Windows (per-user без UAC)
  scripts/
    publish-windows.ps1
    build-installer.ps1
    apply-update.ps1               # Безопасное обновление на Windows с перезапуском
    install-linux.sh               # Установка в ~/.local/share/pcpulse + .desktop
    apply-update.sh                # Безопасное обновление на Linux с перезапуском
  .github/workflows/
    ci.yml                         # Сборка и тесты Windows + Linux (включая --startup-smoke-test)
    release.yml                    # Авторелиз приложения (v*) и публикация пакета контракта (contracts-v*)
```

### 4.2. Стек и общие ограничения

- **Целевая платформа:** `.NET 10` (`net10.0`).
- **UI-фреймворк:** `Avalonia` `12.1.6` (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`). В `PCPulse.Contracts` подключается минимально необходимая зависимость на `Avalonia` (для типа `Control` в контракте view), версия жёстко фиксируется.
- **Язык интерфейса:** русский.
- **Единая тема по умолчанию:** тёмная тема (`FluentTheme` + `ThemeVariant.Dark`), единые токены цветов/отступов/кнопок, согласованные со стилем `MemPulse` и `DiskPulse`.

### 4.3. Контракт модулей (`PCPulse.Contracts`) и его распространение

**Принятые решения по контракту:**
- **Независимое версионирование пакета контракта:** `PCPulse.Contracts.csproj` явно переопределяет `<Version>1.0.0</Version>` и не наследует патч-версию приложения `PCPulse` из `src/Directory.Build.props`. Совместимость определяется парой `Major.Minor` (`1.0`): хост совместим с модулем, если `module.ContractMajor == host.ContractMajor` и `module.ContractMinor <= host.ContractMinor`.
- **Канал распространения `PCPulse.Contracts` (решение):** при изменении `<Version>` в `PCPulse.Contracts.csproj` (а также при первом релизе) `release.yml` собирает `PCPulse.Contracts.<ContractVersion>.nupkg` + `.sha256` и публикует в GitHub Releases под неизменяемым тегом `contracts-v<ContractVersion>` (а также прикладывает к текущему релизу `v*`). Репозитории модулей (`MemPulse`, `DiskPulse`) скачивают `.nupkg` с детерминированного URL `https://github.com/rkfsociety/PCPulse/releases/download/contracts-v1.0.0/PCPulse.Contracts.1.0.0.nupkg` в локальный источник пакетов без регистрации на NuGet.org и без токенов/секретов.

Ключевые элементы контракта:

- **`PulsePlatform`** (flags enum): `Windows = 1`, `Linux = 2`, `All = Windows | Linux`.
- **`PulseModuleDescriptor`**:
  - `string Id` (латиница в нижнем регистре, например `mempulse`, `diskpulse`);
  - `string DisplayName` (например `MemPulse`);
  - `string Description`;
  - `Version ModuleVersion`;
  - `string ContractVersion` (например `"1.0"`);
  - `PulsePlatform SupportedPlatforms`;
  - `bool RequiresElevationForSomeActions`.
- **`IPulseModule`** (входная точка модуля, реализуется публичным классом с конструктором без параметров):
  - `PulseModuleDescriptor Descriptor { get; }`
  - `Task InitializeAsync(IPulseHostContext context, CancellationToken cancellationToken)`
  - `Control CreateView()` — возвращает корневой `Avalonia.Controls.Control` модуля (с внутренней навигацией внутри одного окна без создания `Window`).
  - `Task<PulseStatusSummary?> GetSummaryAsync(CancellationToken cancellationToken)` — опциональная краткая сводка для главной страницы «Обзор».
  - `ValueTask DisposeAsync()`
- **Жизненный цикл модуля (решение):**
  - Включённый модуль инициализируется **один раз** за сессию (`InitializeAsync`), а `CreateView()` вызывается **один раз** с кэшированием корневого `Control` в хосте.
  - Переключение вкладок в боковом меню и сворачивание окна в трей **не вызывают** `DisposeAsync()` и не сбрасывают состояние/фоновый мониторинг модуля.
  - `DisposeAsync()` вызывается **только** при отключении, обновлении/удалении модуля или закрытии `PCPulse`.
- **`IPulseHostContext`** (сервисы хоста, передаваемые модулю при `InitializeAsync`):
  - `string ModuleDataDirectory` — каталог данных модуля (`%LOCALAPPDATA%\PCPulse\data\<id>` на Windows; `${XDG_DATA_HOME:-~/.local/share}/pcpulse/data/<id>` на Linux);
  - `string ModuleInstallDirectory` — каталог установки модуля;
  - `IPulseDialogService Dialogs` — показ подтверждений, предупреждений, информационных диалогов и ошибок **внутри оверлея главного окна `PCPulse`**;
  - `IPulseElevationService Elevation` (решение по безопасности и I/O):
    - `bool IsElevated { get; }`
    - `Task<int> RunElevatedHelperAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)`
    - Хост запускает **исключительно** файл `helperExecutable` для текущей ОС, явно объявленный в `module.json` и находящийся внутри `ModuleInstallDirectory` (с проверкой против path traversal и проверки существования файла). Произвольные пути запускать запрещено.
    - Возвращается код возврата (`int`): на Windows используется `UseShellExecute = true, Verb = "runas"`, на Linux — `pkexec`. Если хелперу нужно вернуть структурированные данные (например, отчёт сканирования или список ошибок), модуль передаёт через `args` путь ко временному файлу внутри `ModuleDataDirectory`.
  - `IPulseLogger Logger` — запись логов модуля в общий ротируемый журнал `PCPulse`;
  - `IPulseSettingsStore Settings` — чтение/запись JSON-настроек модуля в `ModuleDataDirectory`;
  - `Action RequestSummaryRefresh` — уведомление хоста об обновлении краткой сводки модуля на главной странице.

### 4.4. Формат пакета модуля, предварительный манифест и загрузка

- **Файлы в GitHub Release каждого модуля:**
  1. `<id>-module-<version>.manifest.json` — лёгкий манифест (копия `module.json`), позволяющий `PCPulse` проверить совместимость по ОС и `contractVersion` **до скачивания архива** (решение);
  2. `<id>-module-<version>-<rid>.zip` (где `<rid>` — `win-x64`, `linux-x64`, `linux-arm64` или `any`);
  3. `<id>-module-<version>-<rid>.zip.sha256`.
- **Содержимое `module.json` (и `.manifest.json`):**
  ```json
  {
    "id": "mempulse",
    "name": "MemPulse",
    "description": "Мониторинг и очистка оперативной памяти",
    "version": "0.3.0",
    "contractVersion": "1.0",
    "platforms": ["windows", "linux"],
    "entryAssembly": "MemPulse.PulseModule.dll",
    "entryType": "MemPulse.PulseModule.MemPulseModule",
    "helperExecutable": {
      "windows": "MemPulse.Helper.exe",
      "linux": "MemPulse.Helper"
    }
  }
  ```
  Поле `helperExecutable` опционально; если модуль не требует повышения прав, оно отсутствует.
- **Изоляция и общие сборки (`ModuleLoadContext : AssemblyLoadContext`):**
  - Каждый модуль грузится в собственный `AssemblyLoadContext` с `AssemblyDependencyResolver` по пути к `entryAssembly`.
  - `PCPulse.Contracts` и все сборки `Avalonia.*` (а также общие системные сборки) **всегда резолвятся из `AssemblyLoadContext.Default` хоста** (`Load` возвращает `null`), чтобы типы `Control`, `IPulseModule` и `IPulseHostContext` совпадали без конфликта контекстов.
  - Сбой одного модуля перехватывается хостом, логируется и отображает встроенную карточку ошибки на экране этого модуля, не роняя `PCPulse` и остальные модули.

### 4.5. Каталог, установка, обновление и удаление модулей

- **Реестр доступных модулей (`catalog.json`):**
  - Встроенный в `PCPulse` список официальных модулей (на старте: `mempulse` → `rkfsociety/MemPulse`, `diskpulse` → `rkfsociety/DiskPulse`).
  - Статусы в интерфейсе: `Не установлен`, `Установлен vX.Y.Z`, `Доступно обновление vA.B.C`, `Отключён`, `Несовместим с ОС`, `Несовместим с версией контракта`.
- **Установка / обновление:**
  1. Запрос последних релизов репозитория модуля через GitHub API (начиная с последнего; при несовместимости — поиск последнего совместимого релиза по `<id>-module-*.manifest.json`).
  2. Проверка `contractVersion` и `platforms` по `.manifest.json` до скачивания `.zip`.
  3. Подбор asset для текущего `<rid>` (приоритет: точный `win-x64` / `linux-x64` / `linux-arm64`, затем `any`) и его `.sha256`.
  4. Скачивание во временную папку, проверка SHA-256 и повторная сверка `module.json` внутри архива.
  5. Распаковка в `modules/<id>/<version>/`, обновление `installed-modules.json`.
  6. Если на Windows старые DLL активного модуля заблокированы процессом при обновлении/удалении, удаление старого каталога ставится в pending-очередь и выполняется при следующем старте `PCPulse` до загрузки модулей.
  7. Для локальной разработки и тестирования в разделе «Модули» есть кнопка «Установить из локального ZIP…».

### 4.6. Интерфейс единого окна, установка и самообновление `PCPulse`

- **Интерфейс (одно окно, без дочерних окон и системных диалогов):**
  - Левая боковая панель: **Обзор**, динамический список включённых модулей, **Модули**, **Настройки**, **О программе**.
  - Правая область: переключаемый `ContentControl` (кэширует `Control` активных модулей в рамках сессии).
  - Встроенный `DialogOverlay` поверх окна для всех подтверждений и ошибок.
  - Опциональный значок в трее (`Avalonia.Controls.TrayIcon`).
- **Модель установки (решение — per-user без прав администратора/`root`):**
  - **Windows (`installer/PCPulse.iss`):** `PrivilegesRequired=lowest`, установка в `%LOCALAPPDATA%\Programs\PCPulse`, ярлыки и регистрация деинсталлятора в `HKCU`.
  - **Linux (`scripts/install-linux.sh`):** установка в `${XDG_DATA_HOME:-$HOME/.local/share}/pcpulse` и `.desktop` в `~/.local/share/applications`.
- **Ручная проверка обновлений самого `PCPulse` (решение):**
  - В «Настройках» кнопка «Проверить обновления» запрашивает последний релиз `rkfsociety/PCPulse`.
  - Если версия новее текущей, хост показывает подтверждение в `DialogOverlay`, скачивает архив/установщик с проверкой SHA-256 и запускает внешний скрипт обновления (`apply-update.ps1` на Windows с тихим обновлением через `PCPulse-Setup-x64.exe` или заменой файлов для portable-режима; `apply-update.sh` на Linux), дожидается завершения текущего процесса и перезапускает `PCPulse`.

### 4.7. Правила разработки, версионирования и релизов

- Работа ведётся напрямую в ветке `main` (без отдельных веток и PR).
- Версия приложения — `<Version>` в `src/Directory.Build.props` (формат `MAJOR.MINOR.PATCH`). При каждом изменении кода приложения версия повышается минимум на patch.
- Версия контракта — `<Version>` в `src/PCPulse.Contracts/PCPulse.Contracts.csproj` (начальная `1.0.0`), меняется только при изменении публичного контракта `PCPulse.Contracts`.
- CI (`ci.yml`) собирает и прогоняет тесты на `windows-latest` и `ubuntu-latest` (включая headless smoke-тест запуска `PCPulse.App --startup-smoke-test`).
- Релизный workflow (`release.yml`) срабатывает при повышении `<Version>` в `src/Directory.Build.props` (публикует приложение для Windows и Linux + `.sha256` и прикладывает `PCPulse.Contracts.<ContractVersion>.nupkg`), а при изменении версии в `src/PCPulse.Contracts/PCPulse.Contracts.csproj` создаёт неизменяемый тег и релиз `contracts-v<ContractVersion>`.

## 5. Что остаётся на усмотрение разработчика при планировании

- Точные визуальные размеры отступов, векторные иконки (`StreamGeometry`) и детали анимаций.
- Конкретная реализация ротации лога (например, до 1 МБ + 1 архив `.1` как в `DiskPulse`).
- Детали HTTP-клиента GitHub API (таймауты, `User-Agent`, обработка rate limit).
- Набор unit- и smoke-тестов вокруг `PCPulse.SampleModule`, валидации `module.json`, `SHA-256`, `ModuleLoadContext` и проверки path traversal.
