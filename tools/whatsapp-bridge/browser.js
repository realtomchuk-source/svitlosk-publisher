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
        page = pages.length > 0 ? pages[0] : await browserContext.newPage();

        page.on('close', () => {
            console.warn('[PLAYWRIGHT] Active page closed.');
            page = null;
            isConnected = false;
        });

        await page.goto('https://web.whatsapp.com', { waitUntil: 'domcontentloaded', timeout: 60000 });
        console.log('[PLAYWRIGHT] WhatsApp Web loaded. Monitoring authentication state...');

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

// Navigate to channel and wait for message list / composer
async function ensureChannelOpen(channelIdentifier) {
    await ensureBrowser();

    if (!isBrowserAlive() || !isConnected) {
        throw new Error('WhatsApp Web is not authenticated yet. Please keep Chromium open and scan QR code if needed.');
    }

    const composer = await page.$('div[data-testid="conversation-compose-box-input"], footer div[contenteditable="true"]');
    const headerTitle = await page.$('header span[title*="SvitloSk"], header span:has-text("SvitloSk")');
    if (composer && headerTitle) {
        await scrollToBottom();
        return;
    }

    console.log('[PLAYWRIGHT] Opening SvitloSk channel...');
    // Click Channels icon on left rail
    const railBtn = await page.$('span[data-icon*="newsletter"], button[aria-label*="Канал"], button[aria-label*="Channel"]');
    if (railBtn) {
        await railBtn.click();
    } else {
        await page.mouse.click(25, 180);
    }
    await page.waitForTimeout(1500);

    // Click SvitloSk channel at top of list
    const channelItem = page.locator('#pane-side span[title*="SvitloSk"], #pane-side div[role="listitem"]:has-text("SvitloSk")').first();
    const itemVisible = await channelItem.waitFor({ state: 'visible', timeout: 3000 }).then(() => true).catch(() => false);
    if (itemVisible) {
        await channelItem.click({ force: true });
    } else {
        await page.mouse.click(200, 180);
    }

    await page.waitForTimeout(2000);
    await scrollToBottom();
}

