# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

End-to-end hotel booking platform for Mr. Rogers' hotel in Tuscaloosa, Alabama (branded **Druid City Hotel** in the client). Guests search availability, see the full cost of a stay, and book online. Staff manage reservations, availability, rates, and guest communication.

The long-term project is an "AI Concierge for Hospitality Bookings".

**Current phase: Sprint 5: integrating the ASP.NET Core backend (`API/`) with the existing front end, and adding AI.** This phase adds:

- **An ASP.NET Core Web API in `API/`** that serves the client's data over HTTP.
- **A MySQL database** behind the API, with full CRUD (create, read, update, delete) for reservations, rooms, holds/blocks, rates, and guests.
- **AI functionality** using Claude through the **official Anthropic SDK**, starting with the Druid AI Assistant concierge chat.

**Live:** the Azure site runs on the MySQL database (Zach's PR #2 plus fixes, merged in PR #3).

**Also live:** RAG for the concierge (knowledge documents searched with model-written, validated read-only SQL as `druid_llm_ro`). Shipped ahead of Mr. Rogers' sign-off; he still needs to confirm it (README assumption 25).

Success definition from the client: the site should make life easy, both for guests booking and for staff managing.

See [README.md](README.md) for the route table, hotel model details, and the running list of assumptions to confirm with Mr. Rogers.

## Running

The API serves the client too (locally straight from `Client/`; published builds copy it into `wwwroot`), so one command runs everything. Opening `index.html` from disk can't call the API.

```sh
dotnet run --project API --launch-profile http          # site and API on http://localhost:5269
```

**Deployment:** Azure App Service (Linux, single instance) via [.github/workflows/deploy-azure.yml](.github/workflows/deploy-azure.yml) on every push to `main`. On Azure, `ANTHROPIC_API_KEY` is an App Service environment variable. Setup steps are in the README.

There is no test suite or linter. Verify changes by loading the site and walking through the affected views on both a narrow (phone) and wide (desktop) viewport.

The API needs MySQL: `DB_APP_CONNECTION` (the `druid_app` user on the `mis321` schema) in the repo-root `.env`. Locally that can point at your own MySQL or at the shared Azure database. One-time setup (run `API/Database/schema.sql`, then `seed.sql`, as an admin account) is in the README. If the database is unreachable, the API returns a friendly "offline" message.

The AI concierge is part of the API (`POST api/concierge`). It needs `ANTHROPIC_API_KEY` in the repo-root `.env`; without it the chat shows a friendly error and the rest of the site keeps working.

## Structure

```
.env                          Secrets (ANTHROPIC_API_KEY, DB_APP_CONNECTION, DB_LLM_RO_CONNECTION, DB admin login). Git-ignored; never commit.
.env.example                  Variable names with placeholders (committed)
.github/workflows/deploy-azure.yml   Publish and deploy to Azure App Service on push to main
API/                          ASP.NET Core Web API (.NET 10, controllers); also serves Client/
├── Controllers/              One controller per resource (rooms, rates, reservations, holds, blocks, demo)
├── Models/HotelModels.cs     Response shapes and request bodies
├── Services/HotelStore.cs    All booking, availability, and pricing rules, on MySQL (Dapper)
├── Services/HotelRuleExceptionFilter.cs   Rule errors → 400/404, database errors → friendly 503
├── Services/DotEnv.cs        Loads the repo-root .env at startup
├── Controllers/ConciergeController.cs   POST api/concierge (streams NDJSON, rate limited)
├── Database/
│   ├── schema.sql            Tables (run by hand as admin)
│   ├── seed.sql              Hotel, room types, 120 rooms (run once by hand as admin)
│   ├── migrate-*.sql         Changes for databases built from an older schema.sql (run by hand as admin)
│   ├── create-llm-ro-user.sql   Creates druid_llm_ro (SELECT on v_knowledge_documents only)
│   ├── Db.cs                 Opens connections as druid_app from DB_APP_CONNECTION
│   ├── ReadOnlyDb.cs         Opens connections as druid_llm_ro from DB_LLM_RO_CONNECTION (with time caps)
│   └── SampleData.cs         Sample reservations, hold, and event block for "Reset sample data"
└── Concierge/
    ├── hotel-info.json       Editable hotel facts loaded into the concierge prompt (TODOs = unknown)
    ├── ConciergePrompt.cs    Server-side system prompt (identity, scope, page context)
    ├── ConciergeService.cs   Anthropic SDK call, history window, streaming, tool loop
    ├── ConciergeTools.cs     Tool registry (knowledge tools when DB_LLM_RO_CONNECTION is set)
    ├── KnowledgeTools.cs     get_knowledge_schema and search_knowledge (RAG, read-only)
    └── KnowledgeQueryValidator.cs   Checks model-written SQL before it runs
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
- **Secrets live in `.env` only.** Read `ANTHROPIC_API_KEY` and `DB_APP_CONNECTION` from the environment. Never hard-code keys, never put them in `Client/`, never log them, and never commit `.env`.
- **Payment form is UI only.** Validate in the browser, then discard. Never store, log, or transmit card numbers, expiry, or CVC. A reservation records only paid/not-paid status and whether the guest opted into card-on-file. Card-on-file is an optional checkbox.
- **Scope changes go through Mr. Rogers.** Do not add significant features beyond the spec without the user confirming; flag ideas instead.

## Backend, database, and AI

**Backend:** C# / ASP.NET Core Web API (`API/`, .NET 10), including the AI concierge. **Database:** MySQL 8 (`mis321` schema), accessed with MySqlConnector + Dapper. Live: Azure Database for MySQL Flexible Server `druid-city-db` (see "Live database on Azure" below).

- **CRUD endpoints** cover everything the `api` object exposes today (reservations, rooms, availability, holds, event blocks, rates, guests). Keep the request and response shapes matching what the views already expect, so swapping the `api` methods to `fetch()` doesn't require view changes.
- **Validate on the server** as well as in the browser. Never trust client input, and use parameterized queries (no SQL built by string concatenation).
- **Business rules belong to the backend:** availability, rate calculation (weekend = Friday/Saturday nights), and "existing reservations keep their booked totals". The client may display them but the server is the source of truth.
- **The database never stores card data.** The payment rule above applies to the database too: store only paid/not-paid status and the card-on-file opt-in.
- **Availability is calculated, not stored:** from booking, `maintenance_block` (holds), and event block overlaps. Writes that take a room lock the room rows (`SELECT ... FOR UPDATE`) and re-check overlaps in one transaction, so the same room can't be booked twice.
- **Seed data:** `seed.sql` loads the hotel, room types, and 120 rooms. "Reset sample data" (`POST api/demo/reset`) clears bookings, guests, holds, blocks, and special rates, restores default rates, and writes the sample data as `druid_app`.
- **Reset is destructive once data persists.** It only runs in Development or while the server has `ALLOW_SAMPLE_RESET=true`; otherwise it returns 403. Keep it that way until staff sign-in exists.
- **Use hotel-local time for "today".** The hotel is in Tuscaloosa (`America/Chicago`) and Azure runs on UTC. Business dates (past-arrival checks, hold/block filters, sample-data dates) use `HotelStore.Today()`, never `DateTime.Now` or `DateTime.Today`.
- **Guest records aren't rewritten by bookings.** `guest` is one row per email from the first booking; each `booking` keeps its own `guest_first_name` / `guest_last_name` / `guest_phone`, and staff screens read those. A booking with an existing email must not change another reservation's contact details.
- **Validate every date range on the server:** checkout after checkin, a maximum stay length, and inactive rooms excluded, on every endpoint that takes dates (including the single-room check), not just search and booking.
- **Writes must report what actually happened.** If an UPDATE affects 0 rows, return an error instead of 200. Repeat actions, such as cancelling a booking that is already cancelled, must not add duplicate side effects like log messages.
- **Staff lists must scale with persisted data.** Filter by date or status, or page the results. Don't load every booking ever stored.
- **One copy of write logic.** Sample-data seeding should reuse `HotelStore`'s insert and rate functions, not keep its own copies.
- **Schema changes:** update `schema.sql` (fresh installs) and add a numbered `migrate-NNN-*.sql` for existing databases, including the Azure one.
- **Hotel facts: the database wins.** When `hotel-info.json` or the concierge's knowledge documents disagree with the database (for example `hotel` check-in/out times, `room_type` sizes, occupancy, and rates), treat the database as correct and update the JSON and documents to match. Facts the database doesn't hold (taxes, policies, amenities, address) stay TODO until Mr. Rogers confirms them.

### Live database on Azure

- **Server:** `druid-city-db.mysql.database.azure.com` (Burstable B1ms, MySQL 8.0, 32 GB, auto-grow off), resource group `TuscaloosaDruidHotel_group`, region **Belgium Central**. Azure for Students only offers MySQL there and in Denmark East, not in North Central US where the App Service is. Expect a little latency per query.
- **Free offer:** 12 months of B1ms on Azure for Students (started October 2026). Don't change the size, storage, auto-grow, or high availability, or it becomes billable.
- **Logins:** admin (`DB_ADMIN_USER` / `DB_ADMIN_PASSWORD` in `.env`) for schema changes only; the API uses `druid_app`. The App Service has `DB_APP_CONNECTION` set as an environment variable.
- **Firewall:** "Allow Azure services" plus individual developer IPs. Add yours to load SQL from your machine.
- **Client tool:** connect with a MySQL 8.x client (`brew install mysql-client@8.4`). Newer clients can't use the server's `mysql_native_password` login.
- **Loading sample data on the live site:** set `ALLOW_SAMPLE_RESET=true` on the App Service, click Reset sample data once, then remove the setting.

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
- `druid_llm_ro`: SELECT only, on read-only views. Today that is `v_knowledge_documents` only; add views (for example room types or rates) one grant at a time. No access to guest, payment, or booking_message data. Connection and execution time capped. Used only by `ReadOnlyDb`.
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

- Current state: Druid AI Assistant answers hotel and booking questions only (from `hotel-info.json`, or from the knowledge documents when `DB_LLM_RO_CONNECTION` is set), is aware of which site section the user is on (main / booking / employee portal), and is restricted to booking-related topics.
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

- **All data access goes through the `api` object**, which wraps `fetch()` via `request()` against the same-origin `/api`. Views never call `fetch()` directly. JSON shapes from the API match what the views expect; keep them in sync when changing either side.
- `ROOM_TYPES` is loaded from `GET api/rooms/types` at startup, before the router runs.
- Server data lives in MySQL and survives API restarts. The Staff Portal's "Reset sample data" link restores the sample reservations, hold, and event block.
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
