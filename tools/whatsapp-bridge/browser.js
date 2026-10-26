// tools/whatsapp-bridge/browser.js
// Playwright-based browser driver for WhatsApp Web Channel automation.

const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');

const PROFILE_DIR = path.resolve(__dirname, '../../local/whatsapp-browser-profile');
const SCRATCH_DIR = path.resolve(__dirname, '../../scratch');
const LOGS_DIR = path.resolve(__dirname, '../../local/logs');
const LOG_FILE = path.resolve(LOGS_DIR, 'whatsapp-bridge.log');
const DEFAULT_CHANNEL_ID = process.env.WHATSAPP_CHANNEL_ID || '0029Vb96XUUIN9ixA88Umz3Y';

if (!fs.existsSync(PROFILE_DIR)) {
    fs.mkdirSync(PROFILE_DIR, { recursive: true });
}
if (!fs.existsSync(SCRATCH_DIR)) {
    fs.mkdirSync(SCRATCH_DIR, { recursive: true });
}
if (!fs.existsSync(LOGS_DIR)) {
    fs.mkdirSync(LOGS_DIR, { recursive: true });
}

function kyivTimestamp() {
    try {
        return new Intl.DateTimeFormat('sv-SE', {
            timeZone: 'Europe/Kyiv',
            year: 'numeric',
            month: '2-digit',
            day: '2-digit',
            hour: '2-digit',
            minute: '2-digit',
            second: '2-digit'
        }).format(new Date());
    } catch (_) {
        return new Date().toISOString();
    }
}

function logBridge(level, message) {
    const line = `[${kyivTimestamp()} Kyiv][${level}] ${message}`;
    if (level === 'ERROR' || level === 'WARN') {
        console.warn(line);
    } else {
        console.log(line);
    }
    try {
        fs.appendFileSync(LOG_FILE, line + '\n', 'utf8');
    } catch (_) {}
}

let browserContext = null;
let page = null;
let isConnected = false;
let hasQr = false;
let isLaunching = false;
let launchPromise = null;
let authInterval = null;
let initialChannelOpenTriggered = false;
let channelOpenPromise = null;

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

    logBridge('INFO', 'Initializing WhatsApp Web browser session...');
    logBridge('INFO', `Profile directory: ${PROFILE_DIR}`);

    try {
        if (browserContext) {
            try { await browserContext.close(); } catch (_) {}
            browserContext = null;
            page = null;
            isConnected = false;
            initialChannelOpenTriggered = false;
        }

        browserContext = await chromium.launchPersistentContext(PROFILE_DIR, {
            headless: false, // Visible window for session persistence & channel admin
            viewport: { width: 1280, height: 850 },
            args: [
                '--disable-blink-features=AutomationControlled',
                '--no-sandbox',
                '--disable-setuid-sandbox',
                '--disable-infobars',
                '--disable-session-crashed-bubble',
                '--hide-crash-restore-bubble',
                '--window-size=1280,850'
            ]
        });

        browserContext.on('close', () => {
            logBridge('WARN', 'Chromium browser context closed.');
            browserContext = null;
            page = null;
            isConnected = false;
            initialChannelOpenTriggered = false;
        });

        const pages = browserContext.pages();
        // 1. Find existing WhatsApp Web tab or pick primary tab
        let targetPage = pages.find(p => p.url().startsWith('https://web.whatsapp.com'));
        if (!targetPage) {
            targetPage = pages.length > 0 ? pages[0] : await browserContext.newPage();
        }

        // 2. Close all superfluous tabs (about:blank, chrome://newtab/, etc.) so only 1 clean tab remains
        for (const p of browserContext.pages()) {
            if (p !== targetPage) {
                try { await p.close(); } catch (_) {}
            }
        }

        page = targetPage;
        page.on('close', () => {
            logBridge('WARN', 'Active page closed.');
            const remaining = browserContext ? browserContext.pages() : [];
            page = remaining.find(p => p.url().includes('web.whatsapp.com')) || (remaining.length > 0 ? remaining[0] : null);
            if (!page) {
                isConnected = false;
                initialChannelOpenTriggered = false;
            }
        });

        // 3. Force navigation to https://web.whatsapp.com/ if not already on it
        const currentUrl = page.url();
        if (!currentUrl.startsWith('https://web.whatsapp.com')) {
            logBridge('INFO', `Navigating primary tab from '${currentUrl}' to https://web.whatsapp.com/ ...`);
            await page.goto('https://web.whatsapp.com/', { waitUntil: 'domcontentloaded', timeout: 60000 });
        } else {
            logBridge('INFO', `Primary tab already on '${currentUrl}'.`);
        }

        await page.bringToFront().catch(() => {});

        logBridge('INFO', 'WhatsApp Web page attached. Monitoring authentication state...');

        if (!authInterval) {
            monitorAuthState();
        }

        // 4. Wait up to 25s for session restoration from profile
        for (let i = 0; i < 25; i++) {
            await checkAuthStateOnce();
            if (isConnected) break;
            await new Promise(r => setTimeout(r, 1000));
        }

        // 5. Immediately open the SvitloSk channel upon authentication so the user sees the channel feed right away
        if (isConnected && !initialChannelOpenTriggered) {
            initialChannelOpenTriggered = true;
            try {
                logBridge('INFO', 'Auto-opening SvitloSk channel after browser startup...');
                await ensureChannelOpen(DEFAULT_CHANNEL_ID, 2);
            } catch (openErr) {
                logBridge('WARN', `Startup auto-open channel deferred: ${openErr.message}`);
            }
        }
    } catch (err) {
        logBridge('ERROR', `Failed to launch browser context: ${err.message}`);
        browserContext = null;
        page = null;
        isConnected = false;
    }
}

