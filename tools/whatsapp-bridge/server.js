const express = require('express');
const path = require('path');
const fs = require('fs');
const browser = require('./browser');

const app = express();
app.use(express.json({ limit: '50mb' }));

const PORT = process.env.PORT || 3000;

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

// 2. Channel info
app.get('/channel-info', async (req, res) => {
    await browser.ensureBrowser();
    const status = browser.getStatus();
    if (!status.connected) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet. Please scan QR code in the browser window.' });
    }

    const channelId = req.query.channelId || '0029Vb96XUUIN9ixA88Umz3Y';
    res.json({
        isSuccess: true,
        jid: channelId,
        name: 'SvitloSk Channel',
        description: 'SvitloSk Publisher Official Channel',
        subscribers: 0
    });
});

// 2b. Open channel explicitly
app.post('/open-channel', async (req, res) => {
    try {
        await browser.ensureBrowser();
        await browser.ensureChannelOpen('0029Vb96XUUIN9ixA88Umz3Y');
        res.json({ isSuccess: true });
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
        res.json(result);
    } catch (err) {
        console.error('[SERVER] Send text failed:', err);
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
            return res.json(textResult);
        }

        const result = await browser.sendMediaMessage(channelOrChatId, caption || '', imageBase64, mimeType);
        res.json(result);
    } catch (err) {
        console.error('[SERVER] Send media failed:', err);
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

    const { channelOrChatId, messageId } = req.body;
    if (!channelOrChatId || !messageId) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId and messageId are required.' });
    }

    try {
        const result = await browser.deleteMessage(channelOrChatId, messageId);
        res.json(result);
    } catch (err) {
        console.warn(`[SERVER] Delete failed (treating as idempotent): ${err.message}`);
        res.json({ isSuccess: true, messageId: messageId, note: err.message });
    }
});

// 6. Edit message
app.post('/edit', async (req, res) => {
    await browser.ensureBrowser();
    const status = browser.getStatus();
    if (!status.connected) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp Web is not authenticated yet.' });
    }

    const { channelOrChatId, messageId, text } = req.body;
    if (!channelOrChatId || !messageId || !text) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId, messageId and text are required.' });
    }

    try {
        const result = await browser.editMessage(channelOrChatId, messageId, text);
        res.json(result);
    } catch (err) {
        console.error('[SERVER] Edit message failed:', err);
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

process.on('uncaughtException', (err) => {
    console.error('[SERVER] Uncaught exception:', err.message);
});
process.on('unhandledRejection', (reason) => {
    console.error('[SERVER] Unhandled rejection:', reason);
});

app.listen(PORT, '127.0.0.1', () => {
    console.log(`======================================================`);
    console.log(` [WHATSAPP-BRIDGE] Playwright Browser Microservice`);
    console.log(` Listening on: http://127.0.0.1:${PORT}`);
    console.log(`======================================================`);
    browser.initBrowser();
});
