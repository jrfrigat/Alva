<p align="center"><a href="README.md">English</a> - <b>Русский</b></p>

<p align="center">
  <a href="https://github.com/jrfrigat/Alva/actions/workflows/ci.yml"><img src="https://github.com/jrfrigat/Alva/actions/workflows/ci.yml/badge.svg" alt="CI"></a>
  <a href="https://github.com/jrfrigat/Alva/actions/workflows/pages.yml"><img src="https://github.com/jrfrigat/Alva/actions/workflows/pages.yml/badge.svg" alt="Pages"></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-blue.svg" alt="License: MIT"></a>
  <img src="https://img.shields.io/badge/.NET-10-512BD4.svg" alt=".NET 10">
</p>

# Alva

**Alva** — веб-приложение (PWA) для стратегического планирования задач и балансировки
ресурсов команды. Оно помогает максимально качественно, но без лишней бюрократии
распределить нагрузку по сотрудникам, наглядно подсветить риски сдвига дедлайнов и сразу
показать причины, почему команда не успевает.

Ключевые компоненты: гибкое распределение задач (люди, отделы и роли), двухмерный Гантт
(по задачам и по исполнителям), расчёт ёмкости и рисков, автобаланс спринта и полный
справочник участников.

**Демо:** <https://jrfrigat.github.io/Alva/> — работает целиком в браузере, без сервера.

## Возможности

- **Планирование** — проекты (M:N с командами), спринты, дерево задач с неограниченной
  декомпозицией, история изменений, зависимости FS/SS/FF/SF, назначение на человека,
  подкоманду или роль.
- **Гантт** — ось на всю ширину, дорожки, свёртка, критический путь, связи,
  перетаскивание, масштаб и виртуализация; один компонент для трёх страниц.
- **Ёмкость** — рабочие графики с исключениями и повторяющимися блоками, распределение
  оценки по доступным часам, нераспределённая потребность по ролям и подразделениям.
- **Риски** — нет исполнителя, просрочка, прогноз позже срока, перегрузка, незавершённая
  зависимость; сводка, фильтры и чипы причин, колокольчик с уведомлениями.
- **Автобаланс** — оптимизация назначений и сроков (OR-Tools) в нескольких режимах с
  предложением прямо на Гантте.
- **Аутентификация** — вход, регистрация и выход; пространства и команды, участники и
  владельцы, opt-in матрица прав.
- **Данные** — экспорт и импорт snapshot команды в JSON/CSV/XLSX, атомарно и со свежими
  идентификаторами.
- **Интерфейс** — PWA на Flare (тема MaterialDesign3Expressive, палитра Sapphire),
  адаптивная оболочка и доступность на десктопе.

## Быстрый старт

### Демо в браузере

Откройте <https://jrfrigat.github.io/Alva/> — данные живут в `localStorage`, сервер не нужен.

### Docker (прод-версия: приложение + PostgreSQL)

```powershell
./run-docker-compose.bat
```

Скрипт создаст `.env` со сгенерированным паролем, соберёт образ и поднимет стек.
Приложение: <http://localhost:8080>. Подробности — в
[Начале работы](docs/ru/getting-started.md).

Собрать образ без запуска стека и опубликовать его:

```powershell
docker build -f build/Dockerfile -t alva:latest .
./publish-docker.bat ВАШ_DOCKERHUB latest
```

### Локально без Docker

```powershell
dotnet restore Alva.slnx
$env:Database__Provider = "Sqlite"
dotnet run --project src/Alva.Api --urls http://localhost:8080
```

## Установка из релиза

Релиз (тег `v*`) публикует образ в GHCR и, если задана переменная `DOCKERHUB_USERNAME`, в Docker Hub:

```sh
docker pull ghcr.io/jrfrigat/alva:latest
docker run -p 8080:8080 \
  -e ConnectionStrings__Alva="Host=<host>;Port=5432;Database=alva;Username=alva;Password=<пароль>" \
  ghcr.io/jrfrigat/alva:latest
# Alva: http://localhost:8080
```

Полная конфигурация — в [Конфигурации](docs/ru/configuration.md).

## Структура решения

| Проект | Назначение |
| :-- | :-- |
| `Alva.Domain`, `Alva.Contracts`, `Alva.Infrastructure` | Домен, контракты, доступ к данным (EF Core) |
| `Alva.Api` | Minimal API, аутентификация, права, раздача WASM |
| `Alva.Client.UI` | Общая библиотека интерфейса и `IApiService` |
| `Alva.Client`, `Alva.Client.Demo` | Прод- и демо-оболочки |
| `Alva.Theme.Alva`, `Flare.Components.Gantt` | Тема и компонент Ганта |

## Документация

Полная документация — в **[docs/](docs/README.md)**:

| Документ | Описание |
| :-- | :-- |
| [Начало работы](docs/ru/getting-started.md) | Запуск (Docker, локально, демо), первый вход |
| [Конфигурация](docs/ru/configuration.md) | Все ключи и переменные `.env` |
| [Архитектура](docs/ru/architecture.md) | Проекты, слои, поток данных, аутентификация |
| [Развёртывание](docs/ru/deployment.md) | Docker, compose, HTTPS, резервные копии |
| [Разработка](docs/ru/development.md) | Сборка, тесты, конвенции |

## Сборка и тесты

```sh
dotnet build Alva.slnx -c Release
dotnet test Alva.slnx -c Release
```

Требуется .NET 10 (SDK 10.0.401, см. `global.json`).

## Участие и лицензия

- [CONTRIBUTING.md](CONTRIBUTING.md) — как внести изменения.
- [CHANGELOG.md](CHANGELOG.md) — история версий.

Интерфейс построен на [Flare](https://github.com/jrfrigat/Flare).

## Лицензия

[MIT](LICENSE) © 2026 FrigaT