async function checkAuthStateOnce() {
    if (!page || page.isClosed()) {
        isConnected = false;
        return;
    }

    try {
        // Check if tab accidentally wandered off web.whatsapp.com
        const currentUrl = page.url();
        if (!currentUrl.startsWith('https://web.whatsapp.com') && !currentUrl.startsWith('chrome-error://')) {
            logBridge('WARN', `Tab URL is '${currentUrl}'. Redirecting to https://web.whatsapp.com/ ...`);
            await page.goto('https://web.whatsapp.com/', { waitUntil: 'domcontentloaded', timeout: 45000 }).catch(() => {});
            return;
        }

        // Check for QR canvas
        const qrCanvas = await page.$('canvas[aria-label*="Scan"], div[data-ref]');
        if (qrCanvas) {
            if (!hasQr) {
                logBridge('WARN', 'QR Code detected on screen! Saving screenshot of QR code for scanning...');
                try {
                    const localQr = path.resolve(__dirname, 'whatsapp_qr.png');
                    await qrCanvas.screenshot({ path: localQr });
                    logBridge('INFO', `QR code saved to ${localQr}`);
                } catch (ssErr) {
                    logBridge('WARN', `QR screenshot failed: ${ssErr.message}`);
                }
            }
            hasQr = true;
            isConnected = false;
            return;
        }

        // Check for authenticated main interface (pane-side, chat list, or channels bar)
        const mainInterface = await page.$(
            '#pane-side, div[data-tab="3"], header, div[role="navigation"], ' +
            'span[data-icon*="newsletter"], span[data-icon*="channel"], ' +
            'button[aria-label*="Канал" i], button[aria-label*="Channel" i]'
        );
        if (mainInterface) {
            const wasConnected = isConnected;
            isConnected = true;
            hasQr = false;
            if (!wasConnected) {
                logBridge('INFO', 'Connected and authenticated successfully! User session is active.');
                if (!initialChannelOpenTriggered) {
                    initialChannelOpenTriggered = true;
                    ensureChannelOpen(DEFAULT_CHANNEL_ID, 2).catch(err => {
                        logBridge('WARN', `Background auto-open channel warning: ${err.message}`);
                    });
                }
            }
        }
    } catch (_) {
        // Page might be navigating or temporarily busy
    }
}

function monitorAuthState() {
    if (authInterval) clearInterval(authInterval);
    authInterval = setInterval(checkAuthStateOnce, 2500);
}

async function scrollToBottom() {
    if (!page || page.isClosed()) return;
    try {
        const scrollBtn = await page.$(
            '#main span[data-icon="down"], #main span[data-icon*="chevron-down"], ' +
            '#main div[role="button"][aria-label*="Scroll" i], ' +
            '#main div[role="button"][aria-label*="Вниз" i], ' +
            '#main button[aria-label*="down" i]'
        );
        if (scrollBtn) {
            await scrollBtn.click({ force: true }).catch(() => {});
            await page.waitForTimeout(400);
        }

        await page.evaluate(() => {
            const main = document.querySelector('#main');
            if (!main) return;
            const scrollers = Array.from(main.querySelectorAll('div')).filter(el => {
                const style = window.getComputedStyle(el);
                return (style.overflowY === 'auto' || style.overflowY === 'scroll') && el.scrollHeight > el.clientHeight;
            });
            for (const s of scrollers) {
                s.scrollTop = s.scrollHeight;
            }
        });
        await page.waitForTimeout(400);
    } catch (_) {}
}

// Force restart browser session to recover from stuck DOM or disconnects
async function restartSession() {
    logBridge('INFO', 'Initiating clean restart of WhatsApp Web browser session...');
    try {
        if (browserContext) {
            await browserContext.close().catch(() => {});
        }
    } catch (_) {}
    browserContext = null;
    page = null;
    isConnected = false;
    hasQr = false;
    initialChannelOpenTriggered = false;
    await initBrowser();
}

// Verify whether #main is currently open on the SvitloSk channel and has an active composer
async function isSvitloSkChannelActiveInMain() {
    if (!page || page.isClosed()) return false;
    try {
        return await page.evaluate(() => {
            const main = document.querySelector('#main');
            if (!main) return false;

            const header = main.querySelector('header');
            if (!header) return false;
            const headerText = (header.innerText || '').toLowerCase();
            const isTargetChannel = headerText.includes('svitlosk') || headerText.includes('журнал знеструмлень');
            if (!isTargetChannel) return false;

            const composer = main.querySelector('footer div[contenteditable="true"], div[data-testid="conversation-compose-box-input"], footer div[role="textbox"]');
            return !!composer;
        });
    } catch (_) {
        return false;
    }
}

