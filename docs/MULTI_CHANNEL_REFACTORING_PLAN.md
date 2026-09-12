# Оптимізований план рефакторингу SvitloSk Publisher (Мультиканальність: Telegram + Facebook)

## 1. Мета та бачення
**SvitloSk Publisher** розвивається як повноцінна мультиканальна система автономної публікації:
1. **Єдине спільне ядро (Core Engine)**:
   - Збирає відкриті дані про знеструмлення.
   - Розраховує графіки та формує статистику.
   - Генерує графічні банери (горизонтальні 1080x480 та квадратні 1080x1080) і 12-підчергову векторну сітку.
2. **Ізольовані канали доставки (Channel Pipelines)** відповідають виключно за специфіку своїх платформ (форматування, аутентифікація, API, стан постів):
   - **Telegram Channel**: MarkdownV2/HTML екранування, ліміт підпису 1024 символи, закриття коментарів у групі обговорень, робота з цілочисельними MessageId.
   - **Facebook Channel**: чистий UTF-8 текст (без зайвого екранування), робота з Facebook Graph API (пости у стрічку/фотоальбоми сторінки або групи), власна система ідентифікаторів постів string (наприклад, "10008392819_1209382109381").

---

## 2. Аналіз та ізоляція ризиків

### 2.1. Винесення платформної специфіки Telegram з бізнес-логіки:
1. **RegistryModel.cs**:
   - Поле TelegramMessageId: int? замінюється на універсальне ExternalMessageId: string? (у Facebook ID постів мають строковий формат).
2. **EditorialDecision.cs**:
   - Використовує універсальний ExternalMessageId.
3. **SequentialDispatcher.cs**:
   - Логіка закриття коментарів Telegram (CloseCommentsAsync) переноситься в ізольований шар Telegram-каналу (Infrastructure/Channels/Telegram/).
4. **Форматування тексту (EditorialContentTransformer)**:
   - Ядро формує чистий канонічний текст, а специфіка розмітки застосовується виключно всередині відповідного адаптера каналу.

### 2.2. Захист від взаємного впливу (Blast Radius):
- **Кожен канал має свій незалежний файл реєстру:**
  - local/registry/telegram_registry.json
  - local/registry/facebook_registry.json
- Збій мережі до Facebook або прострочений Facebook Access Token **жодним чином не блокує** публікацію в Telegram.

---

## 3. Цільова структура проекту

`
svitlosk-publisher/
│
├── SvitloSk.Publisher.Core/                     # 100% ЧИСТЕ ЯДРО (Domain & Engine)
│   ├── Engine/
│   │   ├── EditorialContentTransformer.cs       # Розрахунок статистики та чистий текст постів
│   │   ├── GraphicAssembly.cs                   # 12-підчерговий графік (SVG)
│   │   ├── BannerGraphicAssembly.cs             # Банери (SVG)
│   │   └── TerritoryAggregator.cs               # Агрегація територій
│   └── Domain/                                  # Доменні сутності (Edition, Publication тощо)
│
├── SvitloSk.Publisher.Application/              # БІЗНЕС-ОРКЕСТРАЦІЯ ТА МОДЕЛІ СТАНУ
│   ├── Interfaces/
│   │   ├── IPublisherOrchestrator.cs
│   │   ├── IRegistryStore.cs
│   │   └── IChannelPipeline.cs                  # Уніфікований контракт конвеєра каналу
│   └── Model/
│       ├── RegistryModel.cs                     # Уніфікований ExternalMessageId (string)
│       └── EditorialInput.cs                    # Вхідні пакети даних
│
├── SvitloSk.Publisher.Infrastructure/           # АДАПТЕРИ, I/O ТА МЕРЕЖА
│   ├── Channels/
│   │   ├── Telegram/                            # ІЗОЛЬОВАНИЙ TELEGRAM КАНАЛ
│   │   │   ├── ITelegramAdapter.cs
│   │   │   ├── TelegramAdapter.cs
│   │   │   ├── TelegramSequentialDispatcher.cs  # Закриття коментарів, черговість
│   │   │   ├── TelegramDryRunAdapter.cs
│   │   │   └── TelegramPipeline.cs
│   │   │
│   │   └── Facebook/                            # ІЗОЛЬОВАНИЙ FACEBOOK КАНАЛ
│   │       ├── IFacebookAdapter.cs
│   │       ├── FacebookGraphApiClient.cs        # Graph API (пости, фото)
│   │       ├── FacebookSequentialDispatcher.cs  # Відправка постів/фото у стрічку
│   │       ├── FacebookDryRunAdapter.cs
│   │       └── FacebookPipeline.cs
│   │
│   ├── Persistence/                             # Збереження реєстрів (JSON)
│   └── Program.cs                               # Головний мультиканальний раннер
│
├── deploy/                                      # СКРИПТИ ДЕПЛОЮ ТА СЕКРЕТІВ
│   ├── telegram/
│   │   ├── set-telegram-secrets.ps1
│   │   └── run-telegram-daemon.ps1
│   ├── facebook/
│   │   ├── set-facebook-secrets.ps1
│   │   └── run-facebook-daemon.ps1
│   └── run-all-channels.ps1                     # Запуск повного циклу з усіма каналами
│
└── docs/                                        # ДОКУМЕНТАЦІЯ
    ├── MULTI_CHANNEL_REFACTORING_PLAN.md        # Цей план
    ├── architecture/
    │   └── MULTI_CHANNEL_ARCHITECTURE.md
    └── channels/
        ├── telegram/
        │   └── CONFIGURATION.md
        └── facebook/
            ├── GRAPH_API_SETUP.md
            └── PERMISSIONS_AND_TOKENS.md
`

---

## 4. Покроковий план виконання робіт

### Етап 1: Уніфікація реєстру та ізоляція Telegram (Zero Risk)
1. Уніфікувати ідентифікатори повідомлень у RegistryModel.cs (ExternalMessageId: string?).
2. Ізолювати специфічні виклики закриття коментарів у простір Infrastructure.Channels.Telegram.
3. Перевірити, що всі наявні 140 тестів проходять без помилок.

### Етап 2: Організація структури папок і скриптів
1. Впорядкувати структуру скриптів у deploy/ під окремі канали.
2. Розділити конфігурацію файлів стану на telegram_registry.json та facebook_registry.json.

### Етап 3: Каркас для Facebook (Graph API)
1. Створити IFacebookAdapter (PublishPostAsync, PublishPhotoAsync, UpdatePostAsync, DeletePostAsync).
2. Реалізувати FacebookGraphApiClient та FacebookDryRunAdapter.
3. Створити FacebookPipeline для публікації графіків і журналів на сторінці Facebook.

### Етап 4: Інтеграція в головний раннер Program.cs
1. Додати підтримку паралельного або послідовного запуску конвеєрів:
   - Якщо змінні Facebook не налаштовані — канал автоматично пропускається без зупинки Telegram ([INFO] Facebook channel is disabled, skipping.).
   - Якщо налаштовані обидва — автономно оновлюються обидва канали.
2. Фінальне наскрізне тестування.
