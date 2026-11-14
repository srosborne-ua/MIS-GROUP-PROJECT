# Druid City Hotel — Booking Platform

An end-to-end hotel booking platform for Mr. Rogers' hotel in Tuscaloosa, Alabama. Guests search availability, see the full cost of a stay, and book online. Staff manage reservations, availability, rates, and guest communication. The client's measure of success: the site should make life easy, both for guests booking and for staff managing.

**Status (Sprint 5):** the guest site, booking flow, and Staff Portal are built on an ASP.NET Core API (`API/`) backed by MySQL, with guest accounts, emailed confirmations, optional Stripe payments, and the Druid AI Assistant, a Claude-powered concierge that answers hotel questions and books rooms end to end.

## Running the site

Needs the .NET 10 SDK and MySQL 8.0.16+. Put the `druid_app` connection string in `.env` as `DB_APP_CONNECTION`: your own local MySQL or the shared Azure database (ask a teammate, and add your IP to its firewall). `.env.example` lists every variable.

**One-time local database setup** (as an admin account):

1. Create the `mis321` schema, then run `API/Database/schema.sql` and `API/Database/seed.sql` (120 rooms).
2. Create `druid_app` with `SELECT, INSERT, UPDATE, DELETE` on `mis321` only.
3. Create a manager account: `dotnet run --project API -- add-employee`.
4. Start the API, sign in at `#/staff`, and click **Reset sample data**.

If your database predates the current `schema.sql`, run the `API/Database/migrate-*.sql` files you haven't run, in order.

```sh
dotnet run --project API --launch-profile http   # site and API on http://localhost:5269
```

The API serves `Client/` too, so edits show up on refresh. Opening `index.html` from disk won't work. There is no automated test suite; check changes at phone and desktop widths.

Optional settings; the site works without each of them:

| Variable | Turns on |
| --- | --- |
| `ANTHROPIC_API_KEY` | The concierge chat (`CONCIERGE_MODEL` overrides the model) |
| `DB_LLM_RO_CONNECTION` | The concierge's database lookups (`create-llm-ro-user.sql`) |
| `ACS_CONNECTION_STRING`, `ACS_SENDER_ADDRESS`, `SITE_URL` | Emails through Azure Communication Services (below) |
| `STRIPE_SECRET_KEY` | Card payments on Stripe's hosted Checkout page |

**Email.** Booking confirmations, cancellation notices, and guest password-reset links are sent as do-not-reply messages through Azure Communication Services (`EmailSender`). `ACS_CONNECTION_STRING` holds an access key: keep it in `.env` or the App Service settings, never in git or logs. `SITE_URL` is the live address used for links in emails. Unset, the site sends nothing and reservation notes say "not sent". The free `azurecomm.net` domain often lands in spam and has low rate limits.

## Deploying to Azure

The site runs on one Azure App Service (Linux, .NET 10): `dotnet publish` copies `Client/` into `wwwroot`. [deploy-azure.yml](.github/workflows/deploy-azure.yml) deploys on every push to `main`, which reaches production within about a minute. **Run any new migration on Azure first**, since code that expects new tables or columns fails every booking until it runs.

One-time setup: create the Web App (.NET 10, Linux), turn on HTTPS Only, add the environment variables above plus `DB_APP_CONNECTION`, enable SCM Basic Auth publishing, and add the GitHub secret `AZURE_WEBAPP_PUBLISH_PROFILE` and variable `AZURE_WEBAPP_NAME`. Keep **one instance**: the concierge rate limits live in memory.

**Live database:** Azure Database for MySQL Flexible Server `druid-city-db` (resource group `TuscaloosaDruidHotel_group`, Belgium Central, the only region the student offer allows besides Denmark East). It uses the free 12-month Burstable B1ms offer; don't change the size, storage, auto-grow, or high availability, or it becomes billable. Run SQL from your machine with a MySQL 8.x client (`brew install mysql-client@8.4`, `--ssl-mode=REQUIRED`) as the admin login after adding your IP to the firewall. To load sample data on the live site, set `ALLOW_SAMPLE_RESET=true`, click **Reset sample data** once, then remove it.

## Project structure

```
.env                      Secrets (git-ignored); .env.example lists the names
API/                      ASP.NET Core Web API (.NET 10)
├── Controllers/          One controller per resource
├── Models/               Response shapes and request bodies
├── Services/             HotelStore (all booking, availability, and pricing rules), EmployeeStore,
│                         GuestAccountStore, EmailSender, StripePayments, sessions, PasswordHasher
├── Database/             schema.sql, seed.sql, migrate-*.sql, connection factories, sample data
└── Concierge/            Prompt, hotel facts, Anthropic SDK call, tools, SQL validator
Client/                   index.html (the only HTML file), Resources/styles/index.css, Resources/scripts/index.js
```