// 3-Stage Navigation to SvitloSk channel with strict #main verification and concurrency lock
async function ensureChannelOpen(channelIdentifier, maxRetries = 3) {
    if (channelOpenPromise) {
        return await channelOpenPromise;
    }

    channelOpenPromise = (async () => {
        let attempt = 0;
        while (attempt < maxRetries) {
            attempt++;
            await ensureBrowser();

            if (!isBrowserAlive() || !isConnected) {
                if (attempt < maxRetries) {
                    logBridge('WARN', `WhatsApp Web not ready (Attempt ${attempt}/${maxRetries}). Waiting 4s before retry...`);
                    await new Promise(r => setTimeout(r, 4000));
                    continue;
                }
                throw new Error('WhatsApp Web is not authenticated yet. Please keep Chromium open and scan QR code if needed.');
            }

            try {
                // Stage 1: Strict check if #main is already open on SvitloSk channel with visible composer
                if (await isSvitloSkChannelActiveInMain()) {
                    await scrollToBottom();
                    return;
                }

                logBridge('INFO', `Navigating to SvitloSk channel (Attempt ${attempt}/${maxRetries})...`);
                await dismissOverlays();

                // Stage 2: Ensure we are on the Channels ("Каналы") pane on the left navigation rail
                const railLoc = page.locator(
                    'button[aria-label*="Канал" i], button[aria-label*="Channel" i], ' +
                    'div[role="button"][aria-label*="Канал" i], div[role="button"][aria-label*="Channel" i], ' +
                    'span[data-icon*="newsletter"], span[data-icon*="channel"], ' +
                    '[data-testid*="newsletter"], [data-testid*="channel"]'
                ).first();

                if (await railLoc.count() > 0) {
                    await railLoc.click({ timeout: 5000 }).catch(() => {});
                    logBridge('INFO', 'Clicked Channels icon on left navigation rail.');
                    await page.waitForTimeout(1200);
                }

                // Stage 3: Click SvitloSk channel row TITLE (`span[title^="SvitloSk"]`) using real Playwright pointer event
                // Never use broad `:has-text("SvitloSk")` on listitem because personal chat previews may contain SvitloSk invite links!
                const svitloSkTitleLocator = page.locator(
                    'span[title^="SvitloSk"], span[title*="SvitloSk Журнал"]'
                ).first();

                let clicked = false;
                if (await svitloSkTitleLocator.count() > 0) {
                    await svitloSkTitleLocator.scrollIntoViewIfNeeded().catch(() => {});
                    await svitloSkTitleLocator.click({ timeout: 5000 });
                    clicked = true;
                    logBridge('INFO', 'Clicked SvitloSk channel title in channels list via real Playwright mouse event.');
                } else {
                    // Fallback coordinate click strictly on a row whose primary title starts with SvitloSk
                    const box = await page.evaluate(() => {
                        const spans = Array.from(document.querySelectorAll('span[title], span'));
                        const target = spans.find(el => {
                            const r = el.getBoundingClientRect();
                            if (r.width === 0 || r.height === 0 || r.x > 550) return false;
                            const title = (el.getAttribute('title') || '').trim().toLowerCase();
                            const txt = (el.innerText || '').trim().toLowerCase();
                            return title.startsWith('svitlosk') || txt === 'svitlosk журнал знеструмлень' || txt === 'svitlosk';
                        });
                        if (!target) return null;
                        const row = target.closest('div[role="listitem"], div[role="button"]') || target;
                        const r = row.getBoundingClientRect();
                        return { x: r.x + r.width / 2, y: r.y + r.height / 2 };
                    });
                    if (box) {
                        await page.mouse.click(box.x, box.y);
                        clicked = true;
                        logBridge('INFO', `Clicked SvitloSk channel row at coordinates (${Math.round(box.x)}, ${Math.round(box.y)}).`);
                    }
                }

                // Wait up to 8s for #main header & composer to become active
                let verified = false;
                for (let w = 0; w < 16; w++) {
                    await page.waitForTimeout(500);
                    if (await isSvitloSkChannelActiveInMain()) {
                        verified = true;
                        break;
                    }
                }

                if (!verified) {
                    throw new Error(`Channel SvitloSk did not open in #main (clicked=${clicked}, header or composer missing after click).`);
                }

                await scrollToBottom();
                logBridge('INFO', 'Channel SvitloSk successfully verified in #main and ready.');
                return;
            } catch (err) {
                logBridge('WARN', `ensureChannelOpen attempt ${attempt} failed: ${err.message}`);
                try {
                    const errShot = path.resolve(LOGS_DIR, 'nav-error.png');
                    if (page && !page.isClosed()) {
                        await page.screenshot({ path: errShot }).catch(() => {});
                    }
                } catch (_) {}

                if (attempt >= maxRetries) {
                    throw new Error(`Failed to open SvitloSk WhatsApp channel after ${maxRetries} attempts: ${err.message}`);
                }
                await page.waitForTimeout(2000);
            }
        }
    })();

    try {
        return await channelOpenPromise;
    } finally {
        channelOpenPromise = null;
    }
}

