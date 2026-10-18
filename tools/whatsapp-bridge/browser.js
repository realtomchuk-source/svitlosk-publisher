// tools/whatsapp-bridge/browser.js
// Playwright-based browser driver for WhatsApp Web Channel automation.

const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');

const PROFILE_DIR = path.resolve(__dirname, '../../local/whatsapp-browser-profile');
const SCRATCH_DIR = path.resolve(__dirname, '../../scratch');

if (!fs.existsSync(PROFILE_DIR)) {
    fs.mkdirSync(PROFILE_DIR, { recursive: true });
}
if (!fs.existsSync(SCRATCH_DIR)) {
    fs.mkdirSync(SCRATCH_DIR, { recursive: true });
}

let browserContext = null;
let page = null;
let isConnected = false;
let hasQr = false;
let currentQrData = null;
let currentChannelUrl = null;
let isLaunching = false;
let launchPromise = null;
let authInterval = null;

function isBrowserAlive() {
    return !!(browserContext && page && !page.isClosed());
}

async function ensureBrowser() {
    if (isBrowserAlive() && isConnected) {
        return;
    }

    if (isLaunching && launchPromise) {
        return await launchPromise;
    }

    isLaunching = true;
    launchPromise = (async () => {
        try {
            await initBrowser();
        } finally {
            isLaunching = false;
            launchPromise = null;
        }
    })();

    return await launchPromise;
}

async function initBrowser() {
    if (isBrowserAlive() && isConnected) {
        return;
    }

    console.log('[PLAYWRIGHT] Initializing WhatsApp Web browser session...');
    console.log(`[PLAYWRIGHT] Profile directory: ${PROFILE_DIR}`);

    try {
        if (browserContext) {
            try { await browserContext.close(); } catch (_) {}
            browserContext = null;
            page = null;
            isConnected = false;
        }

        browserContext = await chromium.launchPersistentContext(PROFILE_DIR, {
            headless: false, // Visible window for session persistence & channel admin
            viewport: { width: 1280, height: 850 },
            args: [
                '--disable-blink-features=AutomationControlled',
                '--no-sandbox',
                '--disable-setuid-sandbox',
                '--disable-infobars',
                '--window-size=1280,850'
            ]
        });

        browserContext.on('close', () => {
            console.warn('[PLAYWRIGHT] Chromium browser context closed.');
            browserContext = null;
            page = null;
            isConnected = false;
        });

        const pages = browserContext.pages();
        // Look for existing WhatsApp Web tab
        let targetPage = pages.find(p => p.url().includes('web.whatsapp.com'));
        if (!targetPage) {
            targetPage = pages.length > 0 ? pages[0] : await browserContext.newPage();
            await targetPage.goto('https://web.whatsapp.com', { waitUntil: 'domcontentloaded', timeout: 60000 });
        }

        // Close any superfluous about:blank tabs opened by Chromium launch
        for (const p of browserContext.pages()) {
            if (p !== targetPage && p.url() === 'about:blank') {
                try { await p.close(); } catch (_) {}
            }
        }

        page = targetPage;
        page.on('close', () => {
            console.warn('[PLAYWRIGHT] Active page closed.');
            // Re-acquire another page if context is still open
            const remaining = browserContext ? browserContext.pages() : [];
            page = remaining.find(p => p.url().includes('web.whatsapp.com')) || (remaining.length > 0 ? remaining[0] : null);
            if (!page) {
                isConnected = false;
            }
        });

        console.log('[PLAYWRIGHT] WhatsApp Web page attached. Monitoring authentication state...');

        if (!authInterval) {
            monitorAuthState();
        }

        // Wait up to 15s for session restoration from profile
        for (let i = 0; i < 15; i++) {
            if (isConnected) break;
            await new Promise(r => setTimeout(r, 1000));
        }
    } catch (err) {
        console.error('[PLAYWRIGHT] Failed to launch browser context:', err.message);
        browserContext = null;
        page = null;
        isConnected = false;
    }
}