## Guest site

A single-page app: each area is a `<section class="view">` in `index.html`, shown by a hash router in `index.js`. Views are linkable, back and forward work, and unknown routes fall back to Home.

| Route | View |
| --- | --- |
| `#/home` | Hero, quick search, the hotel, amenities, location |
| `#/search?checkin=…&checkout=…&guests=…&bed=…&floor=…` | Each open room type with the stay's total and one room picked by the server, filtered by bed type and floor |
| `#/booking?room=…&checkin=…&checkout=…&guests=…` | Guest details and payment |
| `#/confirmation?id=…` | Booking confirmation |
| `#/rooms` | Room types with weeknight and Fri & Sat rates |
| `#/account` | Sign in, create an account, "Your stays", forgot password |
| `#/pay?id=…` | Pay for one of your own unpaid upcoming stays |

- **Accounts:** guests must sign in to book (on the page or in the chat). A booking uses the account's email, and an account sees only its own bookings. Passwords are PBKDF2-SHA256 hashes (8+ characters). The session is its own HttpOnly, SameSite=Strict cookie, never valid for staff. Emails aren't verified.
- **Payment:** guests pay now or at the hotel. With `STRIPE_SECRET_KEY` set, they pay on Stripe's Checkout page and a webhook (or the **Check payment** button) marks the stay paid. Without it, the card form is UI only: validated in the browser, then discarded. Card numbers are never stored, logged, or sent to our server or database; a reservation records only paid/not paid and the card-on-file opt-in.

## Staff Portal

Reached from a footer link. Employees sign in with email and password, and the API refuses every staff request until they do.

| Route | What staff can do |
| --- | --- |
| `#/staff/reservations` | View customer info and payment status, edit guest details, cancel, message guests |
| `#/staff/new` | Create a reservation |
| `#/staff/rooms` | Nightly room board; place and release holds on rooms |
| `#/staff/events` | Block off rooms for events |
| `#/staff/rates` | Edit default rates per room type, add special date rates, preview 14 nights |
| `#/staff/employees` | Weekly shift schedule and staff accounts |

- **Roles:** `manager` or `staff`. Both do all front-desk work; only managers change accounts or the schedule and use Reset sample data. The API enforces this, and the page only hides controls.
- **Sign-in:** 12-hour HttpOnly, SameSite=Strict cookie; passwords of 12+ characters; 10 attempts per 15 minutes per IP. Every endpoint is staff-only unless marked `[AllowAnonymous]`.
- **Reset sample data** clears bookings, guests, holds, blocks, and special rates and reloads the samples. It keeps staff and guest accounts, needs a manager, and runs only in Development or with `ALLOW_SAMPLE_RESET=true`.

## Hotel model

- **Rooms:** 3 floors × 40 rooms (101–140, 201–240, 301–340): 38 King, 41 Double Queen, 39 Twin, and 2 Master Suites (339, 340).
- **Default rates** (weeknight / weekend): Twin $90 / $135, King $110 / $160, Double Queen $130 / $185, Master Suite $250 / $325. Friday and Saturday nights are weekend nights. Rates are edited per room type in the portal and apply to new searches only; existing reservations keep their booked totals.
- Prices and hotel facts live in the database, so changing `seed.sql` or `HotelStore.DefaultRates` changes nothing on a running site. Ship a migration.

## Backend and data

The browser talks only to our API, never to Anthropic or the database.

- **The server owns the rules:** pricing, availability, and validation live in `HotelStore`. The client pre-validates for fast feedback but never decides prices. Errors return `{ "message": "..." }` in the site's voice.
- **Availability is calculated, not stored,** from confirmed bookings, holds, and event blocks. Writes lock the room rows and re-check overlaps in one transaction, so a room can't be double-booked.
- **Hotel-local time:** "today" is `America/Chicago` (`HotelStore.Today()`), since Azure runs on UTC.
- **Safe writes:** SQL is parameterized, every date range is validated on the server, and an update that changes 0 rows is an error, not a 200.