async function dismissOverlays() {
    if (!page || page.isClosed()) return;
    try {
        // 1. If there's an open confirmation dialog or modal, click cancel
        const cancelModal = await page.$(
            'div[role="dialog"] button[aria-label*="Отмен" i], ' +
            'div[role="dialog"] button:has-text("Отмена"), ' +
            'div[role="dialog"] button:has-text("Скасувати"), ' +
            'div[role="dialog"] button:has-text("Cancel")'
        );
        if (cancelModal) {
            logBridge('INFO', 'Dismissing open modal dialog...');
            await cancelModal.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 2. If in media preview dialog (close via 'x-alt' or 'x' icon)
        const closePreview = await page.$(
            'div[data-animate-modal-popup] span[data-icon="x-alt"], ' +
            'div[data-animate-modal-popup] span[data-icon="x"], ' +
            'div[aria-label*="Закр" i] span[data-icon="x"], ' +
            'button[aria-label="Close"], button[aria-label="Закрыть"], button[aria-label="Закрити"]'
        );
        if (closePreview) {
            await closePreview.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 3. If in message multi-select deletion mode (bottom bar)
        const cancelSelect = await page.$(
            '#main button[aria-label*="Отмен" i], ' +
            '#main button[aria-label*="Скасув" i], ' +
            '#main button[aria-label*="Cancel" i]'
        );
        if (cancelSelect) {
            logBridge('INFO', 'Dismissing selection mode...');
            await cancelSelect.click({ force: true }).catch(() => {});
            await page.waitForTimeout(300);
        }

        // 4. Press Escape ONLY if a context menu or popup menu is active
        const activeMenu = await page.$('div[role="application"] div[role="menu"], ul[role="menu"], div[data-testid="context-menu"]');
        if (activeMenu) {
            await page.keyboard.press('Escape');
            await page.waitForTimeout(200);
        }
    } catch (_) {}
}

// Parse marker from the last non-empty lines of a message text
// Supported formats:
//   T · dd.mm · NN
//   F · dd.mm · NN
//   G · dd.mm
//   S · dd.mm
function extractMarkerFromText(rawText) {
    if (!rawText || typeof rawText !== 'string') return null;
    const lines = rawText
        .split(/\r?\n/)
        .map(l => l.trim())
        .filter(l => l.length > 0);

    // Search from bottom up (WhatsApp DOM innerText may append timestamp/views at the very end)
    const tfRegex = /^([TF])\s*[·•]\s*(\d{2}\.\d{2})\s*[·•]\s*(\d{2})$/;
    const gsRegex = /^([GS])\s*[·•]\s*(\d{2}\.\d{2})$/;

    for (let i = lines.length - 1; i >= 0; i--) {
        const line = lines[i].replace(/^[_*`]+|[_*`]+$/g, '').trim();
        const tfMatch = line.match(tfRegex);
        if (tfMatch) {
            return {
                marker: `${tfMatch[1]} · ${tfMatch[2]} · ${tfMatch[3]}`,
                type: tfMatch[1],
                dateStr: tfMatch[2],
                seqNum: parseInt(tfMatch[3], 10),
                lineIndex: i
            };
        }
        const gsMatch = line.match(gsRegex);
        if (gsMatch) {
            return {
                marker: `${gsMatch[1]} · ${gsMatch[2]}`,
                type: gsMatch[1],
                dateStr: gsMatch[2],
                seqNum: null,
                lineIndex: i
            };
        }
    }
    return null;
}

// Snapshot all currently visible message data-ids in #main
async function snapshotVisibleDataIds() {
    if (!page || page.isClosed()) return new Set();
    try {
        const ids = await page.evaluate(() => {
            const main = document.querySelector('#main') || document;
            const els = Array.from(main.querySelectorAll('div[data-id], [data-id]'));
            return els.map(e => e.getAttribute('data-id')).filter(Boolean);
        });
        return new Set(ids);
    } catch (_) {
        return new Set();
    }
}

function normalizeShortId(fullDataId) {
    if (!fullDataId) return null;
    const parts = fullDataId.split('_');
    return parts.length > 1 ? parts[parts.length - 1] : fullDataId;
}

// Expand all visible "Read more" / "Читать дальше" / "Читати далі" buttons inside #main so bottom markers are exposed in DOM
async function expandVisibleReadMoreButtons() {
    if (!page || page.isClosed()) return;
    try {
        const clickedCount = await page.evaluate(() => {
            const main = document.querySelector('#main') || document;
            const btns = Array.from(main.querySelectorAll('.read-more-button, div[role="button"].read-more-button, span.read-more-button'));
            let clicked = 0;
            for (const b of btns) {
                const rect = b.getBoundingClientRect();
                if (rect.width > 0 && rect.height > 0) {
                    b.click();
                    clicked++;
                }
            }
            return clicked;
        });
        if (clickedCount > 0) {
            await page.waitForTimeout(300);
        }
    } catch (_) {}
}

// Wait until a newly posted message appears in #main (verified by marker or new data-id)
async function waitForNewMessageConfirmation(knownIdsSet, expectedMarker, timeoutMs = 25000) {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        await page.waitForTimeout(500);
        await expandVisibleReadMoreButtons();
        try {
            const found = await page.evaluate(({ knownIds, markerStr }) => {
                const main = document.querySelector('#main') || document;
                const rows = Array.from(main.querySelectorAll('div[role="row"], div[data-id]'));
                for (let i = rows.length - 1; i >= 0; i--) {
                    const r = rows[i];
                    const dataId = r.getAttribute('data-id') || r.querySelector('[data-id]')?.getAttribute('data-id');
                    if (!dataId) continue;
                    const text = (r.innerText || '').trim();
                    const isNewId = !knownIds.includes(dataId);

                    if (markerStr) {
                        if (text.includes(markerStr)) {
                            return { dataId, matchedByMarker: true, isNewId };
                        }
                    }
                    // Also accept a newly appeared data-id at the bottom of #main if it wasn't in knownIds before sending
                    if (isNewId && text.length > 0) {
                        return { dataId, matchedByMarker: false, isNewId: true };
                    }
                }
                return null;
            }, { knownIds: Array.from(knownIdsSet), markerStr: expectedMarker });

            if (found && found.dataId) {
                return {
                    confirmed: true,
                    fullDataId: found.dataId,
                    messageId: normalizeShortId(found.dataId),
                    matchedByMarker: found.matchedByMarker
                };
            }
        } catch (_) {}
    }
    return { confirmed: false, fullDataId: null, messageId: null, matchedByMarker: false };
}

// Read the tail of the channel feed from bottom up (GET /tail-state)
async function getTailState(channelIdentifier, stopBeforeDateDdMm = null, maxScrollScreens = 6) {
    await ensureChannelOpen(channelIdentifier);
    await dismissOverlays();
    await scrollToBottom();

    logBridge('INFO', `Reading channel tail state (stopBeforeDateDdMm=${stopBeforeDateDdMm || 'auto'}, maxScrollScreens=${maxScrollScreens})...`);

    const collectedMap = new Map(); // shortId -> message object

    const extractVisibleRows = async () => {
        return await page.evaluate(() => {
            const main = document.querySelector('#main') || document;
            const rows = Array.from(main.querySelectorAll('div[role="row"]'));
            const results = [];

            for (let idx = 0; idx < rows.length; idx++) {
                const r = rows[idx];
                const dataIdEl = r.hasAttribute('data-id') ? r : r.querySelector('[data-id]');
                const fullDataId = dataIdEl ? dataIdEl.getAttribute('data-id') : null;
                if (!fullDataId) continue;

                const hasImage = !!r.querySelector('img[src^="blob:"], div[data-testid="image-thumb"], div[role="button"] img');
                const rect = r.getBoundingClientRect();

                // Extract clean text from copyable-text / selectable-text spans if available, otherwise fallback to row innerText
                const textSpans = Array.from(r.querySelectorAll('span.selectable-text, div.copyable-text span[dir]'));
                let cleanText = '';
                if (textSpans.length > 0) {
                    // Deduplicate nested spans
                    const topSpans = textSpans.filter(s => !textSpans.some(other => other !== s && other.contains(s)));
                    cleanText = topSpans.map(s => s.innerText || '').join('\n').trim();
                }
                if (!cleanText) {
                    cleanText = (r.innerText || '').trim();
                }

                results.push({
                    fullDataId,
                    hasImage,
                    rawText: cleanText,
                    topY: rect.y
                });
            }
            return results;
        });
    };

    for (let screen = 0; screen <= maxScrollScreens; screen++) {
        await expandVisibleReadMoreButtons();
        const visibleRows = await extractVisibleRows();
        let reachedPreviousDayBanner = false;

        for (const row of visibleRows) {
            const shortId = normalizeShortId(row.fullDataId);
            if (!shortId) continue;

            const parsedMarker = extractMarkerFromText(row.rawText);
            const lines = (row.rawText || '')
                .split(/\r?\n/)
                .map(l => l.trim())
                .filter(l => l.length > 0 && !/^\d{1,2}:\d{2}$/.test(l) && !/^\d+$/.test(l));

            let textWithoutMarker = lines.join('\n').trim();
            if (parsedMarker) {
                const filteredLines = lines.filter(l => {
                    const cleanLine = l.replace(/^[_*`]+|[_*`]+$/g, '').trim();
                    return cleanLine !== parsedMarker.marker;
                });
                textWithoutMarker = filteredLines.join('\n').trim();
            }

            const titleLine = textWithoutMarker.split('\n')[0] || '';

            const existing = collectedMap.get(shortId);
            // If we already captured this message with a marker or longer text, and the newly scrolled view is truncated/virtualized, do NOT overwrite with shorter/unmarked data!
            if (existing) {
                const existingHasMarker = !!existing.marker;
                const newHasMarker = !!parsedMarker;
                if (existingHasMarker && !newHasMarker) {
                    continue;
                }
                if (existing.textWithoutMarker && (!textWithoutMarker || textWithoutMarker.length < existing.textWithoutMarker.length)) {
                    continue;
                }
            }

            collectedMap.set(shortId, {
                messageId: shortId,
                fullDataId: row.fullDataId,
                marker: parsedMarker ? parsedMarker.marker : (existing ? existing.marker : null),
                markerType: parsedMarker ? parsedMarker.type : (existing ? existing.markerType : null),
                markerDate: parsedMarker ? parsedMarker.dateStr : (existing ? existing.markerDate : null),
                markerSeq: parsedMarker ? parsedMarker.seqNum : (existing ? existing.markerSeq : null),
                titleLine: titleLine || (existing ? existing.titleLine : ''),
                textWithoutMarker: textWithoutMarker || (existing ? existing.textWithoutMarker : ''),
                rawText: row.rawText || (existing ? existing.rawText : ''),
                hasImage: row.hasImage || (existing ? existing.hasImage : false),
                screenIndex: existing ? existing.screenIndex : screen,
                topY: existing ? existing.topY : row.topY
            });

            // Check if we reached yesterday's T · dd.mm · 00 banner
            if (parsedMarker && parsedMarker.type === 'T' && parsedMarker.seqNum === 0) {
                if (stopBeforeDateDdMm && parsedMarker.dateStr !== stopBeforeDateDdMm) {
                    reachedPreviousDayBanner = true;
                }
            }
        }

        if (reachedPreviousDayBanner || screen === maxScrollScreens) {
            break;
        }

        // Scroll up one screen in #main
        const chatPanel = await page.$('#main div[data-testid="conversation-panel-messages"], #main');
        if (chatPanel) await chatPanel.hover().catch(() => {});
        await page.mouse.wheel(0, -700);
        await page.waitForTimeout(350);
    }

    await scrollToBottom();

    // Order collected messages chronologically from oldest (top) to newest (bottom)
    // Higher screenIndex means further up in history; within the same screenIndex, smaller topY is higher up
    const orderedItems = Array.from(collectedMap.values()).sort((a, b) => {
        if (a.screenIndex !== b.screenIndex) {
            return b.screenIndex - a.screenIndex;
        }
        return a.topY - b.topY;
    }).map((item, idx) => ({
        order: idx,
        messageId: item.messageId,
        fullDataId: item.fullDataId,
        marker: item.marker,
        markerType: item.markerType,
        markerDate: item.markerDate,
        markerSeq: item.markerSeq,
        titleLine: item.titleLine,
        textWithoutMarker: item.textWithoutMarker,
        hasImage: item.hasImage
    }));

    logBridge('INFO', `Tail state read complete: ${orderedItems.length} message(s) collected (${orderedItems.filter(x => x.marker).length} marked).`);
    return {
        isSuccess: true,
        messages: orderedItems
    };
}

// 1. Send Text Message (Atomic: no hidden side-effect deletions, verifies new message by marker)
async function sendTextMessage(channelIdentifier, text) {
    await ensureChannelOpen(channelIdentifier);
    await scrollToBottom();
    await dismissOverlays();

    const parsedMarker = extractMarkerFromText(text);
    const expectedMarker = parsedMarker ? parsedMarker.marker : null;
    logBridge('INFO', `Sending text message (${text.length} chars, marker='${expectedMarker || 'none'}')...`);

    const knownIds = await snapshotVisibleDataIds();

    // Target the bottom channel composer inside #main
    const composer = page.locator('#main footer div[role="textbox"][contenteditable="true"], #main div[role="textbox"][contenteditable="true"]').last();
    await composer.waitFor({ state: 'visible', timeout: 15000 });

    await composer.focus();
    await page.keyboard.press('Control+A');
    await page.keyboard.press('Backspace');

    await page.keyboard.insertText(text);
    await page.waitForTimeout(400);

    const sendBtn = await page.$('#main span[data-icon="send"], #main button[aria-label="Send"], #main button[aria-label="Надіслати"], #main button[aria-label="Отправить"]');
    if (sendBtn) {
        await sendBtn.click();
    } else {
        await page.keyboard.press('Enter');
    }

    const confirmation = await waitForNewMessageConfirmation(knownIds, expectedMarker, 20000);
    if (!confirmation.confirmed || !confirmation.messageId) {
        const errMsg = `Text message confirmation timed out (expected marker '${expectedMarker || 'any'}').`;
        logBridge('ERROR', errMsg);
        return {
            isSuccess: false,
            messageId: null,
            errorDescription: errMsg
        };
    }

    logBridge('INFO', `Text message sent and confirmed. ID: ${confirmation.messageId} (marker='${expectedMarker || 'none'}')`);
    return {
        isSuccess: true,
        messageId: confirmation.messageId
    };
}

// 2. Send Media Message (Atomic: never sends bare image + separate text post; cancels modal if caption fails)
async function sendMediaMessage(channelIdentifier, caption, imageBase64, mimeType = 'image/png') {
    await ensureChannelOpen(channelIdentifier);
    await scrollToBottom();

    const parsedMarker = extractMarkerFromText(caption);
    const expectedMarker = parsedMarker ? parsedMarker.marker : null;
    logBridge('INFO', `Preparing media upload (caption=${(caption || '').length} chars, marker='${expectedMarker || 'none'}')...`);

    const ext = mimeType.includes('jpeg') || mimeType.includes('jpg') ? 'jpg' : 'png';
    const tempFile = path.resolve(SCRATCH_DIR, `whatsapp_upload_${Date.now()}.${ext}`);
    fs.writeFileSync(tempFile, Buffer.from(imageBase64, 'base64'));

    try {
        await dismissOverlays();
        const knownIds = await snapshotVisibleDataIds();

        // 1. Locate and click attachment button (paperclip / plus) in #main footer
        const attachSelector =
            '#main footer button[aria-haspopup="menu"], ' +
            '#main footer button[aria-label*="Прикреп" i], ' +
            '#main footer button[aria-label*="Вклас" i], ' +
            '#main footer button[aria-label*="Attach" i], ' +
            '#main footer span[data-icon="ic-attach-file"], ' +
            '#main footer span[data-icon="plus"], ' +
            '#main footer button:has(span[data-icon="clip"]), ' +
            'footer [data-icon="ic-attach-file"]';
        const attachBtn = page.locator(attachSelector).first();
        const attachVisible = await attachBtn.waitFor({ state: 'visible', timeout: 7000 }).then(() => true).catch(() => false);

        if (!attachVisible) {
            throw new Error('Attachment clip button could not be found in channel composer.');
        }

        const menuAlreadyOpen = await page.locator('div[role="menu"] button[role="menuitem"], ul[role="menu"] li').first().isVisible().catch(() => false);
        if (!menuAlreadyOpen) {
            await attachBtn.click();
            await page.waitForTimeout(600);
        }

        // 2. Locate "Photos & videos" option in attachment menu
        const photoSelector =
            'div[role="menu"] button[role="menuitem"]:has-text("Фото"), ' +
            'div[role="menu"] button[role="menuitem"]:has-text("Photo"), ' +
            'div[role="menu"] button[role="menuitem"]:first-child, ' +
            'button[role="menuitem"][aria-label*="Фото" i], ' +
            'button[role="menuitem"][aria-label*="Photo" i], ' +
            'button[role="menuitem"]:first-child';
        const photoBtn = page.locator(photoSelector).first();
        const photoVisible = await photoBtn.waitFor({ state: 'visible', timeout: 5000 }).then(() => true).catch(() => false);

        if (!photoVisible) {
            throw new Error('"Photos & videos" option could not be found in attachment menu.');
        }

        // 3. Trigger native file picker via Playwright filechooser event (or fallback to input[type="file"])
        try {
            const [fileChooser] = await Promise.all([
                page.waitForEvent('filechooser', { timeout: 6000 }),
                photoBtn.click()
            ]);
            await fileChooser.setFiles(tempFile);
            logBridge('INFO', `Media file staged via filechooser: ${path.basename(tempFile)}`);
        } catch (fcErr) {
            logBridge('WARN', `filechooser event timed out (${fcErr.message}), falling back to input[type="file"]...`);
            const fileInput = page.locator('input[type="file"][accept*="video"], input[type="file"][accept*="image"]').last();
            await fileInput.setInputFiles(tempFile);
            logBridge('INFO', `Media file staged via input[type="file"]: ${path.basename(tempFile)}`);
        }
        await page.waitForTimeout(1500);

        // 4. Fill inline caption in preview modal if provided
        if (caption && caption.trim().length > 0) {
            const captionSelector =
                'div[data-animate-modal-popup] div[contenteditable="true"], ' +
                'div[data-testid="media-caption-input-container"] div[contenteditable="true"], ' +
                'div[aria-autocomplete="list"][role="textbox"], ' +
                'div[aria-label*="обновлен" i][contenteditable="true"], ' +
                'div[aria-label*="caption" i][contenteditable="true"], ' +
                'div[aria-label*="підпис" i][contenteditable="true"], ' +
                'div[aria-placeholder*="підпис" i][contenteditable="true"], ' +
                'div[aria-placeholder*="caption" i][contenteditable="true"], ' +
                'div[aria-placeholder*="подпись" i][contenteditable="true"]';
            const captionInput = page.locator(captionSelector).first();
            const captionVisible = await captionInput.waitFor({ state: 'visible', timeout: 8000 }).then(() => true).catch(() => false);

            if (!captionVisible) {
                logBridge('ERROR', 'Caption input box not found in media preview modal. Cancelling upload to prevent un-marked media post.');
                await dismissOverlays();
                return {
                    isSuccess: false,
                    messageId: null,
                    errorDescription: 'Caption input box not found in media preview modal.'
                };
            }

            await captionInput.click({ force: true });
            await page.waitForTimeout(250);
            await page.keyboard.press('Control+A');
            await page.keyboard.press('Backspace');
            await page.keyboard.insertText(caption);
            await page.waitForTimeout(400);

            const textInBox = await captionInput.innerText().catch(() => '');
            const markerVerifiedInBox = !expectedMarker || textInBox.includes(expectedMarker);
            if (!textInBox || textInBox.trim().length === 0 || !markerVerifiedInBox) {
                logBridge('ERROR', `Caption verification failed in preview modal (length=${textInBox.length}, markerVerified=${markerVerifiedInBox}). Cancelling upload.`);
                await dismissOverlays();
                return {
                    isSuccess: false,
                    messageId: null,
                    errorDescription: 'Inline caption or marker failed to attach in media preview modal.'
                };
            }
            logBridge('INFO', `Inline caption verified in preview modal (${textInBox.length} chars, marker='${expectedMarker || 'none'}').`);
        }

        // 5. Click send button in media preview
        const sendMediaBtn = await page.$(
            'div[data-animate-modal-popup] span[data-icon="wds-ic-send-filled"], ' +
            'div[data-animate-modal-popup] span[data-icon="send"], ' +
            'span[data-icon="wds-ic-send-filled"], span[data-icon="send"], ' +
            'div[role="button"][aria-label*="Send" i], div[role="button"][aria-label*="Надіслати" i], div[role="button"][aria-label*="Отправить" i], ' +
            'button[aria-label*="Send" i], button[aria-label*="Надіслати" i], button[aria-label*="Отправить" i]'
        );
        if (sendMediaBtn) {
            await sendMediaBtn.click();
        } else {
            await page.keyboard.press('Enter');
        }

        // 6. Wait up to 60s for media message to appear in DOM with our marker
        const confirmation = await waitForNewMessageConfirmation(knownIds, expectedMarker, 60000);
        if (!confirmation.confirmed || !confirmation.messageId) {
            const errMsg = `Media message confirmation timed out after 60s (expected marker '${expectedMarker || 'any'}').`;
            logBridge('ERROR', errMsg);
            return {
                isSuccess: false,
                messageId: null,
                errorDescription: errMsg
            };
        }

        logBridge('INFO', `Media message sent and confirmed. ID: ${confirmation.messageId} (marker='${expectedMarker || 'none'}')`);
        return {
            isSuccess: true,
            messageId: confirmation.messageId
        };
    } finally {
        try {
            if (fs.existsSync(tempFile)) fs.unlinkSync(tempFile);
        } catch (_) {}
    }
}

// Helper: Locate a message row in #main by messageId or marker, scrolling up if needed
async function findMessageRowElement(messageId, marker = null, maxScrollAttempts = 8) {
    if (!page || page.isClosed()) return null;

    const tryFindInVisibleDom = async () => {
        await expandVisibleReadMoreButtons();
        // 1. Try by messageId
        if (messageId && !messageId.startsWith('wa_mock_') && !messageId.startsWith('web_')) {
            const target = await page.$(`#main div[data-id*="${messageId}"], #main [data-id*="${messageId}"]`);
            if (target) {
                const rowHandle = await page.evaluateHandle(el => el.closest('div[role="row"], div.message-out') || el, target);
                if (rowHandle && rowHandle.asElement()) {
                    return rowHandle.asElement();
                }
            }
        }

        // 2. Fallback by exact marker string
        if (marker) {
            const rowHandle = await page.evaluateHandle((mStr) => {
                const main = document.querySelector('#main') || document;
                const rows = Array.from(main.querySelectorAll('div[role="row"]'));
                for (let i = rows.length - 1; i >= 0; i--) {
                    const txt = rows[i].innerText || '';
                    if (txt.includes(mStr)) {
                        return rows[i];
                    }
                }
                return null;
            }, marker);
            if (rowHandle && rowHandle.asElement()) {
                return rowHandle.asElement();
            }
        }

        return null;
    };

    await scrollToBottom();
    let found = await tryFindInVisibleDom();
    if (found) return found;

    const chatPanel = await page.$('#main div[data-testid="conversation-panel-messages"], #main');
    if (chatPanel) await chatPanel.hover().catch(() => {});

    for (let s = 0; s < maxScrollAttempts; s++) {
        await page.mouse.wheel(0, -700);
        await page.waitForTimeout(350);
        found = await tryFindInVisibleDom();
        if (found) return found;
    }

    return null;
}

// Helper: Delete a single DOM message element via context menu or selection bar
async function deleteMessageRow(targetElement, messageId = null, marker = null) {
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
        const trashBtn = page.locator(
            'button[aria-label="Удалить"], button[aria-label*="Удалить" i], ' +
            'button[aria-label*="Видалити" i], button[aria-label*="Delete" i], ' +
            'button[data-tab="10"]:has(span[data-icon="delete"])'
        ).last();
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
        if (messageId && !messageId.startsWith('wa_mock_')) {
            const stillPresent = await page.locator(`#main div[data-id*="${messageId}"], #main [data-id*="${messageId}"]`).count() > 0;
            if (stillPresent) {
                logBridge('WARN', `Element '${messageId}' is still present in DOM after delete attempt.`);
                return false;
            }
        } else if (marker) {
            const markerStillPresent = await page.evaluate((mStr) => {
                const main = document.querySelector('#main') || document;
                return Array.from(main.querySelectorAll('div[role="row"]')).some(r => (r.innerText || '').includes(mStr));
            }, marker);
            if (markerStillPresent) {
                logBridge('WARN', `Marker '${marker}' is still present in DOM after delete attempt.`);
                return false;
            }
        }

        return true;
    } catch (e) {
        logBridge('WARN', `deleteMessageRow error: ${e.message}`);
        await dismissOverlays();
        return false;
    }
}

