# Druid City Hotel — Booking Platform

An end-to-end hotel booking platform for Mr. Rogers' hotel in Tuscaloosa, Alabama. Guests search availability, see the full cost of a stay, and book online. Staff manage reservations, availability, rates, and guest communication.

The client's measure of success: the site should make life easy, both for guests booking and for staff managing.

## Status

**Sprint 5.** The guest site, the booking flow, and the Staff Portal are built. This sprint has three parts:

| Work | State |
| --- | --- |
| **Backend API:** an ASP.NET Core Web API in `API/` that owns every pricing, availability, and booking rule | Built; the front end calls it for all data |
| **Database:** replace the in-memory store with full CRUD for reservations, rooms, holds, event blocks, rates, and guests | Built on MySQL; the API reads and writes it for all data |
| **AI:** the Druid AI Assistant concierge chat, using Claude through the official Anthropic C# SDK | First version built (questions and answers only) |

## Running the site

The site needs MySQL 8.0.16 or later. Put the `druid_app` connection string in `.env` as `DB_APP_CONNECTION`: either your own local MySQL (set up below) or the shared Azure database (ask a teammate for the connection string, and add your IP to its firewall).

**One-time local database setup** (as an admin account such as root, for example in MySQL Workbench):

1. Create the `mis321` schema, then run `API/Database/schema.sql` to create the tables.
2. Run `API/Database/seed.sql` to load the hotel, room types, and 120 rooms.
3. Create the app user `druid_app` with `SELECT, INSERT, UPDATE, DELETE` on `mis321` only, and put its connection string in `.env` as `DB_APP_CONNECTION`.
4. Start the API and click **Reset sample data** in the Staff Portal (or `POST api/demo/reset`) to load the sample reservations, hold, and event block.

If your database was built from an earlier `schema.sql`, run the `API/Database/migrate-*.sql` files you haven't run yet, in order.

The API (.NET 10 SDK) serves both the site and its data, so one command runs everything:

```sh
dotnet run --project API --launch-profile http          # site and API on http://localhost:5269
```

Then visit <http://localhost:5269>. Locally the API serves the `Client/` folder directly, so edits to the HTML, CSS, or JS show up on refresh. Opening `Client/index.html` directly from disk won't work, because the page calls the API at `/api` on the same address.

The AI concierge needs `ANTHROPIC_API_KEY` in a `.env` file at the project root (git-ignored; never commit it). Without the key, the chat shows a friendly error and the rest of the site works normally. If the database is unreachable, the API returns a friendly "offline" message instead.

There is no automated test suite. Check changes by walking through the affected views at both phone and desktop widths.

## Deploying to Azure

The site runs on Azure App Service as a single app: `dotnet publish` copies `Client/` into the API's `wwwroot`, and the API serves it. [.github/workflows/deploy-azure.yml](.github/workflows/deploy-azure.yml) publishes and deploys on every push to `main`.

One-time setup:

