---
name: telegram-rich-formatting
description: Detailed guidelines for using Telegram Bot API 10.1+ Rich Messages, including tables, blockquotes, and increased limits.
---

# Telegram Rich Formatting Guide (API 10.1+)

Telegram has introduced the "Rich Messages" feature for complex, structured messages, effectively functioning like a block editor. This should be used for advanced structures like Tables or Blockquotes instead of relying on legacy MarkdownV2 hacks.

## 1. Core Concepts
- **Method:** Use the `sendRichMessage` method (or construct blocks if using API libraries).
- **Structure:** A Rich Message is composed of an array of distinct structural blocks.
- **Max Limits:** 32,768 UTF-8 characters per message, up to 500 blocks, max 16 levels of nesting.

## 2. Key Block Entities

### Tables (InputRichBlockTable)
Native tables support structured rows and cells.
- Tables cannot be styled cleanly using plain MarkdownV2. You must build an `InputRichBlockTable` JSON object.
- Features: per-cell styling, striped rows, column borders.

### Blockquotes (InputRichBlockBlockQuotation)
- For advanced quoting semantics. Allows for expandable text blocks.

## 3. Legacy vs Modern Formatting
For standard text, `MarkdownV2` or `HTML` is still supported.
However, when formatting **Tables** (e.g., parsing outage schedules into a tabular format), do NOT use monospace (` ``` `) as it breaks on mobile and lacks responsiveness. Instead, format the output to match the new `RichBlockTable` structures or utilize the appropriate library wrappers if writing C# clients.

## 4. Escaping Rules (Reminder)
If sticking to `MarkdownV2` for regular text, you MUST escape all of the following characters with a backslash `\`:
`_ * [ ] ( ) ~ \ > # + - = | { } . !`
