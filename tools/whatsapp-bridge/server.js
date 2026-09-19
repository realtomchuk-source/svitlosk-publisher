const express = require('express');
const { default: makeWASocket, useMultiFileAuthState, DisconnectReason } = require('@whiskeysockets/baileys');
const pino = require('pino');
const qrcode = require('qrcode-terminal');
const path = require('path');
const fs = require('fs');

const app = express();
app.use(express.json({ limit: '25mb' }));

const PORT = process.env.PORT || 3000;
const SESSION_DIR = path.resolve(__dirname, '../../local/whatsapp-session');

if (!fs.existsSync(SESSION_DIR)) {
    fs.mkdirSync(SESSION_DIR, { recursive: true });
}

let sock = null;
let isConnected = false;
let currentQr = null;

async function initWhatsApp() {
    try {
        const { state, saveCreds } = await useMultiFileAuthState(SESSION_DIR);
        const logger = pino({ level: 'error' });

        sock = makeWASocket({
            auth: state,
            logger: logger,
            printQRInTerminal: false,
            browser: ['SvitloSk Publisher', 'Chrome', '120.0.0']
        });

        sock.ev.on('creds.update', saveCreds);

        sock.ev.on('connection.update', async (update) => {
            const { connection, lastDisconnect, qr } = update;

            if (qr) {
                currentQr = qr;
                console.log('\n======================================================');
                console.log(' [WHATSAPP-BRIDGE] Scan the QR code below to connect:');
                console.log(' 1. Open WhatsApp on your phone');
                console.log(' 2. Go to Settings (or 3 dots) -> Linked Devices');
                console.log(' 3. Tap "Link a Device" and point your camera here:');
                console.log('======================================================\n');
                qrcode.generate(qr, { small: true });
            }

            if (connection === 'close') {
                isConnected = false;
                const statusCode = lastDisconnect?.error?.output?.statusCode;
                const shouldReconnect = statusCode !== DisconnectReason.loggedOut;
                console.log(`[WHATSAPP-BRIDGE] Connection closed (status: ${statusCode}). Reconnecting: ${shouldReconnect}`);

                if (shouldReconnect) {
                    setTimeout(initWhatsApp, 3000);
                } else {
                    console.log('[WHATSAPP-BRIDGE] Logged out. Session invalidated.');
                    try {
                        fs.rmSync(SESSION_DIR, { recursive: true, force: true });
                    } catch (e) {}
                    setTimeout(initWhatsApp, 2000);
                }
            } else if (connection === 'open') {
                isConnected = true;
                currentQr = null;
                console.log('\n======================================================');
                console.log(' [WHATSAPP-BRIDGE] Connected to WhatsApp Web successfully!');
                console.log(` [WHATSAPP-BRIDGE] User: ${sock?.user?.id || 'Admin'}`);
                console.log(` [WHATSAPP-BRIDGE] Listening on: http://127.0.0.1:${PORT}`);
                console.log('======================================================\n');
            }
        });
    } catch (err) {
        console.error('[WHATSAPP-BRIDGE] Error initializing WhatsApp socket:', err);
        setTimeout(initWhatsApp, 5000);
    }
}

// Helper: resolve link or invite code to JID (@newsletter)
async function resolveDestinationJid(identifier) {
    if (!identifier) throw new Error('Channel or recipient identifier is required');

    let clean = identifier.trim();

    // Already a complete JID
    if (clean.includes('@')) {
        return clean;
    }

    // Extract from channel URL (e.g. https://whatsapp.com/channel/0029Va...)
    const urlMatch = clean.match(/whatsapp\.com\/channel\/([a-zA-Z0-9_-]+)/i);
    if (urlMatch) {
        clean = urlMatch[1];
    }

    // If alphanumeric code without @ (like 0029Va...)
    if (sock && typeof sock.newsletterMetadata === 'function') {
        try {
            console.log(`[WHATSAPP-BRIDGE] Resolving invite code '${clean}' via newsletterMetadata...`);
            const meta = await sock.newsletterMetadata('invite', clean);
            if (meta && meta.id) {
                console.log(`[WHATSAPP-BRIDGE] Resolved invite '${clean}' to JID: ${meta.id}`);
                return meta.id;
            }
        } catch (e) {
            console.warn(`[WHATSAPP-BRIDGE] Failed to resolve invite '${clean}' as newsletter: ${e.message}`);
        }
    }

    // Fallback: If it's a channel numeric ID or phone number
    if (/^\d+$/.test(clean)) {
        if (clean.length >= 15) {
            return `${clean}@newsletter`;
        }
        return `${clean}@s.whatsapp.net`;
    }

    return `${clean}@newsletter`;
}