1. **Create the Web App** in the Azure portal: Publish **Code**, Runtime **.NET 10**, OS **Linux**, region **Central US** (closest to Tuscaloosa). Pricing plan **Free F1** for testing or **Basic B1** to stay always on.
2. **Turn on HTTPS Only** (Settings > Configuration).
3. **Add `ANTHROPIC_API_KEY`** under Settings > Environment variables (optionally `CONCIERGE_MODEL`). This replaces `.env`, which is never deployed.
4. **Allow publish-profile deployments:** Settings > Configuration > turn on "SCM Basic Auth Publishing Credentials". Then download the publish profile from the app's Overview page.
5. **In GitHub** (Settings > Secrets and variables > Actions): add the secret `AZURE_WEBAPP_PUBLISH_PROFILE` (paste the whole publish profile file) and the variable `AZURE_WEBAPP_NAME` (the Web App's name).
6. **Deploy:** push to `main`, or run the workflow from the Actions tab.

Keep the app at **one instance**: the concierge rate limit lives in the app's memory.

### Live database

The live data is in **Azure Database for MySQL – Flexible Server** `druid-city-db` (resource group `TuscaloosaDruidHotel_group`). It's in **Belgium Central** because Azure for Students offers MySQL only there and in Denmark East, so each query crosses the Atlantic. It uses the free 12-month offer (Burstable B1ms, 32 GB, storage auto-grow off, no high availability); changing any of those makes it billable.

How it was set up, for rebuilding it:

1. Create the server (MySQL 8.0, the settings above), the `mis321` database, and firewall rules for "Allow Azure services" and your own IP.
2. As the admin login, run `schema.sql` then `seed.sql`, using a MySQL 8.x client (`brew install mysql-client@8.4`; newer clients can't log in), with `--ssl-mode=REQUIRED`.
3. Create `druid_app` with `SELECT, INSERT, UPDATE, DELETE` on `mis321` only.
4. On the App Service, add the environment variable `DB_APP_CONNECTION` = `Server=druid-city-db.mysql.database.azure.com;Database=mis321;User ID=druid_app;Password=…;SslMode=Required;`.
5. To load the sample reservations, add `ALLOW_SAMPLE_RESET=true` to the App Service, click **Reset sample data** once, then remove it. Without that setting, Reset is turned off on the live site.

## Project structure

```
.env                          Secrets (ANTHROPIC_API_KEY, DB_APP_CONNECTION). Git-ignored.
API/                          ASP.NET Core Web API (.NET 10)
├── Controllers/              One controller per resource
├── Models/HotelModels.cs     Response shapes and request bodies
├── Services/HotelStore.cs    All booking, availability, and pricing rules, on MySQL
├── Database/                 schema.sql, seed.sql, connection factory, and sample data for Reset
├── Services/DotEnv.cs        Loads .env at startup
└── Concierge/                AI concierge: prompt, hotel facts, Anthropic SDK call, tools
Client/
├── Resources/
│   ├── images/               Placeholder photography
│   ├── styles/index.css      All styling
│   └── scripts/index.js      All behavior, routing, and API calls
└── index.html                The only HTML file
```

## Guest site

The guest side is a single-page app. Each area is a `<section class="view">` inside `index.html`, and a hash router in `index.js` shows one at a time. Views are linkable, back and forward work, and unknown routes fall back to Home.

| Route | View |
| --- | --- |
| `#/home` | Hero, quick search, the hotel, amenities, location |
| `#/search?checkin=…&checkout=…&guests=…&bed=…&floor=…` | Available rooms with the total cost of the stay, filtered by bed type and floor |
| `#/booking?room=…&checkin=…&checkout=…&guests=…` | Guest details and payment |
| `#/confirmation?id=…` | Booking confirmation |
| `#/rooms` | Property and room information |

Fast availability and cost lookup is a top client priority: search shows each available room with its full price for the chosen dates.

### Payment

The payment form is UI only. Card fields are validated in the browser (Luhn check on the card number, expiry, security code, and ZIP) and then discarded. Card details are never stored, logged, or sent anywhere, including to the database. A reservation records only whether it was paid and whether the guest opted to keep a card on file. Guests can choose to pay now or pay at the hotel.

## Staff Portal

Reached from a footer link. It covers every staff requirement from the client:

| Route | What staff can do |
| --- | --- |
| `#/staff/reservations` | View customer info and payment status (paid / not paid), cancel reservations, send guest messages |
| `#/staff/new` | Create a reservation |
| `#/staff/rooms` | See the nightly room board; place and release holds on individual rooms |
| `#/staff/events` | Block off groups of rooms for events |
| `#/staff/rates` | View and edit default rates, add special date rates, preview the next 14 nights |

The **Reset sample data** link clears every reservation, guest, hold, event block, and special rate in the database, restores the default rates, and loads the sample reservations, hold, and event block. Rooms and room types are left as `seed.sql` loaded them.

## Hotel model

- **Floors:** 3 floors with 40 rooms each, numbered 101–140, 201–240, and 301–340.
- **Room mix:** 38 King rooms, 41 Double Queen rooms, 39 Twin rooms, and 2 Master Suites (rooms 339 and 340).
- **Default rates:** $100 per weeknight and $150 per weekend night. Friday and Saturday count as weekend nights.
- **Rate changes:** staff can edit the default rates and add special rates for single dates. Changes apply to new searches only; existing reservations keep the totals they were booked at.

## Backend and data

The browser talks only to our API; it never calls Anthropic or a database directly.

- **The server owns the rules.** Pricing, availability, and booking validation live in `API/Services/HotelStore.cs`. The client checks forms for fast feedback but never decides prices or availability.
- **One `api` object on the client.** All data access in `index.js` goes through it (section 3), calling `/api` on the same address that served the page.
- **Errors** come back as `{ "message": "..." }` in the site's voice and are shown to the user as-is.
- **Storage:** MySQL (`mis321` schema), reached only by the API as `druid_app` with parameterized queries. Data survives API restarts.
- **Availability is calculated, not stored:** a room is open when no confirmed booking, hold, or event block overlaps the requested nights.
- **No double-booking:** creating a booking, hold, or event block locks the room rows, re-checks for overlaps, and then inserts, all in one transaction.

| Resource | Endpoints |
| --- | --- |
| Rooms | `GET api/rooms/types`, `GET api/rooms`, `GET api/rooms/availability`, `GET api/rooms/{number}/check`, `GET api/rooms/board`, `GET api/rooms/{number}/schedule` |
| Reservations | `GET api/reservations`, `GET api/reservations/{id}`, `POST api/reservations`, `POST api/reservations/{id}/cancel`, `PUT api/reservations/{id}/payment-status`, `POST api/reservations/{id}/messages` |
| Holds | `GET api/holds`, `POST api/holds`, `DELETE api/holds/{id}` |
| Event blocks | `GET api/blocks`, `POST api/blocks`, `DELETE api/blocks/{id}` |
| Rates | `GET api/rates`, `PUT api/rates`, `POST api/rates/overrides`, `DELETE api/rates/overrides/{date}`, `GET api/rates/preview` |
| Sample data | `POST api/demo/reset` |
| AI concierge | `POST api/concierge` (streamed, rate limited) |

## AI concierge

The Druid AI Assistant is a chat widget on every page, served by `POST api/concierge` using the official Anthropic C# SDK (default model `claude-opus-5-5`, overridable with `CONCIERGE_MODEL`).

- It answers hotel and booking questions only, and knows which part of the site the visitor is on (main site, booking, or Staff Portal).
- Its facts come from `API/Concierge/hotel-info.json`; restart the API after editing it. Anything marked TODO is treated as unknown, and the guest is referred to the front desk.
- The system prompt stays on the server. The browser sends only the chat messages and the current site section.
- It cannot book or check availability yet; it sends guests to the booking page. Booking and availability through the chat are planned, using tool calls into the same backend rules as the rest of the site, with explicit guest confirmation.

## Design and accessibility

- **Feel:** minimal, editorial, boutique hotel, with a local Tuscaloosa character.
- **Typography:** Cormorant Garamond for headings and Inter for body text, both from Google Fonts.
- **Color:** off-white, warm gray, and near-black, with one muted crimson accent. Colors are tokens in `:root` in `index.css`.
- **Layout:** mobile-first and responsive. `index.html` contains no inline styles or scripts.
- **Accessibility:** semantic HTML, labeled fields, inline errors linked to their fields, full keyboard navigation with visible focus, a skip link, and focus moved to the heading on each view change. Status is never shown by color alone. Motion respects `prefers-reduced-motion`.

## Assumptions to confirm with Mr. Rogers

**Rooms and rates**

1. **Suites:** the two Master Suites are rooms 339 and 340 and count toward the 120 rooms; they are not additional rooms.
2. **Bed types:** "Queen/twin" means two separate room types. Double Queen rooms have two queen beds and sleep 4. Twin rooms have two twin beds and sleep 2. King rooms have one king bed and sleep 2.
3. **Suite rates:** the spec gives no suite rate, so suites default to $100/$150 like every other room. Staff can set suite rates separately.
4. **Special date rates:** we read "view/edit cost data" to include one-night special rates, for example on game days. A special rate applies to every room on that night.
5. **Rate changes:** new rates do not change the totals of existing reservations.
6. **Taxes:** prices are shown before taxes; lodging tax is not yet included.

**Booking and payment**

7. **Paying online:** guests can either pay now or pay at the hotel, since whether guests should pay on the site is still an open question. "Keep card on file" is optional and only offered with pay now.
8. **Check-in and stay limits:** check-in is from 3:00 PM and check-out is by 11:00 AM. Online stays are capped at 30 nights.

**Staff Portal**

9. **Staff sign-in:** the Staff Portal has no sign-in yet. It is reachable from a footer link. Once the API and database are on a network, staff endpoints need authentication before launch.
10. **Holds and event blocks:** both use a first night and a last night, and neither can overlap an existing booking.
11. **Guest communication:** confirmation emails, pre-arrival notes, and staff messages are simulated. They are logged on the reservation in the Staff Portal, but nothing is actually sent.

**Content**

12. **Placeholder content:** the amenities copy, room sizes, and street address need to be confirmed or replaced.
13. **Photography:** both photos are stand-ins from another hotel brand and must be replaced with photos of the property before launch.

**Backend and AI**

14. **Backend stack:** the backend is C# only: an ASP.NET Core Web API, which also hosts the AI concierge. The database is MySQL, accessed with MySqlConnector and Dapper.
15. **AI concierge scope:** answers hotel and booking questions only. It cannot book, check availability, or take payment; it sends guests to the booking page. It only states facts from `hotel-info.json`.
16. **Concierge and online payment:** the concierge tells guests online payment is "coming soon" (`payment.online_payment_available: false`), even though the booking form already offers a UI-only "pay now" option. Flip the flag once real payment ships.
17. **Concierge welcome card:** it shows on first visit and tucks into a small "Ask the concierge" button when the visitor scrolls or uses the page, so it never covers content. If a guest minimizes it, it stays minimized for the browser session.
18. **Hosting:** the site is hosted on Azure App Service (Linux, North Central US) as one app that serves both the site and the API. Mr. Rogers to confirm the hosting cost and whether he wants a custom domain instead of the `azurewebsites.net` address.
19. **Public demo data:** while the site is public without staff sign-in, it holds sample data only, and anyone can open the Staff Portal. Real guest data must wait for staff authentication.

**Database**

20. **One guest per email, details per booking:** a returning guest (same email) shares one guest record, but each reservation keeps the name and phone entered with it. A new booking never changes the details on earlier reservations.
21. **Reset sample data deletes real data:** it clears every reservation and guest, so it is turned off on the live site unless `ALLOW_SAMPLE_RESET=true` is set on the server. It needs staff sign-in before launch.
22. **Payment method:** web bookings record "online" (pay now) or "at the hotel"; staff-created bookings leave the method blank.
23. **Database location:** the live database is in Belgium Central (the only free option for Azure for Students), while the site is in North Central US, which adds some delay to each page load. Mr. Rogers to confirm whether that's acceptable or whether to pay for a US-region database.
24. **"Today" is Tuscaloosa time:** past-arrival checks and the staff lists use the hotel's local date (Central Time), not the server's UTC clock.
