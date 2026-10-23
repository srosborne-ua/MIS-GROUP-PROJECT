# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

End-to-end hotel booking platform for Mr. Rogers' hotel in Tuscaloosa, Alabama (branded **Druid City Hotel** in the client). Guests search availability, see the full cost of a stay, and book online. Staff manage reservations, availability, rates, and guest communication.

The long-term project is an "AI Concierge for Hospitality Bookings".

**Current phase: Sprint 5: integrating the ASP.NET Core backend (`API/`) with the existing front end, and adding AI.** This phase adds:

- **An ASP.NET Core Web API in `API/`** that serves the client's data over HTTP (in-memory for now).
- **A database** that replaces the in-memory store, with full CRUD (create, read, update, delete) for reservations, rooms, holds/blocks, rates, and guests.
- **AI functionality** using Claude through the **official Anthropic SDK**, starting with the Druid AI Assistant concierge chat.

Success definition from the client: the site should make life easy, both for guests booking and for staff managing.

See [README.md](README.md) for the route table, hotel model details, and the running list of assumptions to confirm with Mr. Rogers.

## Running

Run the API and serve the client (opening `index.html` from disk can't call the API):

```sh
dotnet run --project API --launch-profile http          # http://localhost:5269
python3 -m http.server 8000 --directory Client          # http://localhost:8000
```

There is no test suite or linter. Verify changes by loading the site and walking through the affected views on both a narrow (phone) and wide (desktop) viewport.

The AI concierge is part of the API (`POST api/concierge`), so those two commands run everything. It needs `ANTHROPIC_API_KEY` in the repo-root `.env`; without it the chat shows a friendly error and the rest of the site keeps working.

## Structure

```
.env                          Secrets (ANTHROPIC_API_KEY). Git-ignored; never commit.
API/                          ASP.NET Core Web API (.NET 10, controllers)
├── Controllers/              One controller per resource (rooms, rates, reservations, holds, blocks, demo)
├── Models/HotelModels.cs     Response shapes and request bodies
├── Services/HotelStore.cs    In-memory data, sample data, and all booking/pricing rules
├── Services/DotEnv.cs        Loads the repo-root .env at startup
├── Controllers/ConciergeController.cs   POST api/concierge (streams NDJSON, rate limited)
└── Concierge/
    ├── hotel-info.json       Editable hotel facts loaded into the concierge prompt (TODOs = unknown)
    ├── ConciergePrompt.cs    Server-side system prompt (identity, scope, page context)
    ├── ConciergeService.cs   Anthropic SDK call, history window, streaming, tool loop
    └── ConciergeTools.cs     Tool registry (empty for now)
Client/
├── Resources/
│   ├── images/               Placeholder photography (must be replaced before launch)
│   ├── styles/index.css      All styling
│   └── scripts/index.js      All behavior, routing, and API calls
└── index.html                The only HTML file
```

## Hard rules

- **Single-page app.** `index.html` is the only HTML file. Never create additional HTML files.
- **No inline styles or scripts** in `index.html`. All styling goes in `index.css`; all behavior goes in `index.js`. `index.html` only links to those two files (plus Google Fonts).
- **Backend: C# only.** All server code is C# in the ASP.NET Core project in `API/`. Don't add Node, Python, or other backend services or runtimes; new endpoints, AI features, and data access go into this project.
- **Client: plain HTML, CSS, and JavaScript.** No frameworks, libraries, or build tools in `Client/` unless the user explicitly asks. The backend may use a framework and the Anthropic SDK, but keep dependencies minimal.
- **Hash-based routing** (`#/home`, `#/search`, `#/booking`, `#/confirmation`, `#/rooms`, `#/staff/...`). Views must be linkable, back/forward must work, and unknown routes fall back to Home. The site must run without special server configuration.
- **Each site area is a view inside `index.html`**: a `<section class="view" data-view="...">` shown/hidden by the router.
- **The server owns the rules.** Pricing, availability, and booking validation live in `HotelStore`; the client may pre-validate for fast feedback but never decides prices or availability. API errors are `{ "message": "..." }` in the site's voice, shown to the user as-is.
- **The client never talks to Anthropic or the database directly.** The browser calls our backend; only the backend uses the API key and the database.
- **Secrets live in `.env` only.** Read `ANTHROPIC_API_KEY` from the environment. Never hard-code keys, never put them in `Client/`, never log them, and never commit `.env`.
- **Payment form is UI only.** Validate in the browser, then discard. Never store, log, or transmit card numbers, expiry, or CVC. A reservation records only paid/not-paid status and whether the guest opted into card-on-file. Card-on-file is an optional checkbox.
- **Scope changes go through Mr. Rogers.** Do not add significant features beyond the spec without the user confirming; flag ideas instead.

## Backend, database, and AI

**Backend:** C# / ASP.NET Core Web API (`API/`, .NET 10), including the AI concierge. **Still to decide:** database engine.

- **CRUD endpoints** cover everything the `api` object exposes today (reservations, rooms, availability, holds, event blocks, rates, guests). Keep the request and response shapes matching what the views already expect, so swapping the `api` methods to `fetch()` doesn't require view changes.
- **Validate on the server** as well as in the browser. Never trust client input, and use parameterized queries (no SQL built by string concatenation).
- **Business rules belong to the backend:** availability, rate calculation (weekend = Friday/Saturday nights), and "existing reservations keep their booked totals". The client may display them but the server is the source of truth.
- **The database never stores card data.** The payment rule above applies to the database too: store only paid/not-paid status and the card-on-file opt-in.
- **Seed data:** provide a way to load the 120-room sample hotel so the "Reset sample data" link in the Staff Portal keeps working.

### AI (Anthropic SDK)

- **Use the official Anthropic C# SDK** (NuGet package `Anthropic`). Don't use the OpenAI-compatible endpoint or raw HTTP.
- **Default model: `claude-opus-5-5`.** Don't switch models without the team agreeing.
- **The SDK reads `ANTHROPIC_API_KEY` from the environment.** `DotEnv.Load()` in `Program.cs` loads the repo-root `.env` at startup; don't pass the key around in code.
- **AI features read and change hotel data only through the backend's own service layer** (for example, as tool calls that hit the same functions as the CRUD endpoints), never with direct database writes from model output. Anything that creates, changes, or cancels a booking needs explicit guest or staff confirmation.
- **Handle API errors and refusals gracefully.** The site must keep working (search, booking, Staff Portal) if the AI service is unavailable.
- **Which AI features to build** (guest concierge chat, staff assistant, etc.) goes through Mr. Rogers like any other scope change. Flag ideas; don't build unconfirmed features.
- **Concierge model:** set with the `CONCIERGE_MODEL` env var (default `claude-opus-5-5`). The system prompt lives only on the server; the client sends just the chat messages and which site section it is on.
### Database Safety Rules 
**Two database users, least privilege.**
- `druid_app`: SELECT, INSERT, UPDATE, DELETE only. Used by the service layer. No CREATE, DROP, ALTER, or GRANT.
- `druid_llm_ro`: SELECT only, on read-only views (room types, rooms, rates, amenities, availability). No access to guest, payment, or booking_message data. Connection and execution time capped.
- Schema changes and seeding run by hand under a separate admin account. The app never connects as root.
- **The model never writes data with SQL.** Bookings, cancellations, holds, and payments go through service-layer tool functions with parameterized queries, as stated above.
- **If we add a plain-English query feature** (model writes its own SQL), it must:
- connect only as `druid_llm_ro`
- give the model the schema of the views only (table names, column names, types, nullability), never row data or connection strings
- validate in C# before running: one statement only, must start with SELECT, reject DROP, DELETE, UPDATE, INSERT, ALTER, TRUNCATE, CREATE, EXEC, EXECUTE, GRANT, REVOKE, INTO OUTFILE, comments (`--`, `/*`, `#`), and `;`. Match whole keywords so columns like `created_at` and `updated_at` aren't blocked
- cap results at 50 rows
- have a system prompt that says: get the schema first, SELECT only, never attempt writes, explain results in plain language
- This feature is a scope change and needs team and Mr. Rogers approval before it's built.
- **Errors:** catch database errors and return `{ "message": "..." }`. Never expose stack traces, SQL text, or connection details to the client or the model.
- **Defense in depth:** database permissions are the real protection; code validation and prompt rules are backup layers. Don't rely on any single layer.

### AI Concierge — planned features

- Current state: Druid AI Assistant answers hotel and booking questions only, is aware of which site section the user is on (main / booking / employee portal), and is restricted to booking-related topics.
- Planned: booking rooms directly through the concierge and checking room availability through it, via Anthropic tool use calling the site's existing booking and availability logic.
- Planned: in-site payment (credit card entry during booking). When it ships, update the payment flag in the hotel info file so the concierge tells guests they can pay online.
- Until then, the concierge redirects booking and availability requests to the booking page. Keep the concierge server code (`ConciergeService` / `ConciergeTools`) structured so tools can be added without a rewrite.

## Code conventions (`index.js`)

The file is one IIFE in `'use strict'` mode, organized into numbered sections. Keep new code in the matching section:

1. Utilities (dates, money, escaping)
2. Hotel configuration (room types, amenities)
3. `api` (fetch calls to the backend)
4. Router
5. Views
6. Startup

- **All data access goes through the `api` object**, which wraps `fetch()` via `request()`. Views never call `fetch()` directly. JSON shapes from the API match what the views expect; keep them in sync when changing either side.
- `ROOM_TYPES` is loaded from `GET api/rooms/types` at startup, before the router runs.
- Server data is in memory and resets when the API restarts. The Staff Portal has a "Reset sample data" link.
- `api.askConcierge()` calls `POST api/concierge` and reads the streamed NDJSON reply. The concierge widget is initialized before the room-types load, so the widget still appears (with a friendly error) if the API is down.
- Show loading and error states in views, since calls go over the network.
- Escape any user- or data-derived text with `esc()` before inserting it into HTML.
- Use the existing `$` / `$$` query helpers.
- 4-space indentation, single quotes, `const`/`let`, semicolons.

## Domain model

- **Floors and rooms:** 3 floors × 40 rooms = 120 rooms, numbered 101–140, 201–240, 301–340. Room number reflects the floor.
- **Bed mix:** about 1/3 King, remainder split between Double Queen and Twin, plus 2 large Master Suites (currently rooms 339 and 340, counted within the 120).
- **Default rates:** $100 per weeknight, $150 per weekend night. **Friday and Saturday nights are weekend nights.**
- **Rates are editable in the Staff Portal** and apply to new searches only. Existing reservations keep the totals they were booked at.
- **Search** returns available rooms plus the total cost for the chosen date range, with filters for **bed type** and **floor**. Fast availability and cost lookup is a top client priority.

## Staff Portal requirements

Every item must remain covered:

- View customer info
- See payment status (paid / not paid)
- Create and cancel reservations
- Place holds on individual rooms
- Block off rooms for events
- View and edit cost data and default rates

The portal currently has no sign-in (see README assumptions). With a real backend and database, staff endpoints are exposed over the network, so staff authentication should be raised with Mr. Rogers before launch.

## Design

**Feel:** minimal, editorial, boutique hotel (think Aesop or Ace Hotel, not a booking chain). Copy and amenities should read as swanky, with local Tuscaloosa character, while staying simple to navigate.

- **Layout:** generous whitespace, wide margins, one idea per section. Full-bleed hero image with a short headline.
- **Typography:** exactly two fonts: Cormorant Garamond (serif, headings) and Inter (sans, body). Large headings, comfortable line height.
- **Color:** neutral base (off-white, warm gray, near-black) plus one muted crimson accent. Use the tokens in `:root` in `index.css` (`--paper`, `--ink`, `--ink-soft`, `--line`, `--accent`, etc.); don't hard-code new colors. No gradients.
- **Components:** flat buttons with subtle hover states, thin dividers instead of boxes, small corner radius (`--radius`).
- **Avoid:** drop shadows everywhere, stock-icon grids, carousels, more than two fonts, cluttered navs.
- **Responsive:** mobile-first. Base styles target phones; add `min-width` media queries for larger screens.

## Accessibility

Required on every change:

- Semantic HTML; each view labelled by its heading (`aria-labelledby`).
- Every form field has a visible `<label>`; errors are shown inline and linked to their field.
- Full keyboard navigation and visible focus states.
- Sufficient color contrast (check against the tokens; `--ink-soft` and `--line-strong` were chosen for contrast).
- Never convey status by color alone (e.g., the staff room board shows status as text too).
- Respect `prefers-reduced-motion`.
- On view change, focus moves to the view's heading; keep the skip link working.

## Open questions with the client

Payment (should guests pay on the site at all?) is still an open question; the current build offers both pay now and pay at the hotel. The full assumptions list lives in the README. When you make a new assumption, add it to that list so the team can confirm it with Mr. Rogers.
