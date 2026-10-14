# Cloudpunk Co-op

Исходный код экспериментального кооперативного мода для Cloudpunk (Windows, Unity IL2CPP, BepInEx 6). Протокол текущей версии — **18**. Здесь находятся актуальные исходники самого мода, включая ранее незакоммиченные изменения рабочего проекта.

**Полная бета ещё не завершена.** Независимые регионы, все сюжетные действия, торговля, награды и окончательная приёмка на двух компьютерах требуют дальнейшей разработки и проверки. Текущее состояние описано в [PROGRESS.md](PROGRESS.md) и [реестре оставшихся задач](docs/retro/beta-recovery/gap-register.md).

## Скачать и редактировать код

```powershell
git clone https://github.com/Vvyiloff/cloudpunk-coop-mod.git
cd cloudpunk-coop-mod
```

Откройте папку в Visual Studio, Rider или VS Code. Проекты находятся в `src/`:

| Проект | Назначение |
| --- | --- |
| `CloudpunkCoop.Plugin` | Плагин, игровая интеграция и интерфейс подключения |
| `CloudpunkCoop.Protocol` | Сетевой протокол и независимая игровая логика |
| `CloudpunkCoop.Protocol.Tests` | Модульные тесты протокола и логики |
| `CloudpunkCoop.TestClient` | Тестовый сетевой клиент |

## Установить готовый мод

Откройте [Releases](https://github.com/Vvyiloff/cloudpunk-coop-mod/releases), скачайте `CloudpunkCoop-install-protocol18-20261014.zip` и следуйте `НАЧАТЬ ЗДЕСЬ.txt` внутри архива. Содержимое папки `Скопировать в папку игры` копируется рядом с `Cloudpunk.exe`. Архив содержит загрузчик, runtime, библиотеки, лицензии и проверку SHA256.

## Сборка и тесты

Нужен .NET SDK с поддержкой `net6.0` и .NET 6 runtime для запуска тестов. При подготовке исходников использован SDK 10.0.401. Python требуется только для необязательных аналитических инструментов.

Тесты и тестовый клиент работают без установленной игры:

```powershell
dotnet test src\CloudpunkCoop.Protocol.Tests\CloudpunkCoop.Protocol.Tests.csproj -c Release
dotnet build src\CloudpunkCoop.TestClient\CloudpunkCoop.TestClient.csproj -c Release
```

Для сборки плагина нужна собственная совместимая установка Cloudpunk. Установите комплект из Releases, запустите игру один раз, чтобы BepInEx создал `BepInEx/interop/*.dll`, затем закройте игру.

Собрать плагин без установки:

```powershell
dotnet build src\CloudpunkCoop.Plugin\CloudpunkCoop.Plugin.csproj -c Release "-p:GameRoot=D:\Games\Cloudpunk"
```

Собрать и установить обе DLL в игру:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\deploy.ps1 -GameRoot "D:\Games\Cloudpunk"
```

Подставьте свою папку игры. `deploy.ps1` проверяет наличие interop и отказывается устанавливать сборку при запущенной игре. DLL появятся в `src/CloudpunkCoop.Plugin/bin/Release/` и `BepInEx/plugins/`.

## Совместная разработка

Любой участник может скачать код, создать форк и предложить изменения через Pull Request. Порядок описан в [CONTRIBUTING.md](CONTRIBUTING.md). Прямой push в основной репозиторий доступен владельцу и добавленным им участникам.

Архитектура находится в [docs/01-architecture.md](docs/01-architecture.md), планы и критерии готовности — в `plans/`. Исторические документы отражают этапы разработки; текущий статус начинается с верхнего раздела `PROGRESS.md`. Инструменты `live-test.ps1` и `duo-test.ps1` запускают игру, управляют окнами и создают резервные копии сохранений; используйте их на тестовой установке, предварительно прочитав параметры.

## Состав репозитория

`main` содержит исходники мода, тесты, инструменты и документацию. Готовый установочный комплект вынесен в Releases. Оригинальный комплект также сохранён в истории под тегом `test-2026-10-14-protocol18`.

Игровые исполняемые файлы, автоматически созданные interop-assemblies, сохранения, личные конфиги и рабочие дампы не включены. Аналитические Python-инструменты ожидают ваш собственный `analysis/dump/dump.cs`; для обычной сборки этот файл не нужен.
