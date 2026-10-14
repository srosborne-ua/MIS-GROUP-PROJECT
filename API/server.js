'use strict';

// Druid City Hotel backend.
//   - Serves the static client from ../Client
//   - POST /api/concierge  -> Druid AI Assistant (streams NDJSON)
//
// Run:  cd API && npm install && npm start   (then open http://localhost:3000)

const path = require('path');

// Load ../.env before anything reads process.env. Never log its contents.
try {
    process.loadEnvFile(path.join(__dirname, '..', '.env'));
} catch (err) {
    if (err.code !== 'ENOENT') console.warn('Could not read .env:', err.code || err.message);
}

const http = require('http');
const fs = require('fs');
const { runConcierge, cleanHistory, InputError, MODEL, Anthropic } = require('./concierge/concierge');

const PORT = Number(process.env.PORT) || 3000;
const CLIENT_DIR = path.resolve(__dirname, '..', 'Client');
const MAX_BODY_BYTES = 32 * 1024;

// Simple in-memory rate limit for the AI endpoint, per client IP.
const RATE_WINDOW_MS = 60 * 1000;
const RATE_MAX = 20;
const hits = new Map();

function rateLimited(ip) {
    const now = Date.now();
    const recent = (hits.get(ip) || []).filter((t) => now - t < RATE_WINDOW_MS);
    recent.push(now);
    hits.set(ip, recent);
    return recent.length > RATE_MAX;
}

setInterval(() => {
    const now = Date.now();
    for (const [ip, times] of hits) {
        if (!times.some((t) => now - t < RATE_WINDOW_MS)) hits.delete(ip);
    }
}, RATE_WINDOW_MS).unref();

// Allow the client to call the API when it is opened from file:// (Origin: null)
// or served by another local dev server (e.g. python -m http.server 8000).
function corsHeaders(req) {
    const origin = req.headers.origin;
    if (origin && (origin === 'null' || /^http:\/\/(localhost|127\.0\.0\.1)(:\d+)?$/.test(origin))) {
        return {
            'Access-Control-Allow-Origin': origin,
            'Access-Control-Allow-Methods': 'POST, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type',
            'Vary': 'Origin'
        };
    }
    return {};
}

function sendJSON(req, res, status, body) {
    res.writeHead(status, Object.assign({ 'Content-Type': 'application/json; charset=utf-8' }, corsHeaders(req)));
    res.end(JSON.stringify(body));
}

function readBody(req) {
    return new Promise((resolve, reject) => {
        let size = 0;
        const chunks = [];
        req.on('data', (chunk) => {
            size += chunk.length;
            if (size > MAX_BODY_BYTES) {
                reject(new InputError('request too large'));
                req.destroy();
                return;
            }
            chunks.push(chunk);
        });
        req.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')));
        req.on('error', reject);
    });
}

const FRIENDLY_ERROR = 'Sorry, I\'m having trouble answering right now. Please try again in a moment, or call the front desk.';
const REFUSAL_MESSAGE = 'I\'m sorry, I can\'t help with that. I\'m happy to answer questions about your stay or booking.';

