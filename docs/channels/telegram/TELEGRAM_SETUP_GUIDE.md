# Посібник з налаштування Telegram-каналу для SvitloSk Publisher

Цей документ містить інструкцію з налаштування параметрів, змінних оточення та засобів безпеки для автономної публікації в Telegram.

---

## 1. Цільові ресурси Telegram

* **Канал публікації (Channel):** `SvitloSk | Journal` (URL: `https://t.me/SvitloSkJournal`)
* **Ідентифікатор каналу (`chat_id`):** `-1004394558011`
* **Група обговорення (Discussion Group):** `SvitloSk | Chat` (URL: `https://t.me/SvitloSkChat`)
* **Ідентифікатор групи (`chat_id`):** `-1004365969004`
* **Бот публікатора (Bot):** `@svitlosk_publisher_bot` (ID: `8996093872`)

---

## 2. Змінні оточення

| Змінна | Приклад значення | Опис |
| :--- | :--- | :--- |
| `TELEGRAM_BOT_TOKEN` | `8996093872:AAH...` | Секретний токен бота від @BotFather (маскується) |
| `TELEGRAM_CHAT_ID` | `-1004394558011` | ID каналу для публікації журналу |
| `TELEGRAM_DISCUSSION_GROUP_ID` | `-1004365969004` | ID групи обговорень (для контролю коментарів) |
| `REGISTRY_PATH` | `local/registry/production_registry.json` | Шлях до локального JSON-файлу стану каналу |

---

## 3. Швидке налаштування секретів через PowerShell

Для інтерактивного та безпечного збереження змінних у Windows:

```powershell
.\deploy\set-telegram-secrets.ps1
```

Скрипт запитає токен (введення маскується зірочками), Chat ID та Discussion Group ID і збереже їх у профілі користувача Windows.

---

## 4. Діагностика та перевірка підключення

Перевірте валідність токена бота без надсилання повідомлень у канал:

```powershell
dotnet run --project SvitloSk.Publisher.Infrastructure -- --telegram-check
```

Очікуваний результат:
```
[SUCCESS] Connected to Telegram Bot '@svitlosk_publisher_bot'
```
