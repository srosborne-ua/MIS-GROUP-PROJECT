# Druid City Hotel — Booking Platform

An end-to-end hotel booking platform for Mr. Rogers' hotel in Tuscaloosa, Alabama. Guests search availability, see the full cost of a stay, and book online. Staff manage reservations, availability, rates, and guest communication.

The client's measure of success: the site should make life easy, both for guests booking and for staff managing.

## Status

**Sprint 5.** The guest site, the booking flow, and the Staff Portal are built. This sprint has three parts:

| Work | State |
| --- | --- |
| **Backend API:** an ASP.NET Core Web API in `API/` that owns every pricing, availability, and booking rule | Built; the front end calls it for all data |
| **Database:** replace the in-memory store with full CRUD for reservations, rooms, holds, event blocks, rates, and guests | Not started; database engine still to be decided |
| **AI:** the Druid AI Assistant concierge chat, using Claude through the official Anthropic C# SDK | First version built (questions and answers only) |

## Running the site

The site needs two processes: the API (.NET 10 SDK) and a static server for the client.

```sh
dotnet run --project API --launch-profile http          # API on http://localhost:5269
python3 -m http.server 8000 --directory Client          # site on http://localhost:8000
```

Then visit <http://localhost:8000>. Opening `Client/index.html` directly from disk won't work, because the browser won't let a `file://` page call the API. If you serve the client on a different port, add that origin to `Cors:AllowedOrigins` in `API/appsettings.json`.

The AI concierge needs `ANTHROPIC_API_KEY` in a `.env` file at the project root (git-ignored; never commit it). Without the key, the chat shows a friendly error and the rest of the site works normally.

There is no automated test suite. Check changes by walking through the affected views at both phone and desktop widths.

## Project structure

```
.env                          Secrets (ANTHROPIC_API_KEY). Git-ignored.
API/                          ASP.NET Core Web API (.NET 10)
├── Controllers/              One controller per resource
├── Models/HotelModels.cs     Response shapes and request bodies
├── Services/HotelStore.cs    In-memory data, sample data, and all booking and pricing rules
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

The **Reset sample data** link restores the 120-room sample hotel with its sample reservations, hold, and event block.

## Hotel model

- **Floors:** 3 floors with 40 rooms each, numbered 101–140, 201–240, and 301–340.
- **Room mix:** 38 King rooms, 41 Double Queen rooms, 39 Twin rooms, and 2 Master Suites (rooms 339 and 340).
- **Default rates:** $100 per weeknight and $150 per weekend night. Friday and Saturday count as weekend nights.
- **Rate changes:** staff can edit the default rates and add special rates for single dates. Changes apply to new searches only; existing reservations keep the totals they were booked at.

## Backend and data

The browser talks only to our API; it never calls Anthropic or a database directly.

- **The server owns the rules.** Pricing, availability, and booking validation live in `API/Services/HotelStore.cs`. The client checks forms for fast feedback but never decides prices or availability.
- **One `api` object on the client.** All data access in `index.js` goes through it (section 3), calling `http://localhost:5269/api`.
- **Errors** come back as `{ "message": "..." }` in the site's voice and are shown to the user as-is.
- **Storage:** data is in memory for now and **resets when the API restarts**. The database will replace it with the same request and response shapes, so the views won't need to change.

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

14. **Backend stack:** the backend is C# only: an ASP.NET Core Web API, which also hosts the AI concierge. The database engine is still to be decided.
15. **AI concierge scope:** answers hotel and booking questions only. It cannot book, check availability, or take payment; it sends guests to the booking page. It only states facts from `hotel-info.json`.
16. **Concierge and online payment:** the concierge tells guests online payment is "coming soon" (`payment.online_payment_available: false`), even though the booking form already offers a UI-only "pay now" option. Flip the flag once real payment ships.
17. **Concierge welcome card:** it shows on first visit and tucks into a small "Ask the concierge" button when the visitor scrolls or uses the page, so it never covers content. If a guest minimizes it, it stays minimized for the browser session.