async function monitorAuthState() {
    if (authInterval) clearInterval(authInterval);
    authInterval = setInterval(async () => {
        if (!page || page.isClosed()) {
            isConnected = false;
            return;
        }

        try {
            // Check for QR canvas
            const qrCanvas = await page.$('canvas[aria-label*="Scan"], div[data-ref]');
            if (qrCanvas) {
                if (!hasQr) {
                    console.log('\n======================================================');
                    console.log(' [WHATSAPP-WEB] QR Code detected on screen!');
                    console.log(' Saving screenshot of QR code for instant scanning...');
                    console.log('======================================================\n');
                    try {
                        const artifactQr = path.resolve('C:/Users/ATom/.gemini/antigravity/brain/b6c6c31c-5763-4ccb-9998-ea9b80d03d8c', 'whatsapp_qr.png');
                        const localQr = path.resolve(__dirname, 'whatsapp_qr.png');
                        await qrCanvas.screenshot({ path: artifactQr });
                        await qrCanvas.screenshot({ path: localQr });
                        console.log(`[WHATSAPP-WEB] QR code saved to ${artifactQr}`);
                    } catch (ssErr) {
                        console.warn('[WHATSAPP-WEB] QR screenshot failed:', ssErr.message);
                    }
                }
                hasQr = true;
                isConnected = false;
                return;
            }

            // Check for authenticated main interface (pane-side, chat list, or channels bar)
            const mainInterface = await page.$('#pane-side, div[data-tab="3"], header, div[role="navigation"], span[data-icon="newsletter-outline"], span[data-icon="channel-outline"], button[aria-label="Каналы"], button[aria-label="Канали"], button[aria-label="Channels"]');
            if (mainInterface) {
                if (!isConnected) {
                    console.log('\n======================================================');
                    console.log(' [WHATSAPP-WEB] Connected and authenticated successfully!');
                    console.log(' User session is active and stored in persistent profile.');
                    console.log('======================================================\n');
                }
                isConnected = true;
                hasQr = false;
            }
        } catch (e) {
            // Page might be navigating or temporarily busy
        }
    }, 2500);
}

// Clean channel identifier: resolves invite link, full URL or code into direct URL
function getChannelWebUrl(channelIdentifier) {
    if (!channelIdentifier) return null;
    let clean = channelIdentifier.trim();

    const urlMatch = clean.match(/whatsapp\.com\/channel\/([a-zA-Z0-9_-]+)/i);
    if (urlMatch) {
        return `https://web.whatsapp.com/channel/${urlMatch[1]}`;
    }

    if (clean.startsWith('0029')) {
        return `https://web.whatsapp.com/channel/${clean}`;
    }

    return clean;
}

async function scrollToBottom() {
    try {
        const scrollBtn = await page.$('span[data-icon="down"], div[role="button"][aria-label*="Scroll to bottom"], div[role="button"][aria-label*="Вниз"], button[aria-label*="down"]');
        if (scrollBtn) {
            await scrollBtn.click();
            await page.waitForTimeout(600);
        }
        await page.keyboard.press('PageDown');
        await page.keyboard.press('End');
        await page.waitForTimeout(500);
    } catch (_) {}
}

// Force restart browser session to recover from stuck DOM or disconnects
async function restartSession() {
    console.log('[PLAYWRIGHT] Initiating clean restart of WhatsApp Web browser session...');
    try {
        if (browserContext) {
            await browserContext.close().catch(() => {});
        }
    } catch (_) {}
    browserContext = null;
    page = null;
    isConnected = false;
    hasQr = false;
    await initBrowser();
}

// Navigate to channel and wait for message list / composer with retries
async function ensureChannelOpen(channelIdentifier, maxRetries = 3) {
    let attempt = 0;
    while (attempt < maxRetries) {
        attempt++;
        await ensureBrowser();

        if (!isBrowserAlive() || !isConnected) {
            if (attempt < maxRetries) {
                console.warn(`[PLAYWRIGHT] WhatsApp Web not ready (Attempt ${attempt}/${maxRetries}). Waiting 4s before retry...`);
                await new Promise(r => setTimeout(r, 4000));
                continue;
            }
            throw new Error('WhatsApp Web is not authenticated yet. Please keep Chromium open and scan QR code if needed.');
        }

        try {
            // Check if already open and ready
            const composer = await page.$('footer div[contenteditable="true"], div[data-testid="conversation-compose-box-input"], footer div[role="textbox"]');
            const isChannelOpen = await page.evaluate(() => {
                const headers = Array.from(document.querySelectorAll('header'));
                const convHeader = headers.length > 1 ? headers[headers.length - 1] : headers[0];
                return convHeader ? (convHeader.innerText.includes('SvitloSk') || convHeader.innerText.includes('Журнал знеструмлень')) : false;
            });

            if (composer && isChannelOpen) {
                await scrollToBottom();
                return;
            }

            console.log(`[PLAYWRIGHT] Navigating to SvitloSk channel (Attempt ${attempt}/${maxRetries})...`);

            // 1. Click Channels icon on left rail
            const railHandle = await page.evaluateHandle(() => {
                const btns = Array.from(document.querySelectorAll('button, div[role="button"]'));
                return btns.find(b => {
                    const label = (b.getAttribute('aria-label') || '').toLowerCase();
                    return label.includes('канал') || label.includes('channel') || b.querySelector('span[data-icon*="newsletter"]');
                });
            });
            if (railHandle && railHandle.asElement()) {
                await railHandle.asElement().click({ force: true });
                await page.waitForTimeout(1500);
            }

            // 2. Click SvitloSk channel item in channels list
            const itemHandle = await page.evaluateHandle(() => {
                const spans = Array.from(document.querySelectorAll('span'));
                return spans.find(s => s.innerText && (s.innerText.includes('SvitloSk') || s.innerText.includes('Журнал знеструмлень')) && s.getBoundingClientRect().width < 380);
            });
            if (itemHandle && itemHandle.asElement()) {
                await itemHandle.asElement().click({ force: true });
                await page.waitForTimeout(2000);
            }

            // 3. Wait for composer to appear in footer
            const composerLocator = page.locator('footer div[contenteditable="true"], div[data-testid="conversation-compose-box-input"], footer div[role="textbox"]').first();
            await composerLocator.waitFor({ state: 'visible', timeout: 8000 });

            await page.waitForTimeout(1000);
            await scrollToBottom();
            console.log('[PLAYWRIGHT] Channel SvitloSk successfully verified and ready for posting.');
            return;
        } catch (err) {
            console.warn(`[PLAYWRIGHT] ensureChannelOpen attempt ${attempt} failed: ${err.message}`);
            if (attempt >= maxRetries) {
                // If all regular attempts failed, try a page reload as last resort
                console.warn('[PLAYWRIGHT] Reloading WhatsApp Web page to clear corrupted UI state...');
                try {
                    await page.reload({ waitUntil: 'domcontentloaded', timeout: 30000 });
                    await page.waitForTimeout(5000);
                } catch (_) {}
                throw new Error(`Failed to open SvitloSk WhatsApp channel after ${maxRetries} attempts: ${err.message}`);
            }
            await page.waitForTimeout(3000);
        }
    }
}