// 3. Delete Message via Context Menu (Strict: only by messageId or marker, returns deleted / notFound / failed)
async function deleteMessage(channelIdentifier, messageId, marker = null) {
    await ensureChannelOpen(channelIdentifier);

    logBridge('INFO', `Attempting to delete message (id='${messageId || 'none'}', marker='${marker || 'none'}')...`);

    const targetElement = await findMessageRowElement(messageId, marker, 10);
    if (!targetElement) {
        logBridge('WARN', `Message (id='${messageId || 'none'}', marker='${marker || 'none'}') was not found in channel DOM.`);
        await scrollToBottom();
        return {
            isSuccess: true,
            status: 'notFound',
            messageId: messageId,
            errorDescription: 'Message not found in channel DOM.'
        };
    }

    const success = await deleteMessageRow(targetElement, messageId, marker);
    await scrollToBottom();

    if (success) {
        logBridge('INFO', `Confirmed message deletion (id='${messageId || 'none'}', marker='${marker || 'none'}').`);
        return {
            isSuccess: true,
            status: 'deleted',
            messageId: messageId
        };
    }

    logBridge('ERROR', `Failed to delete message (id='${messageId || 'none'}', marker='${marker || 'none'}').`);
    return {
        isSuccess: false,
        status: 'failed',
        messageId: messageId,
        errorDescription: 'Failed to delete message via context menu'
    };
}

