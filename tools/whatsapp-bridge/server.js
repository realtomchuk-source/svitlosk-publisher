const express = require('express');
const fs = require('fs');
const browser = require('./browser');

const app = express();
app.use(express.json({ limit: '50mb' }));

const PORT = process.env.PORT || 3000;

// Kyiv (Europe/Kyiv) calendar date in yyyy-MM-dd, DST-aware (never UTC date).
function kyivTodayIso() {
    return new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/Kyiv', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
}

function kyivTodayDdMm() {
    const iso = kyivTodayIso(); // yyyy-MM-dd
    const parts = iso.split('-');
    return parts.length === 3 ? `${parts[2]}.${parts[1]}` : iso;
}

// 1. Health check
app.get('/health', async (req, res) => {
    let status = browser.getStatus();
    if (!status.connected) {
        try {
            await browser.ensureBrowser();
            status = browser.getStatus();
        } catch (_) {}
    }
    res.json(status);
});

// 1b. QR code screenshot
app.get('/qr', async (req, res) => {
    const qrPath = await browser.saveQrScreenshot();
    if (qrPath && fs.existsSync(qrPath)) {
        res.sendFile(qrPath);
    } else {
        res.status(404).json({ error: 'QR code not currently displayed' });
    }
});

// 1c. Debug DOM elements
app.get('/debug-dom', async (req, res) => {
    try {
        const dom = await browser.getDebugDom();
        res.json(dom);
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 2. Channel info (actively verifies channel availability and composer presence in #main)
app.get('/channel-info', async (req, res) => {
    try {
        await browser.ensureBrowser();
        const status = browser.getStatus();
        if (!status.connected) {
            return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet. Please scan QR code in the browser window.' });
        }

        const channelId = req.query.channelId || '0029Vb96XUUIN9ixA88Umz3Y';
        await browser.ensureChannelOpen(channelId);

        res.json({
            isSuccess: true,
            jid: channelId,
            name: 'SvitloSk Channel',
            description: 'SvitloSk Publisher Official Channel (Verified & Ready)',
            subscribers: 0
        });
    } catch (err) {
        browser.logBridge('WARN', `/channel-info failed: ${err.message}`);
        res.status(503).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 2a. Read channel tail state (for WhatsAppReconciler)
app.get('/tail-state', async (req, res) => {
    try {
        await browser.ensureBrowser();
        const status = browser.getStatus();
        if (!status.connected) {
            return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet.' });
        }

        const channelId = req.query.channelId || '0029Vb96XUUIN9ixA88Umz3Y';
        const todayDdMm = req.query.todayDdMm || kyivTodayDdMm();
        const maxScreens = parseInt(req.query.maxScreens || '6', 10);

        const state = await browser.getTailState(channelId, todayDdMm, maxScreens);
        res.json(state);
    } catch (err) {
        browser.logBridge('ERROR', `/tail-state failed: ${err.message}`);
        res.status(500).json({ isSuccess: false, errorDescription: err.message, messages: [] });
    }
});

// 2b. Open channel explicitly
app.post('/open-channel', async (req, res) => {
    try {
        await browser.ensureBrowser();
        const channelId = req.body.channelOrChatId || '0029Vb96XUUIN9ixA88Umz3Y';
        await browser.ensureChannelOpen(channelId);
        res.json({ isSuccess: true });
    } catch (err) {
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 2b2. Restart session
app.post('/restart-session', async (req, res) => {
    try {
        await browser.restartSession();
        res.json({ isSuccess: true, message: 'Browser session successfully restarted.' });
    } catch (err) {
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 2c. Debug evaluate script in page
app.post('/eval', async (req, res) => {
    try {
        const page = browser.getPage();
        if (!page) return res.status(500).json({ error: 'No page' });
        const result = await page.evaluate(req.body.script);
        res.json({ result });
    } catch (err) {
        res.status(500).json({ error: err.message });
    }
});

// 2c2. Debug evaluate Node Playwright code
app.post('/eval-node', async (req, res) => {
    try {
        const page = browser.getPage();
        if (!page) return res.status(500).json({ error: 'No page' });
        const AsyncFunction = Object.getPrototypeOf(async function(){}).constructor;
        const fn = new AsyncFunction('browser', 'page', req.body.code);
        const result = await fn(browser, page);
        res.json({ result });
    } catch (err) {
        res.status(500).json({ error: err.message, stack: err.stack });
    }
});

// 2d. Clean stray status messages
app.post('/clean-stray', async (req, res) => {
    try {
        await browser.ensureBrowser();
        await browser.ensureChannelOpen(req.body.channelOrChatId || '0029Vb96XUUIN9ixA88Umz3Y');
        await browser.cleanupStrayStatusMessages(req.body.keepLatest === true);
        res.json({ isSuccess: true });
    } catch (err) {
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 2e. Pre-flight sweep for obsolete forecasts
app.post('/sweep-forecasts', async (req, res) => {
    try {
        await browser.ensureBrowser();
        const channelId = req.body.channelOrChatId || '0029Vb96XUUIN9ixA88Umz3Y';
        const todayDate = req.body.todayDate || kyivTodayIso();
        const result = await browser.sweepObsoleteForecasts(channelId, todayDate);
        res.json(result);
    } catch (err) {
        browser.logBridge('WARN', `Sweep forecasts failed: ${err.message}`);
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 3. Send text message
app.post('/send', async (req, res) => {
    await browser.ensureBrowser();
    const status = browser.getStatus();
    if (!status.connected) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet. Please scan QR code in the browser window.' });
    }

    const { channelOrChatId, text } = req.body;
    if (!channelOrChatId || !text) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId and text are required.' });
    }

    try {
        const result = await browser.sendTextMessage(channelOrChatId, text);
        if (!result.isSuccess) {
            return res.status(500).json(result);
        }
        res.json(result);
    } catch (err) {
        browser.logBridge('ERROR', `Send text failed: ${err.message}`);
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 4. Send media message
app.post('/media', async (req, res) => {
    await browser.ensureBrowser();
    const status = browser.getStatus();
    if (!status.connected) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet. Please scan QR code in the browser window.' });
    }

    const { channelOrChatId, caption, imageBase64, mimeType = 'image/png' } = req.body;
    if (!channelOrChatId) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId is required.' });
    }

    try {
        if (!imageBase64) {
            const textResult = await browser.sendTextMessage(channelOrChatId, caption || '');
            if (!textResult.isSuccess) return res.status(500).json(textResult);
            return res.json(textResult);
        }

        const result = await browser.sendMediaMessage(channelOrChatId, caption || '', imageBase64, mimeType);
        if (!result.isSuccess) {
            return res.status(500).json(result);
        }
        res.json(result);
    } catch (err) {
        browser.logBridge('ERROR', `Send media failed: ${err.message}`);
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 5. Delete message (context menu authoritative deletion)
app.post('/delete', async (req, res) => {
    await browser.ensureBrowser();
    const status = browser.getStatus();
    if (!status.connected) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet.' });
    }

    const { channelOrChatId, messageId, marker } = req.body;
    if (!channelOrChatId || (!messageId && !marker)) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId and (messageId or marker) are required.' });
    }

    try {
        const result = await browser.deleteMessage(channelOrChatId, messageId, marker);
        if (!result.isSuccess) {
            return res.status(500).json(result);
        }
        res.json(result);
    } catch (err) {
        browser.logBridge('WARN', `Delete failed: ${err.message}`);
        res.status(500).json({ isSuccess: false, status: 'failed', messageId: messageId, errorDescription: err.message });
    }
});

// 6. Edit message
app.post('/edit', async (req, res) => {
    await browser.ensureBrowser();
    const status = browser.getStatus();
    if (!status.connected) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet.' });
    }

    const { channelOrChatId, messageId, text, marker } = req.body;
    if (!channelOrChatId || (!messageId && !marker) || !text) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId, (messageId or marker) and text are required.' });
    }

    try {
        const result = await browser.editMessage(channelOrChatId, messageId, text, marker);
        if (!result.isSuccess) {
            return res.status(422).json(result);
        }
        res.json(result);
    } catch (err) {
        browser.logBridge('ERROR', `Edit message failed: ${err.message}`);
        res.status(500).json({ isSuccess: false, status: 'failed', errorDescription: err.message });
    }
});

process.on('uncaughtException', (err) => {
    browser.logBridge('ERROR', `Uncaught exception: ${err.message}`);
});
process.on('unhandledRejection', (reason) => {
    browser.logBridge('ERROR', `Unhandled rejection: ${reason}`);
});

app.listen(PORT, '127.0.0.1', () => {
    browser.logBridge('INFO', `======================================================`);
    browser.logBridge('INFO', ` [WHATSAPP-BRIDGE] Playwright Browser Microservice`);
    browser.logBridge('INFO', ` Listening on: http://127.0.0.1:${PORT}`);
    browser.logBridge('INFO', `======================================================`);
    browser.ensureBrowser();
});