| Resource | Endpoints |
| --- | --- |
| Rooms | `GET api/rooms/types`, `api/rooms`, `api/rooms/availability`, `api/rooms/{n}/check`, `api/rooms/board`, `api/rooms/{n}/schedule` |
| Reservations | `GET/POST api/reservations`, `GET api/reservations/{id}`, `PUT …/guest`, `POST …/cancel`, `PUT …/payment-status`, `POST …/messages`, `GET …/stats` |
| Holds, event blocks | `GET/POST api/holds` and `api/blocks`, `DELETE …/{id}` |
| Rates | `GET/PUT api/rates`, `POST/DELETE api/rates/overrides`, `GET api/rates/preview` |
| Staff, shifts | `api/auth/*`, `api/employees/*`, `api/shifts/*`, `POST api/demo/reset` |
| Guest accounts | `api/account/*` (create, sign in/out, `me`, reservations, pay, cancel (unpaid upcoming stays only), forgot/reset password) |
| Payments | Stripe Checkout and webhook endpoints in `PaymentsController` |
| Concierge | `POST api/concierge` (streamed), `POST api/concierge/bookings` |

Open to everyone: room, availability, and rate reads, the concierge, and guest sign-in. A signed-in guest can also book and see their own stays. Staff-only options such as `source: "staff"` and `allowPast` count only with a staff session.

## AI concierge

The Druid AI Assistant is a chat widget on every page, served by `POST api/concierge` through the official Anthropic C# SDK (default model `claude-opus-5-5`). The system prompt stays on the server. It answers hotel and booking questions only, knows today's date and which site section the visitor is on, and shares no guest data.

**Tools** (`API/Concierge/`):

| Tool | What it does |
| --- | --- |
| `get_database_schema`, `query_database` | Claude writes one read-only SELECT against approved views (policies, room counts, rates, which nights rooms are taken). Needs `DB_LLM_RO_CONNECTION`; otherwise facts come from `hotel-info.json` |
| `check_availability` | Open rooms and exact totals from `HotelStore`'s own search, so prices match the booking page |
| `propose_booking` | Re-checks a room and shows a **Confirm booking** card. Writes nothing |
| `confirm_booking` | Books the open card, offered only when the guest's latest message accepts it |

**Booking in the chat:** `propose_booking` signs the exact offer (room, dates, guests, total) into a 10-minute token that only the browser sees. The guest then presses **Confirm booking** or replies yes; the server checks that the reply accepts a live offer for this account, and either path goes through `HotelStore.CreateReservationAsync`, which re-checks the room and refuses a changed total. The model can never book, change, or cancel without the guest's own acceptance. Chat bookings are due at check-in, and the booked card offers **Pay now**. Changing or cancelling bookings in the chat isn't built.

**Database safety, in layers:** (1) queries run only as `druid_llm_ro`, which has SELECT on the `v_*` views and nothing else, with time and connection caps; (2) the prompt says SELECT only and never to reveal SQL or the schema; (3) `KnowledgeQueryValidator` allows one statement starting with SELECT, rejects write keywords, comments, `;`, base tables, other schemas, and risky functions, and caps results at 50 rows; (4) only view schemas reach Claude, and database errors reach it as a generic message. The views hold no guest, booking, payment, or message data: only room numbers and the nights each is taken.

## Security and spend limits

- **Transport:** HTTPS Only on Azure; in Production the API trusts only the last `X-Forwarded-*` hop and sends HSTS.
- **Input:** holds and blocks run at most 366 nights, can't end in the past, and fall within about three years ahead. Deleting a missing hold, block, or rate reports "not found".
- **Concierge spend** (in-memory, so one instance): 6 messages/min and 60/hour per IP, 2 replies in progress per IP, 1,500 per day site-wide (`CONCIERGE_DAILY_LIMIT`), 6 messages of history, 6 tool turns per reply (the last has no tools), 32 KB requests, and 10 Confirm booking presses per 10 minutes per IP. Also set a spend limit on the Anthropic workspace.
- **Known gaps:** sequential reservation IDs (only staff or the booking's guest can look one up), unverified guest emails, no password-reset email for staff (a manager resets those), no rate limit on `POST api/reservations`, and no CSP or clickjacking headers.

## Design and accessibility

Minimal, editorial boutique-hotel feel with Tuscaloosa character. Cormorant Garamond for headings and Inter for body text; off-white, warm gray, and near-black with one muted crimson accent, all as tokens in `:root` in `index.css`. Mobile-first, with no inline styles or scripts. Semantic HTML, labeled fields with inline errors, full keyboard navigation, a skip link, focus moved to the heading on each view change, status never shown by color alone, and `prefers-reduced-motion` respected.