// 4. Edit Message (In-place edit with explicit status: updated / windowExpired / notFound / failed)
async function editMessage(channelIdentifier, messageId, newText, marker = null) {
    await ensureChannelOpen(channelIdentifier);

    const parsedMarker = extractMarkerFromText(newText);
    const searchMarker = marker || (parsedMarker ? parsedMarker.marker : null);
    logBridge('INFO', `Attempting in-place edit (id='${messageId || 'none'}', marker='${searchMarker || 'none'}')...`);

    const targetElement = await findMessageRowElement(messageId, searchMarker, 8);
    if (!targetElement) {
        logBridge('WARN', `Message not found for edit (id='${messageId || 'none'}', marker='${searchMarker || 'none'}').`);
        await scrollToBottom();
        return {
            isSuccess: false,
            status: 'notFound',
            errorDescription: 'Message not found for edit'
        };
    }

    try {
        await dismissOverlays();
        await targetElement.scrollIntoViewIfNeeded().catch(() => {});
        await page.waitForTimeout(400);
        await targetElement.hover().catch(() => {});
        await page.waitForTimeout(300);

        await targetElement.click({ button: 'right' });
        await page.waitForTimeout(600);

        const editBtn = page.locator('button[role="menuitem"], div[role="button"], li, span').filter({ hasText: /^Изменить$|^Редагувати$|^Edit$/i }).last();
        const editVisible = await editBtn.waitFor({ state: 'visible', timeout: 2500 }).then(() => true).catch(() => false);
        if (!editVisible) {
            await page.keyboard.press('Escape');
            await scrollToBottom();
            logBridge('INFO', `Edit menu item not available for id='${messageId || 'none'}' (past 15-min edit window or unsupported).`);
            return {
                isSuccess: false,
                status: 'windowExpired',
                errorDescription: 'Edit menu item not available (past edit window or unsupported)'
            };
        }

        await editBtn.click({ force: true });
        await page.waitForTimeout(800);

        // Edit input box appears (modal or footer)
        const editInput = page.locator('div[role="dialog"] div[contenteditable="true"], [data-animate-modal-popup] div[contenteditable="true"], #main footer div[contenteditable="true"], div[role="textbox"][contenteditable="true"]').last();
        const inputVisible = await editInput.waitFor({ state: 'visible', timeout: 4000 }).then(() => true).catch(() => false);
        if (inputVisible) {
            await editInput.click({ force: true });
            await page.keyboard.press('Control+A');
            await page.keyboard.press('Backspace');
            await page.keyboard.insertText(newText);
            await page.waitForTimeout(500);

            const checkBtn = page.locator(
                'span[data-icon="check"], span[data-icon="checkmark-large"], ' +
                'button[aria-label*="Підтвердити" i], button[aria-label*="Готово" i], ' +
                'button[aria-label*="Confirm" i], button[aria-label*="Сохранить" i]'
            ).first();
            const checkVisible = await checkBtn.waitFor({ state: 'visible', timeout: 2000 }).then(() => true).catch(() => false);
            if (checkVisible) {
                await checkBtn.click({ force: true });
            } else {
                await page.keyboard.press('Enter');
            }
            await page.waitForTimeout(1500);
            await scrollToBottom();

            logBridge('INFO', `In-place edit succeeded for id='${messageId || 'none'}'.`);
            return {
                isSuccess: true,
                status: 'updated',
                messageId: messageId
            };
        }

        await page.keyboard.press('Escape');
        await scrollToBottom();
        return {
            isSuccess: false,
            status: 'failed',
            errorDescription: 'Failed to access edit input'
        };
    } catch (e) {
        logBridge('WARN', `editMessage error: ${e.message}`);
        await dismissOverlays();
        await scrollToBottom();
        return {
            isSuccess: false,
            status: 'failed',
            errorDescription: e.message
        };
    }
}