// 1. Health check
app.get('/health', (req, res) => {
    res.json({
        status: 'ok',
        connected: isConnected,
        hasQr: !!currentQr,
        user: sock?.user?.id || null
    });
});

// 2. Channel info
app.get('/channel-info', async (req, res) => {
    if (!isConnected || !sock) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp bridge is not connected yet.' });
    }

    const channelId = req.query.channelId;
    try {
        const jid = await resolveDestinationJid(channelId);
        let meta = null;
        if (typeof sock.newsletterMetadata === 'function') {
            meta = await sock.newsletterMetadata('jid', jid);
        }

        res.json({
            isSuccess: true,
            jid: jid,
            name: meta?.name || jid,
            description: meta?.description || null,
            subscribers: meta?.subscribers || 0
        });
    } catch (err) {
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 2b. Fetch channel messages
app.get('/messages', async (req, res) => {
    if (!isConnected || !sock) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp bridge is not connected yet.' });
    }

    const channelId = req.query.channelId;
    try {
        const jid = await resolveDestinationJid(channelId);
        if (typeof sock.newsletterFetchMessages === 'function') {
            const result = await sock.newsletterFetchMessages(jid, 10, undefined, undefined);
            return res.json({ isSuccess: true, result });
        }
        res.status(400).json({ isSuccess: false, errorDescription: 'newsletterFetchMessages not supported' });
    } catch (err) {
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 3. Send text message
app.post('/send', async (req, res) => {
    if (!isConnected || !sock) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp bridge is not connected to WhatsApp Web.' });
    }

    const { channelOrChatId, text } = req.body;
    if (!channelOrChatId || !text) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId and text are required.' });
    }

    try {
        const jid = await resolveDestinationJid(channelOrChatId);
        console.log(`[WHATSAPP-BRIDGE] Sending text message to ${jid} (${text.length} chars)...`);

        const result = await sock.sendMessage(jid, { text: text });
        const messageId = result?.key?.id || `msg_${Date.now()}`;

        console.log(`[WHATSAPP-BRIDGE] Message sent successfully. ID: ${messageId}`);
        res.json({
            isSuccess: true,
            messageId: messageId,
            jid: jid
        });
    } catch (err) {
        console.error('[WHATSAPP-BRIDGE] Failed to send message:', err);
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 4. Send media message
app.post('/media', async (req, res) => {
    if (!isConnected || !sock) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp bridge is not connected to WhatsApp Web.' });
    }

    const { channelOrChatId, caption, imageBase64, mimeType = 'image/png' } = req.body;
    if (!channelOrChatId) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId is required.' });
    }

    try {
        const jid = await resolveDestinationJid(channelOrChatId);
        let result = null;

        if (imageBase64) {
            const buffer = Buffer.from(imageBase64, 'base64');
            console.log(`[WHATSAPP-BRIDGE] Sending media message to ${jid} (${buffer.length} bytes)...`);
            result = await sock.sendMessage(jid, {
                image: buffer,
                caption: caption || '',
                mimetype: mimeType
            });
        } else {
            result = await sock.sendMessage(jid, { text: caption || '' });
        }

        const messageId = result?.key?.id || `msg_${Date.now()}`;
        console.log(`[WHATSAPP-BRIDGE] Media sent successfully. ID: ${messageId}`);
        res.json({
            isSuccess: true,
            messageId: messageId,
            jid: jid
        });
    } catch (err) {
        console.error('[WHATSAPP-BRIDGE] Failed to send media:', err);
        res.status(500).json({ isSuccess: false, errorDescription: err.message });
    }
});

// 5. Delete message
app.post('/delete', async (req, res) => {
    if (!isConnected || !sock) {
        return res.status(503).json({ isSuccess: false, errorDescription: 'WhatsApp bridge is not connected.' });
    }

    const { channelOrChatId, messageId } = req.body;
    if (!channelOrChatId || !messageId) {
        return res.status(400).json({ isSuccess: false, errorDescription: 'channelOrChatId and messageId are required.' });
    }

    try {
        const jid = await resolveDestinationJid(channelOrChatId);
        console.log(`[WHATSAPP-BRIDGE] Deleting message ${messageId} from ${jid}...`);

        await sock.sendMessage(jid, {
            delete: {
                remoteJid: jid,
                fromMe: true,
                id: messageId
            }
        });

        res.json({ isSuccess: true, messageId: messageId });
    } catch (err) {
        console.warn(`[WHATSAPP-BRIDGE] Delete failed (treating as idempotent): ${err.message}`);
        res.json({ isSuccess: true, messageId: messageId, note: err.message });
    }
});

app.listen(PORT, '127.0.0.1', () => {
    console.log(`[WHATSAPP-BRIDGE] Service running on http://127.0.0.1:${PORT}`);
    initWhatsApp();
});