async function dismissOverlays() {
    try {
        // 1. If there's an open confirmation dialog or modal, check if it has a cancel or reset button
        const cancelModal = await page.$('div[role="dialog"] button[aria-label*="Отмен"], div[role="dialog"] button:has-text("Отмена"), div[role="dialog"] button:has-text("Скасувати"), div[role="dialog"] button:has-text("Cancel")');
        if (cancelModal) {
            console.log('[PLAYWRIGHT] Dismissing open modal dialog...');
            await cancelModal.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 2. If in media preview dialog (close via 'x-alt' icon)
        const closePreview = await page.$('div[data-animate-modal-popup] span[data-icon="x-alt"], div[data-animate-modal-popup] span[data-icon="x"]');
        if (closePreview) {
            await closePreview.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 3. If in message multi-select deletion mode (bottom bar)
        const cancelSelect = await page.$('button[aria-label*="Отмен"], button[aria-label*="Скасув"], button[aria-label*="Cancel"], button:has(span[data-icon="cancel"]), button:has(span[data-icon="x"])');
        if (cancelSelect) {
            console.log('[PLAYWRIGHT] Dismissing selection mode...');
            await cancelSelect.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 4. Press Escape ONLY if a context menu or popup menu is active to avoid closing the channel
        const activeMenu = await page.$('div[role="application"], div[role="menu"], ul[role="menu"], div[data-testid="context-menu"]');
        if (activeMenu) {
            await page.keyboard.press('Escape');
            await page.waitForTimeout(200);
        }
    } catch (_) {}
}

// 1. Send Text Message
async function sendTextMessage(channelIdentifier, text) {
    await ensureChannelOpen(channelIdentifier);

    // Ephemeral Single-Tail Invariant: If posting a technical status, clean up any previous status posts first
    if (text.includes('Останнє оновлення журналу:') || text.includes('Стан моніторингу:')) {
        await cleanupStrayStatusMessages();
    }

    console.log(`[PLAYWRIGHT] Sending text message (${text.length} chars)...`);

    // Ensure clean state before typing
    await dismissOverlays();

    // Target the bottom channel composer (last contenteditable textbox)
    const composer = page.locator('div[role="textbox"][contenteditable="true"]').last();
    await composer.waitFor({ state: 'visible', timeout: 15000 });

    await composer.focus();
    // Clear any previous text
    await page.keyboard.press('Control+A');
    await page.keyboard.press('Backspace');

    // Insert formatted text
    await page.keyboard.insertText(text);
    await page.waitForTimeout(500);

    // Click send button
    const sendBtn = await page.$('span[data-icon="send"], button[aria-label="Send"], button[aria-label="Надіслати"], button[aria-label="Отправить"]');
    if (sendBtn) {
        await sendBtn.click();
    } else {
        await page.keyboard.press('Enter');
    }

    await page.waitForTimeout(2500);

    // Extract ID of newly posted message from DOM
    const messageId = await getLatestMessageId();
    console.log(`[PLAYWRIGHT] Text message sent successfully. ID: ${messageId}`);

    return {
        isSuccess: true,
        messageId: messageId
    };
}

// 2. Send Media Message (Image Banner / 12-subqueue graphic)
async function sendMediaMessage(channelIdentifier, caption, imageBase64, mimeType = 'image/png') {
    await ensureChannelOpen(channelIdentifier);

    console.log(`[PLAYWRIGHT] Preparing media upload...`);

    const ext = mimeType.includes('jpeg') || mimeType.includes('jpg') ? 'jpg' : 'png';
    const tempFile = path.resolve(SCRATCH_DIR, `whatsapp_upload_${Date.now()}.${ext}`);
    fs.writeFileSync(tempFile, Buffer.from(imageBase64, 'base64'));

    try {
        await dismissOverlays();

        // 1. Locate and click attachment button (paperclip)
        const attachSelector = 'footer button[aria-label*="Прикреп"], footer button[aria-label*="Вклас"], footer button[aria-label*="Attach"], footer span[data-icon="ic-attach-file"], footer button:has(span[data-icon="clip"]), button:has(span[data-icon="ic-attach-file"]), [data-icon="ic-attach-file"]';
        const attachBtn = page.locator(attachSelector).first();
        const attachVisible = await attachBtn.waitFor({ state: 'visible', timeout: 7000 }).then(() => true).catch(() => false);

        if (!attachVisible) {
            throw new Error('Attachment clip button could not be found in composer.');
        }

        await attachBtn.click({ force: true });
        await page.waitForTimeout(600);

        // 2. Locate "Photos & videos" option in attachment menu (first menu item)
        const photoSelector = 'div[role="menu"] button[role="menuitem"]:has-text("Фото"), div[role="menu"] button[role="menuitem"]:has-text("Photo"), div[role="menu"] button[role="menuitem"]:first-child, button[role="menuitem"][aria-label*="Фото"], button[role="menuitem"][aria-label*="Photo"], button[role="menuitem"]:first-child';
        const photoBtn = page.locator(photoSelector).first();
        const photoVisible = await photoBtn.waitFor({ state: 'visible', timeout: 5000 }).then(() => true).catch(() => false);

        if (!photoVisible) {
            throw new Error('"Photos & videos" option could not be found in attachment menu.');
        }

        // 3. Trigger native file picker via Playwright filechooser event
        const [fileChooser] = await Promise.all([
            page.waitForEvent('filechooser', { timeout: 10000 }),
            photoBtn.click({ force: true })
        ]);

        await fileChooser.setFiles(tempFile);
        console.log(`[PLAYWRIGHT] Media file staged via file chooser: ${tempFile}`);
        await page.waitForTimeout(1500);

        // 4. Fill inline caption in preview modal if provided
        let captionSet = false;
        if (caption && caption.trim().length > 0) {
            try {
                console.log(`[PLAYWRIGHT] Setting inline caption on banner (${caption.length} chars)...`);
                const captionSelector = 'div[aria-autocomplete="list"][role="textbox"], div[aria-label*="обновлен"], div[aria-label*="caption"], div[aria-label*="підпис"], div[data-testid="media-caption-input-container"] div[contenteditable="true"], div[data-animate-modal-popup] div[contenteditable="true"]';
                const captionInput = page.locator(captionSelector).first();
                await captionInput.waitFor({ state: 'visible', timeout: 7000 });
                await captionInput.click({ force: true });
                await page.waitForTimeout(300);
                await page.keyboard.insertText(caption);
                await page.waitForTimeout(400);

                const textInBox = await captionInput.innerText().catch(() => '');
                if (textInBox && textInBox.trim().length > 0) {
                    captionSet = true;
                    console.log(`[PLAYWRIGHT] Inline caption verified in preview modal (${textInBox.length} chars).`);
                } else {
                    console.warn(`[PLAYWRIGHT] Caption box was empty after insertion.`);
                }
            } catch (capErr) {
                console.warn(`[PLAYWRIGHT] Could not set inline caption in preview modal: ${capErr.message}`);
            }
        }

        // 5. Click send button in media preview
        const sendMediaBtn = await page.$(
            'span[data-icon="wds-ic-send-filled"], span[data-icon="send"], ' +
            'div[role="button"][aria-label*="Send"], div[role="button"][aria-label*="Надіслати"], div[role="button"][aria-label*="Отправить"], ' +
            'button[aria-label*="Send"], button[aria-label*="Надіслати"], button[aria-label*="Отправить"]'
        );
        if (sendMediaBtn) {
            await sendMediaBtn.click();
        } else {
            await page.keyboard.press('Enter');
        }

        await page.waitForTimeout(4000);

        const messageId = await getLatestMessageId();
        console.log(`[PLAYWRIGHT] Media message sent successfully. ID: ${messageId}`);

        // 6. Safety Fallback: If inline caption failed to attach, send it as companion text message
        if (!captionSet && caption && caption.trim().length > 0) {
            console.log(`[PLAYWRIGHT] Fallback: Sending companion summary text message (${caption.length} chars)...`);
            await page.waitForTimeout(2000);
            await ensureChannelOpen(channelIdentifier);
            await sendTextMessage(channelIdentifier, caption);
        }

        return {
            isSuccess: true,
            messageId: messageId
        };
    } finally {
        try {
            if (fs.existsSync(tempFile)) fs.unlinkSync(tempFile);
        } catch (e) {}
    }
}

// Helper: Delete a single DOM message element via context menu or selection bar
async function deleteMessageRow(targetElement, messageId = null) {
    if (!targetElement) return false;

    try {
        await dismissOverlays();
        await targetElement.scrollIntoViewIfNeeded().catch(() => {});
        await page.waitForTimeout(400);

        const bubble = await targetElement.$('div[data-testid="msg-container"], div[data-testid="image-thumb"], img') || targetElement;
        await bubble.hover().catch(() => {});
        await page.waitForTimeout(300);

        await bubble.click({ button: 'right' });
        await page.waitForTimeout(600);

        const delMenuItem = page.locator('button[role="menuitem"], div[role="button"], li').filter({ hasText: /Удалить|Видалити|Delete/i }).last();
        const isMenuVis = await delMenuItem.waitFor({ state: 'visible', timeout: 3000 }).then(() => true).catch(() => false);
        if (!isMenuVis) {
            await page.keyboard.press('Escape');
            return false;
        }

        const delHandle = await delMenuItem.elementHandle();
        if (delHandle) {
            await page.evaluate(el => el.click(), delHandle);
        } else {
            await delMenuItem.click({ force: true });
        }
        await page.waitForTimeout(800);

        // Find trash icon in the bottom selection bar if selection mode was triggered
        const trashBtn = page.locator('button[aria-label="Удалить"], button[aria-label*="Удалить"], button[aria-label*="Видалити"], button[aria-label*="Delete"], button[data-tab="10"]:has(span[data-icon="delete"])').last();
        const isTrashVis = await trashBtn.waitFor({ state: 'visible', timeout: 2000 }).then(() => true).catch(() => false);
        if (isTrashVis) {
            const trashHandle = await trashBtn.elementHandle();
            if (trashHandle) {
                await page.evaluate(el => el.click(), trashHandle);
            } else {
                await trashBtn.click({ force: true });
            }
            await page.waitForTimeout(800);
        }

        // Handle confirmation modal (supports non-breaking space \u00a0 e.g. "Удалить у\u00a0всех")
        const confirmed = await page.evaluate(() => {
            const dialog = document.querySelector('div[role="dialog"], div[role="alertdialog"], [data-animate-modal-popup]');
            if (!dialog) return false;
            const buttons = Array.from(dialog.querySelectorAll('button'));
            const btn = buttons.find(b => /Удалить.*всех|Видалити.*всіх|Delete.*everyone|Удалить|Видалити|Delete/i.test(b.innerText.replace(/\s+/g, ' ')));
            if (btn) {
                btn.click();
                return true;
            }
            return false;
        });

        if (!confirmed) {
            const confirmBtn = page.locator('div[role="dialog"] button, div[role="alertdialog"] button, [data-animate-modal-popup] button')
                .filter({ hasText: /Удалить\s*у\s*всех|Видалити\s*для\s*всіх|Delete\s*for\s*everyone|Удалить|Видалити/i })
                .last();
            const isConfirmVis = await confirmBtn.waitFor({ state: 'visible', timeout: 3000 }).then(() => true).catch(() => false);
            if (!isConfirmVis) {
                await dismissOverlays();
                return false;
            }
            const confirmHandle = await confirmBtn.elementHandle();
            if (confirmHandle) {
                await page.evaluate(el => el.click(), confirmHandle);
            } else {
                await confirmBtn.click({ force: true });
            }
        }

        await page.waitForTimeout(1500);

        // Verification: confirm the element is actually removed from the DOM
        if (messageId) {
            const stillPresent = await page.locator(`div[data-id*="${messageId}"], [data-id*="${messageId}"]`).count() > 0;
            if (stillPresent) {
                console.warn(`[PLAYWRIGHT] Element '${messageId}' is still present after delete attempt.`);
                return false;
            }
        }

        return true;
    } catch (e) {
        console.warn('[PLAYWRIGHT] deleteMessageRow error:', e.message);
        await dismissOverlays();
        return false;
    }
}

// Clean up any existing technical status messages in channel to maintain strict single-tail invariant
async function cleanupStrayStatusMessages(keepLatest = false) {
    try {
        await scrollToBottom();
        const rows = await page.$$('div[role="row"]');
        const statusRows = [];
        for (let i = 0; i < rows.length; i++) {
            const text = await rows[i].innerText().catch(() => '');
            if (text.includes('Останнє оновлення журналу:') || text.includes('Стан моніторингу:')) {
                statusRows.push(rows[i]);
            }
        }

        const countToDelete = keepLatest ? statusRows.length - 1 : statusRows.length;
        if (countToDelete <= 0) return;

        console.log(`[PLAYWRIGHT] Found ${statusRows.length} status messages. Cleaning up ${countToDelete} to enforce single-tail invariant...`);

        for (let i = 0; i < countToDelete; i++) {
            const currentRows = await page.$$('div[role="row"]');
            let targetRow = null;
            for (let r = 0; r < currentRows.length; r++) {
                const t = await currentRows[r].innerText().catch(() => '');
                if (t.includes('Останнє оновлення журналу:') || t.includes('Стан моніторингу:')) {
                    targetRow = currentRows[r];
                    break;
                }
            }
            if (targetRow) {
                await deleteMessageRow(targetRow);
                await page.waitForTimeout(600);
            }
        }
    } catch (err) {
        console.warn('[PLAYWRIGHT] cleanupStrayStatusMessages non-fatal:', err.message);
        await dismissOverlays();
    }
}

// Pre-flight sweep for obsolete tomorrow forecasts (date <= todayDate or above today's journal)
async function sweepObsoleteForecasts(channelIdentifier, todayDateStr) {
    await ensureChannelOpen(channelIdentifier);
    console.log(`[PLAYWRIGHT] Executing pre-flight sweep for obsolete forecast messages (Today: ${todayDateStr})...`);

    // Clean up duplicate/stray technical status messages, preserving the active one
    await cleanupStrayStatusMessages(true);

    const chatPanel = await page.$('div[id="main"], div[data-testid="conversation-panel-messages"]');
    if (chatPanel) await chatPanel.hover().catch(() => {});
    for (let s = 0; s < 8; s++) {
        await page.mouse.wheel(0, -900);
        await page.waitForTimeout(300);
    }

    const toDeleteMsgIds = await page.evaluate((targetDate) => {
        const spans = Array.from(document.querySelectorAll('span'));
        const todaySpan = spans.find(s => {
            const txt = (s.innerText || '').trim().toLowerCase();
            return (txt === 'сегодня' || txt === 'сьогодні' || txt === 'today') && s.children.length === 0;
        });
        const todayY = todaySpan ? todaySpan.getBoundingClientRect().y : null;

        const rows = Array.from(document.querySelectorAll('div[role="row"]'));
        const obsolete = [];

        for (const r of rows) {
            const dataId = r.getAttribute('data-id') || r.querySelector('[data-id]')?.getAttribute('data-id');
            if (!dataId) continue;
            const text = r.innerText ? r.innerText.trim() : '';
            const hasImg = !!r.querySelector('img, div[data-testid="image-thumb"]');
            const rect = r.getBoundingClientRect();

            // Check 1: Explicit text with "ПРОГНОЗ"
            if (text.includes('ПРОГНОЗ НА ЗАВТРА') || text.includes('ПРОГНОЗ ЗНЕСТРУМЛЕНЬ') || text.includes('ПРОГНОЗ')) {
                const match = text.match(/\b(\d{2})\.(\d{2})\.(\d{4})\b/);
                let isObsolete = true;
                if (match && targetDate) {
                    const postDate = `${match[3]}-${match[2]}-${match[1]}`;
                    if (postDate > targetDate) isObsolete = false;
                }
                if (isObsolete) {
                    obsolete.push(dataId);
                    continue;
                }
            }

            // Check 2: Outage posts situated BEFORE "Сегодня" date divider (yesterday's forecast banner or district posts)
            if (todayY !== null && rect.y < todayY) {
                const isOutagePost = (hasImg && (text.includes('Планові знеструмлення') || text.includes('Аварійні знеструмлення'))) ||
                                     (!hasImg && (text.includes('ПЛАНОВІ ЗНЕСТРУМЛЕННЯ') || text.includes('АВАРІЙНІ ЗНЕСТРУМЛЕННЯ') || text.includes('старостинський округ') || text.includes('СТАРОКОСТЯНТИНІВ')));
                if (isOutagePost) {
                    obsolete.push(dataId);
                    continue;
                }
            }
        }
        return obsolete;
    }, todayDateStr);

    console.log(`[PLAYWRIGHT] Pre-flight sweep identified ${toDeleteMsgIds.length} obsolete forecast message(s).`);

    for (const msgId of toDeleteMsgIds) {
        console.log(`[PLAYWRIGHT] Pre-flight sweep: deleting obsolete forecast message '${msgId}'...`);
        await deleteMessage(channelIdentifier, msgId);
        await page.waitForTimeout(600);
    }

    await scrollToBottom();
    return { isSuccess: true, deletedCount: toDeleteMsgIds.length };
}

// 3. Delete Message via Context Menu
async function deleteMessage(channelIdentifier, messageId) {
    await ensureChannelOpen(channelIdentifier);

    console.log(`[PLAYWRIGHT] Attempting to delete message: ${messageId}...`);

    let targetElement = null;

    // A. Look by message ID attribute if available
    if (messageId && !messageId.includes('status')) {
        const msgLocator = page.locator(`div[data-id*="${messageId}"], [data-id*="${messageId}"]`).first();
        if (await msgLocator.count() === 0) {
            const chatPanel = await page.$('div[id="main"], div[data-testid="conversation-panel-messages"]');
            if (chatPanel) await chatPanel.hover().catch(() => {});

            for (let scrollAttempt = 0; scrollAttempt < 15; scrollAttempt++) {
                await page.mouse.wheel(0, -900);
                await page.waitForTimeout(300);
                if (await page.locator(`div[data-id*="${messageId}"], [data-id*="${messageId}"]`).count() > 0) break;
            }
        }

        targetElement = await page.$(`div[data-id*="${messageId}"], div[data-id$="${messageId}"], [data-id*="${messageId}"]`);
        if (targetElement) {
            const rowHandle = await page.evaluateHandle(el => el.closest('div[role="row"], div.message-out') || el, targetElement);
            if (rowHandle && rowHandle.asElement()) {
                targetElement = rowHandle.asElement();
            }
        }
    }

    // B. If not found by direct ID (or if it was a status message), locate by characteristic text at tail
    if (!targetElement) {
        await scrollToBottom();
        const rows = await page.$$('div[role="row"], div.message-out');
        const tailRows = rows.slice(-5);
        for (let i = tailRows.length - 1; i >= 0; i--) {
            const text = await tailRows[i].innerText().catch(() => '');
            if (text.includes('Останнє оновлення журналу:') || text.includes('Стан моніторингу:')) {
                targetElement = tailRows[i];
                break;
            }
        }
    }

    // C. Fallback: last sent message if messageId is 'latest' or 'tail'
    if (!targetElement && (messageId === 'latest' || messageId === 'tail')) {
        await scrollToBottom();
        const rows = await page.$$('div[role="row"], div.message-out');
        if (rows.length > 0) {
            targetElement = rows[rows.length - 1];
        }
    }

    if (!targetElement) {
        console.warn(`[PLAYWRIGHT] Message ${messageId} was not found on screen. Treating deletion as idempotent.`);
        return { isSuccess: true, messageId: messageId, note: 'Message not found, already deleted.' };
    }

    const success = await deleteMessageRow(targetElement, messageId);
    if (success) {
        console.log(`[PLAYWRIGHT] Confirmed message deletion for ${messageId}.`);
        return { isSuccess: true, messageId: messageId };
    }
    return { isSuccess: false, messageId: messageId, errorDescription: 'Failed to delete message via context menu' };
}

// 4. Edit Message (In-place edit or fallback signal)
async function editMessage(channelIdentifier, messageId, newText) {
    await ensureChannelOpen(channelIdentifier);

    console.log(`[PLAYWRIGHT] Attempting in-place edit for message: ${messageId}...`);

    let targetElement = null;
    if (messageId) {
        targetElement = await page.$(`div[data-id*="${messageId}"], div[data-id$="${messageId}"], [data-id*="${messageId}"]`);
        if (!targetElement) {
            for (let scrollAttempt = 0; scrollAttempt < 8; scrollAttempt++) {
                await page.mouse.wheel(0, -800);
                await page.waitForTimeout(400);
                targetElement = await page.$(`div[data-id*="${messageId}"], div[data-id$="${messageId}"], [data-id*="${messageId}"]`);
                if (targetElement) break;
            }
        }
        if (targetElement) {
            const rowHandle = await page.evaluateHandle(el => el.closest('div[role="row"], div.message-out') || el, targetElement);
            if (rowHandle && rowHandle.asElement()) {
                targetElement = rowHandle.asElement();
            }
        }
    }

    if (!targetElement) {
        return { isSuccess: false, errorDescription: 'Message not found for edit' };
    }

    try {
        await targetElement.scrollIntoViewIfNeeded().catch(() => {});
        await page.waitForTimeout(400);
        await targetElement.hover().catch(() => {});
        await page.waitForTimeout(300);

        await targetElement.click({ button: 'right' });
        await page.waitForTimeout(600);

        const editBtn = page.locator('span').filter({ hasText: /^Изменить$|^Редагувати$|^Edit$/ }).last();
        const editVisible = await editBtn.waitFor({ state: 'visible', timeout: 3000 }).then(() => true).catch(() => false);
        if (!editVisible) {
            await page.keyboard.press('Escape');
            return { isSuccess: false, errorDescription: 'Edit menu item not available (past edit window or unsupported)' };
        }

        await editBtn.click({ force: true });
        await page.waitForTimeout(800);

        // Edit input box appears (footer or inline)
        const editInput = page.locator('footer div[contenteditable="true"], div[role="textbox"]').last();
        const inputVisible = await editInput.waitFor({ state: 'visible', timeout: 4000 }).then(() => true).catch(() => false);
        if (inputVisible) {
            await editInput.click({ force: true });
            await page.keyboard.press('Control+A');
            await page.keyboard.press('Backspace');
            await page.keyboard.insertText(newText);
            await page.waitForTimeout(500);

            const checkBtn = page.locator('span[data-icon="check"], button[aria-label*="Підтвердити"], button[aria-label*="Готово"], button[aria-label*="Confirm"], button[aria-label*="Сохранить"]').first();
            const checkVisible = await checkBtn.waitFor({ state: 'visible', timeout: 2000 }).then(() => true).catch(() => false);
            if (checkVisible) {
                await checkBtn.click({ force: true });
            } else {
                await page.keyboard.press('Enter');
            }
            await page.waitForTimeout(1500);
            return { isSuccess: true, messageId: messageId };
        }

        await page.keyboard.press('Escape');
        return { isSuccess: false, errorDescription: 'Failed to access edit input' };
    } catch (e) {
        await dismissOverlays();
        return { isSuccess: false, errorDescription: e.message };
    }
}

// Helper: Get latest message ID from the DOM
async function getLatestMessageId() {
    try {
        const idElements = await page.$$('div[data-id], [data-id]');
        if (idElements.length > 0) {
            const lastEl = idElements[idElements.length - 1];
            const dataId = await lastEl.getAttribute('data-id');
            if (dataId) {
                // Return clean ID from data-id e.g. true_12345@newsletter_3EB0... -> 3EB0...
                const parts = dataId.split('_');
                return parts.length > 1 ? parts[parts.length - 1] : dataId;
            }
        }
    } catch (e) {}

    return `web_${Date.now()}`;
}

async function saveQrScreenshot() {
    if (!page || page.isClosed()) return null;
    try {
        const qrCanvas = await page.$('canvas[aria-label*="Scan"], div[data-ref]');
        const artifactQr = path.resolve('C:/Users/ATom/.gemini/antigravity/brain/b6c6c31c-5763-4ccb-9998-ea9b80d03d8c', 'whatsapp_qr.png');
        if (qrCanvas) {
            await qrCanvas.screenshot({ path: artifactQr });
            return artifactQr;
        } else {
            await page.screenshot({ path: artifactQr });
            return artifactQr;
        }
    } catch (e) {
        return null;
    }
}

async function getDebugDom() {
    if (!page || page.isClosed()) return { error: 'no page' };
    return await page.evaluate(() => {
        const all = Array.from(document.querySelectorAll('button, div[role="button"], span[data-icon], div[role="toolbar"]'));
        return all.slice(-25).map(el => ({
            tag: el.tagName,
            role: el.getAttribute('role'),
            ariaLabel: el.getAttribute('aria-label'),
            dataIcon: el.getAttribute('data-icon') || el.querySelector('span[data-icon]')?.getAttribute('data-icon'),
            text: el.innerText ? el.innerText.trim().slice(0, 40) : '',
            rect: {
                x: Math.round(el.getBoundingClientRect().x),
                y: Math.round(el.getBoundingClientRect().y),
                width: Math.round(el.getBoundingClientRect().width),
                height: Math.round(el.getBoundingClientRect().height)
            }
        }));
    });
}

function getStatus() {
    const alive = isBrowserAlive();
    return {
        status: alive ? (isConnected ? 'ok' : (hasQr ? 'needs_qr' : 'connecting')) : 'disconnected',
        connected: alive && isConnected,
        hasQr: hasQr,
        user: (alive && isConnected) ? 'Channel Admin (Browser Session)' : null
    };
}

module.exports = {
    initBrowser,
    ensureBrowser,
    ensureChannelOpen,
    sendTextMessage,
    sendMediaMessage,
    deleteMessage,
    editMessage,
    saveQrScreenshot,
    getStatus,
    getDebugDom,
    cleanupStrayStatusMessages,
    sweepObsoleteForecasts,
    restartSession,
    getPage: () => page
};