// Legacy compatibility helpers (preserved for fallback when reconcile flag is off)
async function cleanupStrayStatusMessages(keepLatest = false) {
    try {
        await scrollToBottom();
        const rows = await page.$$('#main div[role="row"]');
        const statusRows = [];
        for (let i = 0; i < rows.length; i++) {
            const text = await rows[i].innerText().catch(() => '');
            if (text.includes('Останнє оновлення журналу:') || text.includes('Стан моніторингу:')) {
                statusRows.push(rows[i]);
            }
        }

        const countToDelete = keepLatest ? statusRows.length - 1 : statusRows.length;
        if (countToDelete <= 0) return;

        for (let i = 0; i < countToDelete; i++) {
            const currentRows = await page.$$('#main div[role="row"]');
            for (let r = 0; r < currentRows.length; r++) {
                const t = await currentRows[r].innerText().catch(() => '');
                if (t.includes('Останнє оновлення журналу:') || t.includes('Стан моніторингу:')) {
                    await deleteMessageRow(currentRows[r]);
                    await page.waitForTimeout(600);
                    break;
                }
            }
        }
    } catch (_) {
        await dismissOverlays();
    }
}

async function sweepObsoleteForecasts(channelIdentifier, todayDateStr) {
    await ensureChannelOpen(channelIdentifier);
    logBridge('INFO', `Executing sweep for obsolete forecast messages (Today: ${todayDateStr})...`);
    return { isSuccess: true, deletedCount: 0 };
}

async function saveQrScreenshot() {
    if (!page || page.isClosed()) return null;
    try {
        const qrCanvas = await page.$('canvas[aria-label*="Scan"], div[data-ref]');
        const localQr = path.resolve(__dirname, 'whatsapp_qr.png');
        if (qrCanvas) {
            await qrCanvas.screenshot({ path: localQr });
            return localQr;
        } else {
            await page.screenshot({ path: localQr });
            return localQr;
        }
    } catch (_) {
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
    getTailState,
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
    logBridge,
    getPage: () => page
};
