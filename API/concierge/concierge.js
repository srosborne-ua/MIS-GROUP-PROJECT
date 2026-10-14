'use strict';

// Druid AI Assistant: validates the chat history from the browser, calls Claude
// through the Anthropic SDK, and streams the reply back through callbacks.
// The request loop already handles tool_use turns, so tools registered in
// tools.js work without restructuring this file.

const Anthropic = require('@anthropic-ai/sdk');
const { buildSystem, normalizePage } = require('./prompt');
const { toolDefinitions, runTool } = require('./tools');

const MODEL = process.env.CONCIERGE_MODEL || 'claude-opus-5-5';
const MAX_TOKENS = 2048;          // short chat replies; includes thinking
const EFFORT = 'low';             // chat route: fast, concise answers
const MAX_HISTORY = 10;           // rolling window of messages sent to the model
const MAX_MESSAGE_CHARS = 2000;   // per message
const MAX_TOOL_TURNS = 4;         // safety cap once tools exist

// Server-side refusal fallback: if the model's safety classifiers decline,
// the API re-runs the request on a recommended fallback model in the same call.
// Only sent for models that support the "default" fallback mode.
const FALLBACK_BETA = 'server-side-fallback-2026-07-01';
const FALLBACK_MODELS = /^claude-(opus-5|fable-5|sonnet-5-5)/;

let client = null;
function getClient() {
    // Reads ANTHROPIC_API_KEY from the environment.
    if (!client) client = new Anthropic({ timeout: 60 * 1000 });
    return client;
}

class InputError extends Error {}

// Accepts [{ role: 'user'|'assistant', content: string }, ...] from the browser.
// Keeps only the last MAX_HISTORY messages, merges consecutive same-role turns,
// and guarantees the conversation starts and ends with a user message.
function cleanHistory(raw) {
    if (!Array.isArray(raw) || raw.length === 0) throw new InputError('messages must be a non-empty array');

    const recent = raw.slice(-MAX_HISTORY);
    const messages = [];
    for (const m of recent) {
        if (!m || (m.role !== 'user' && m.role !== 'assistant') || typeof m.content !== 'string') {
            throw new InputError('each message needs a role of user or assistant and string content');
        }
        const content = m.content.trim().slice(0, MAX_MESSAGE_CHARS);
        if (!content) continue;
        const last = messages[messages.length - 1];
        if (last && last.role === m.role) last.content += '\n\n' + content;
        else messages.push({ role: m.role, content });
    }

    while (messages.length && messages[0].role !== 'user') messages.shift();
    if (!messages.length || messages[messages.length - 1].role !== 'user') {
        throw new InputError('the last message must be from the user');
    }
    return messages;
}

// Runs one concierge reply.
//   onText(delta)  - a chunk of reply text to show
//   onReset()      - discard text shown so far (the model declined mid-reply and
//                    a fallback model is starting over)
// Resolves to { status: 'ok' | 'refusal' | 'max_tokens' }.
async function runConcierge({ messages, page, onText, onReset, signal }) {
    const system = buildSystem(normalizePage(page));
    const tools = toolDefinitions();
    const conversation = messages.slice();

    for (let turn = 0; turn < MAX_TOOL_TURNS; turn++) {
        const params = {
            model: MODEL,
            max_tokens: MAX_TOKENS,
            system,
            messages: conversation,
            output_config: { effort: EFFORT }
        };
        if (tools.length) params.tools = tools;
        if (FALLBACK_MODELS.test(MODEL)) {
            params.betas = [FALLBACK_BETA];
            params.fallbacks = 'default';
        }

        const stream = getClient().beta.messages.stream(params, { signal });
        for await (const event of stream) {
            if (event.type === 'content_block_start' && event.content_block.type === 'fallback') {
                onReset();
            } else if (event.type === 'content_block_delta' && event.delta.type === 'text_delta') {
                onText(event.delta.text);
            }
        }
        const message = await stream.finalMessage();

        if (message.stop_reason === 'refusal') return { status: 'refusal' };
        if (message.stop_reason !== 'tool_use') {
            return { status: message.stop_reason === 'max_tokens' ? 'max_tokens' : 'ok' };
        }

        // Tool turn: run every requested tool and return all results in one user message.
        const toolUses = message.content.filter((b) => b.type === 'tool_use');
        const results = await Promise.all(toolUses.map(async (block) => {
            const result = await runTool(block.name, block.input, { page });
            return { type: 'tool_result', tool_use_id: block.id, content: result.content, is_error: result.isError };
        }));
        conversation.push({ role: 'assistant', content: message.content });
        conversation.push({ role: 'user', content: results });
    }
    return { status: 'ok' };
}

module.exports = { runConcierge, cleanHistory, InputError, MODEL, Anthropic };
