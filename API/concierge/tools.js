'use strict';

// Tool registry for the concierge. Empty for now: the concierge only answers
// questions and redirects booking/availability requests to the booking page.
//
// To add a tool later (e.g. check_availability, create_booking), add an entry:
//
//     {
//         definition: {
//             name: 'check_availability',
//             description: '...',
//             input_schema: { type: 'object', properties: { ... }, required: [...], additionalProperties: false },
//             strict: true
//         },
//         // Validate input here, then call the same service-layer functions the
//         // CRUD endpoints use. Never write to the database from model output directly.
//         // Anything that creates, changes, or cancels a booking must require explicit
//         // guest or staff confirmation before it runs.
//         run: async (input, context) => ({ ... })   // returns a JSON-serializable result
//     }
//
// concierge.js already sends these definitions to the API and runs the tool loop.

const TOOLS = [];

function toolDefinitions() {
    return TOOLS.map((t) => t.definition);
}

async function runTool(name, input, context) {
    const tool = TOOLS.find((t) => t.definition.name === name);
    if (!tool) return { isError: true, content: 'Unknown tool: ' + name };
    try {
        const result = await tool.run(input, context);
        return { isError: false, content: JSON.stringify(result) };
    } catch (err) {
        return { isError: true, content: 'Tool failed: ' + (err && err.message ? err.message : 'unknown error') };
    }
}

module.exports = { toolDefinitions, runTool };