async function handleConcierge(req, res) {
    if (!process.env.ANTHROPIC_API_KEY) {
        sendJSON(req, res, 503, { error: 'The concierge is not configured.' });
        return;
    }
    if (rateLimited(req.socket.remoteAddress || 'unknown')) {
        sendJSON(req, res, 429, { error: 'Too many messages. Please wait a minute and try again.' });
        return;
    }

    let messages;
    let page;
    try {
        const body = JSON.parse(await readBody(req));
        messages = cleanHistory(body.messages);
        page = typeof body.page === 'string' ? body.page : 'main';
    } catch (err) {
        sendJSON(req, res, 400, { error: err instanceof InputError ? err.message : 'Invalid request body.' });
        return;
    }

    // Stream newline-delimited JSON events: {type:'text'|'reset'|'done'|'error', ...}
    res.writeHead(200, Object.assign({
        'Content-Type': 'application/x-ndjson; charset=utf-8',
        'Cache-Control': 'no-store',
        'X-Content-Type-Options': 'nosniff'
    }, corsHeaders(req)));
    const send = (event) => { if (!res.writableEnded) res.write(JSON.stringify(event) + '\n'); };

    const controller = new AbortController();
    res.on('close', () => { if (!res.writableFinished) controller.abort(); });

    try {
        const result = await runConcierge({
            messages,
            page,
            signal: controller.signal,
            onText: (text) => send({ type: 'text', text }),
            onReset: () => send({ type: 'reset' })
        });
        if (result.status === 'refusal') {
            send({ type: 'reset' });
            send({ type: 'text', text: REFUSAL_MESSAGE });
        }
        send({ type: 'done' });
    } catch (err) {
        if (controller.signal.aborted) return;
        // Log status and type only; never the request contents or credentials.
        if (err instanceof Anthropic.RateLimitError) console.warn('Concierge: Anthropic rate limit (429)');
        else if (err instanceof Anthropic.AuthenticationError) console.error('Concierge: API key rejected (401). Check ANTHROPIC_API_KEY in .env');
        else if (err instanceof Anthropic.APIError) console.error('Concierge: API error', err.status || '', err.name);
        else console.error('Concierge: unexpected error', err && err.name, err && err.message);
        send({ type: 'error', message: FRIENDLY_ERROR });
    }
    res.end();
}

const MIME = {
    '.html': 'text/html; charset=utf-8',
    '.css': 'text/css; charset=utf-8',
    '.js': 'text/javascript; charset=utf-8',
    '.json': 'application/json; charset=utf-8',
    '.jpg': 'image/jpeg',
    '.jpeg': 'image/jpeg',
    '.png': 'image/png',
    '.svg': 'image/svg+xml',
    '.webp': 'image/webp',
    '.ico': 'image/x-icon'
};

function serveStatic(req, res) {
    let pathname;
    try {
        pathname = decodeURIComponent(new URL(req.url, 'http://localhost').pathname);
    } catch (e) {
        res.writeHead(400).end();
        return;
    }
    if (pathname === '/') pathname = '/index.html';
    const filePath = path.resolve(CLIENT_DIR, '.' + pathname);
    if (!filePath.startsWith(CLIENT_DIR + path.sep)) {
        res.writeHead(403).end();
        return;
    }
    fs.readFile(filePath, (err, data) => {
        if (err) {
            res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' }).end('Not found');
            return;
        }
        res.writeHead(200, {
            'Content-Type': MIME[path.extname(filePath).toLowerCase()] || 'application/octet-stream',
            'X-Content-Type-Options': 'nosniff'
        });
        res.end(req.method === 'HEAD' ? undefined : data);
    });
}

const server = http.createServer((req, res) => {
    const url = req.url.split('?')[0];
    if (url === '/api/concierge') {
        if (req.method === 'OPTIONS') {
            res.writeHead(204, corsHeaders(req)).end();
        } else if (req.method === 'POST') {
            handleConcierge(req, res).catch(() => { if (!res.headersSent) sendJSON(req, res, 500, { error: FRIENDLY_ERROR }); else res.end(); });
        } else {
            sendJSON(req, res, 405, { error: 'Method not allowed.' });
        }
        return;
    }
    if (url.startsWith('/api/')) {
        sendJSON(req, res, 404, { error: 'Not found.' });
        return;
    }
    if (req.method !== 'GET' && req.method !== 'HEAD') {
        res.writeHead(405).end();
        return;
    }
    serveStatic(req, res);
});

server.listen(PORT, () => {
    console.log('Druid City Hotel running at http://localhost:' + PORT);
    console.log('Concierge model: ' + MODEL + (process.env.ANTHROPIC_API_KEY ? '' : ' (disabled: ANTHROPIC_API_KEY not set)'));
});