async function dismissOverlays() {
    try {
        // 1. If there's an open confirmation dialog or modal, check if it has a cancel or reset button
        const cancelModal = await page.$('div[role="dialog"] button[aria-label*="Отмен"], div[role="dialog"] button:has-text("Отмена"), div[role="dialog"] button:has-text("Скасувати"), div[role="dialog"] button:has-text("Cancel"), button:has-text("Сбросить"), div[role="button"]:has-text("Сбросить"), button:has-text("Скинути")');
        if (cancelModal) {
            console.log('[PLAYWRIGHT] Dismissing open modal dialog...');
            await cancelModal.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 2. If in media preview dialog (close via 'x-alt' icon)
        const closePreview = await page.$('span[data-icon="x-alt"], span[data-icon="x"]');
        if (closePreview) {
            await closePreview.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
            const resetBtn = await page.$('button:has-text("Сбросить"), div[role="button"]:has-text("Сбросить"), button:has-text("Скинути")');
            if (resetBtn) await resetBtn.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 3. If in message multi-select deletion mode (bottom bar)
        const cancelSelect = await page.$('button[aria-label*="Отмен"], button[aria-label*="Скасув"], button[aria-label*="Cancel"], span[data-icon="cancel"]');
        if (cancelSelect) {
            console.log('[PLAYWRIGHT] Dismissing selection mode...');
            await cancelSelect.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 4. Press Escape twice to clear context menus or overlays
        await page.keyboard.press('Escape');
        await page.waitForTimeout(200);
        await page.keyboard.press('Escape');
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
        let attachBtn = await page.$(
            'footer span[data-icon="ic-attach-file"], span[data-icon="ic-attach-file"], ' +
            'footer button:has(span[data-icon="clip"]), button[aria-label*="Прикреп"], button[aria-label*="Вклас"], ' +
            'button[aria-label*="Attach"], div[role="button"][aria-label*="Attach"]'
        );

        if (!attachBtn) {
            throw new Error('Attachment clip button could not be found in composer.');
        }

        await attachBtn.click();
        await page.waitForTimeout(600);

        // 2. Locate "Photos & videos" option in attachment menu
        const photoBtn = await page.$(
            'button[role="menuitem"][aria-label*="Фото"], button[role="menuitem"][aria-label*="Photo"], ' +
            'li:has-text("Фото"), div[role="button"]:has-text("Фото"), div[role="button"]:has-text("Photo")'
        );

        if (!photoBtn) {
            throw new Error('"Photos & videos" option could not be found in attachment menu.');
        }

        // 3. Trigger native file picker via Playwright filechooser event
        const [fileChooser] = await Promise.all([
            page.waitForEvent('filechooser', { timeout: 10000 }),
            photoBtn.click()
        ]);

        await fileChooser.setFiles(tempFile);
        console.log(`[PLAYWRIGHT] Media file staged via file chooser: ${tempFile}`);
        await page.waitForTimeout(1500);

        // 4. Fill inline caption in preview modal if provided
        let captionSet = false;
        if (caption && caption.trim().length > 0) {
            try {
                console.log(`[PLAYWRIGHT] Setting inline caption on banner (${caption.length} chars)...`);
                const captionInput = page.locator('div[data-testid="media-caption-input-container"]');
                await captionInput.waitFor({ state: 'visible', timeout: 7000 });
                await captionInput.click({ force: true });
                await page.waitForTimeout(200);
                await page.keyboard.press('Control+A');
                await page.keyboard.press('Backspace');
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

// Helper: Delete a single DOM message element via context menu
async function deleteMessageRow(targetElement) {
    if (!targetElement) return false;

    try {
        await targetElement.click({ button: 'right' });
        await page.waitForTimeout(600);

        const deleteBtn = page.locator('span').filter({ hasText: /^Удалить$|^Видалити$|^Delete$/ }).last();
        const deleteVisible = await deleteBtn.waitFor({ state: 'visible', timeout: 3000 }).then(() => true).catch(() => false);
        if (!deleteVisible) {
            await page.keyboard.press('Escape');
            return false;
        }

        await deleteBtn.click({ force: true });
        await page.waitForTimeout(800);

        // Find trash icon in the bottom selection bar
        const trashBtn = page.locator('button[aria-label="Удалить"], button[aria-label*="Удалить"], button[aria-label="Видалити"], button[aria-label="Delete"], span[data-icon="delete"]').last();
        const trashVisible = await trashBtn.waitFor({ state: 'visible', timeout: 2500 }).then(() => true).catch(() => false);
        if (trashVisible) {
            await trashBtn.click({ force: true });
            await page.waitForTimeout(800);
        }

        const confirmBtn = page.locator('button').filter({ hasText: /Удалить у|Видалити для|Delete for everyone|Удалить|Видалити/ }).first();
        const confirmVisible = await confirmBtn.waitFor({ state: 'visible', timeout: 3000 }).then(() => true).catch(() => false);
        if (confirmVisible) {
            await confirmBtn.click({ force: true });
            await page.waitForTimeout(1500);
            return true;
        } else {
            await dismissOverlays();
            return false;
        }
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
            if ((text.includes('Останнє оновлення журналу:') || text.includes('Стан моніторингу:')) && !text.includes('Архівний')) {
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
                if ((t.includes('Останнє оновлення журналу:') || t.includes('Стан моніторингу:')) && !t.includes('Архівний')) {
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

// 3. Delete Message via Context Menu
async function deleteMessage(channelIdentifier, messageId) {
    await ensureChannelOpen(channelIdentifier);
    await scrollToBottom();

    console.log(`[PLAYWRIGHT] Attempting to delete message: ${messageId}...`);

    let targetElement = null;

    // A. Look by message ID attribute if available
    if (messageId && !messageId.includes('status')) {
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
        const rows = await page.$$('div[role="row"], div.message-out');
        const tailRows = rows.slice(-5);
        for (let i = tailRows.length - 1; i >= 0; i--) {
            const text = await tailRows[i].innerText().catch(() => '');
            if ((text.includes('Останнє оновлення журналу:') || text.includes('Стан моніторингу:')) && !text.includes('Архівний')) {
                targetElement = tailRows[i];
                break;
            }
        }
    }

    // C. Fallback: last sent message if messageId is 'latest' or 'tail'
    if (!targetElement && (messageId === 'latest' || messageId === 'tail')) {
        const rows = await page.$$('div[role="row"], div.message-out');
        if (rows.length > 0) {
            targetElement = rows[rows.length - 1];
        }
    }

    if (!targetElement) {
        console.warn(`[PLAYWRIGHT] Message ${messageId} was not found on screen. Treating deletion as idempotent.`);
        return { isSuccess: true, messageId: messageId, note: 'Message not found, already deleted.' };
    }

    const success = await deleteMessageRow(targetElement);
    if (success) {
        console.log(`[PLAYWRIGHT] Confirmed message deletion for ${messageId}.`);
        return { isSuccess: true, messageId: messageId };
    } else {
        return { isSuccess: false, errorDescription: 'Failed to delete message via context menu' };
    }
}

// 4. Edit Message (In-place edit or fallback signal)
async function editMessage(channelIdentifier, messageId, newText) {
    await ensureChannelOpen(channelIdentifier);

    console.log(`[PLAYWRIGHT] Attempting in-place edit for message: ${messageId}...`);

    let targetElement = null;
    if (messageId) {
        targetElement = await page.$(`div[data-id*="${messageId}"], div[data-id$="${messageId}"]`);
    }

    if (!targetElement) {
        return { isSuccess: false, errorDescription: 'Message not found for edit (fallback to Delete+Send)' };
    }

    await targetElement.hover();
    await page.waitForTimeout(500);

    const contextBtn = await targetElement.$('span[data-icon="down-context"], span[data-icon="chevron-down"]');
    if (!contextBtn) {
        return { isSuccess: false, errorDescription: 'Context menu not available for edit' };
    }

    await contextBtn.click();
    await page.waitForTimeout(600);

    const editMenuItem = await page.$('li:has-text("Редагувати"), div[role="button"]:has-text("Редагувати"), li:has-text("Edit"), div[role="button"]:has-text("Edit")');
    if (!editMenuItem) {
        await page.keyboard.press('Escape');
        return { isSuccess: false, errorDescription: 'Edit menu item not available (past window or unsupported)' };
    }

    await editMenuItem.click();
    await page.waitForTimeout(800);

    // Edit input box appears
    const editInput = await page.$('footer div[contenteditable="true"], div[role="textbox"]');
    if (editInput) {
        await editInput.focus();
        await page.keyboard.press('Control+A');
        await page.keyboard.press('Backspace');
        await page.keyboard.insertText(newText);
        await page.waitForTimeout(500);

        const checkBtn = await page.$('span[data-icon="check"], button[aria-label="Підтвердити"], button[aria-label="Confirm"]');
        if (checkBtn) {
            await checkBtn.click();
        } else {
            await page.keyboard.press('Enter');
        }
        await page.waitForTimeout(1500);
        return { isSuccess: true, messageId: messageId };
    }

    return { isSuccess: false, errorDescription: 'Failed to access edit input' };
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
    getPage: () => page
};
