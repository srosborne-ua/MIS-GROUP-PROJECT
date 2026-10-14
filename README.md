# Druid City Hotel — Booking Platform

An end-to-end hotel booking experience for Mr. Rogers' hotel in Tuscaloosa, Alabama. Guests can search availability, see the full cost of a stay, and book online. Staff can manage reservations, rooms, and rates.

**Status:** Sprint 5. The core site and standard reservation flow are built; we are now integrating the ASP.NET Core backend in `API/` with the front end. There is no AI functionality yet.

## Running the site

The site needs two processes: the ASP.NET Core API (.NET 10 SDK) and a static server for the client.

```sh
dotnet run --project API --launch-profile http         # API on http://localhost:5269
python3 -m http.server 8000 --directory Client          # site on http://localhost:8000
```

Then visit <http://localhost:8000>. Opening `Client/index.html` directly from disk no longer works, because the browser won't let a `file://` page call the API. If you serve the client on a different port, add that origin to `Cors:AllowedOrigins` in `API/appsettings.json`.

### The AI concierge

The Druid AI Assistant chat is part of the API (`POST api/concierge`), so the two commands above run it too. It reads `ANTHROPIC_API_KEY` (and optionally `CONCIERGE_MODEL`) from `.env` in the project root. Its hotel facts live in `API/Concierge/hotel-info.json`; restart the API after editing that file. The rest of the site works without the key.

## Project structure

```
API/                         ASP.NET Core Web API (controllers)
Client/
├── Resources/
│   ├── images/              Placeholder photography
│   ├── styles/index.css     All styling
│   └── scripts/index.js     All behavior, routing, and API calls
└── index.html               The only HTML file
```

`index.html` contains no inline styles or scripts.

## How it works

### Single-page app with hash routing

Each area of the site is a `<section class="view">` inside `index.html`, and the router in `index.js` shows one at a time. Views are linkable, the back and forward buttons work, and unknown routes fall back to Home.

| Route | View |
| --- | --- |
| `#/home` | Home: hero, quick search, the hotel, amenities, location |
| `#/search?checkin=…&checkout=…&guests=…&bed=…&floor=…` | Search and availability |
| `#/booking?room=…&checkin=…&checkout=…&guests=…` | Booking and checkout |
| `#/confirmation?id=…` | Booking confirmation |
| `#/rooms` | Property and room information |
| `#/staff/reservations` | Staff: reservation list, customer details, payment status, cancel, guest messages |
| `#/staff/new` | Staff: create a reservation |
| `#/staff/rooms` | Staff: nightly room board, place and release holds on individual rooms |
| `#/staff/events` | Staff: block groups of rooms for events |
| `#/staff/rates` | Staff: edit default rates, add special date rates, preview the next 14 nights |

### Data and the API

All data access goes through a single `api` object in `index.js` (section 3), which calls the API at `http://localhost:5269/api`. Pricing, availability, and every booking rule live on the server in `API/Services/HotelStore.cs`; the controllers in `API/Controllers` expose them. Errors come back as `{ "message": "..." }` and are shown to the user as-is.

For now the server keeps data in memory, seeded with sample reservations, a hold, and an event block. **Everything resets when the API restarts.** The **Reset sample data** link in the Staff Portal also restores the sample data. A database comes next.

### Hotel model

- **Floors:** 3 floors with 40 rooms each, numbered 101–140, 201–240, and 301–340.
- **Room mix:** 38 King rooms, 41 Double Queen rooms, 39 Twin rooms, and 2 Master Suites (rooms 339 and 340).
- **Default rates:** $100 per weeknight and $150 per weekend night. Friday and Saturday count as weekend nights.
- **Rate changes:** staff can edit the default rates and add special rates for single dates. Changes apply to new searches only; existing reservations keep the totals they were booked at.

### Payment

The payment form is UI only. Card fields are validated in the browser (Luhn check on the card number, expiry, security code, and ZIP) and then discarded. Card details are never stored or sent anywhere. A reservation records only whether it was paid and whether the guest opted to keep a card on file.

## Design

- **Feel:** minimal, editorial, boutique hotel, with a local Tuscaloosa character.
- **Typography:** Cormorant Garamond for headings and Inter for body text, both loaded from Google Fonts.
- **Color:** a neutral palette of off-white, warm gray, and near-black, with one muted crimson accent. Colors are defined as tokens in `:root` in `index.css`.
- **Layout:** mobile-first and responsive.
- **Accessibility:** semantic HTML, labeled fields, inline error messages linked to their fields, visible focus states, a skip link, and focus moved to the page heading on each view change. Room status on the staff board is shown in text as well as color.

## Assumptions to confirm with Mr. Rogers

1. **Suites:** the two Master Suites are rooms 339 and 340 and count toward the 120 rooms; they are not additional rooms.
2. **Bed types:** "Queen/twin" means two separate room types. Double Queen rooms have two queen beds and sleep 4. Twin rooms have two twin beds and sleep 2. King rooms have one king bed and sleep 2.
3. **Suite rates:** the spec gives no suite rate, so suites default to $100/$150 like every other room. Staff can set suite rates separately.
4. **Special date rates:** we read "view/edit cost data" to include one-night special rates, for example on game days. A special rate applies to every room on that night.
5. **Rate changes:** new rates do not change the totals of existing reservations.
6. **Taxes:** prices are shown before taxes; lodging tax is not yet included.
7. **Paying online:** guests can either pay now or pay at the hotel, since whether guests should pay on the site is still an open question. "Keep card on file" is optional and only offered with pay now.
8. **Check-in and stay limits:** check-in is from 3:00 PM and check-out is by 11:00 AM. Online stays are capped at 30 nights.
9. **Staff sign-in:** the Staff Portal has no sign-in yet. It is reachable from a footer link.
10. **Holds and event blocks:** both use a first night and a last night, and neither can overlap an existing booking.
11. **Guest communication:** confirmation emails, pre-arrival notes, and staff messages are simulated. They are logged on the reservation in the Staff Portal, but nothing is actually sent.
12. **Placeholder content:** the amenities copy, room sizes, and street address need to be confirmed or replaced.
13. **Photography:** both photos are stand-ins from another hotel brand and must be replaced with photos of the property before launch.
14. **AI concierge (Druid AI Assistant):** answers hotel and booking questions only. It cannot book, check availability, or take payment; it sends guests to the booking page. It only states facts from `API/Concierge/hotel-info.json`, and every TODO in that file is treated as unknown (the guest is referred to the front desk).
15. **Concierge and online payment:** the concierge currently tells guests online payment is "coming soon" (`payment.online_payment_available: false`), even though the booking form already offers a UI-only "pay now" option. Flip the flag once real payment ships.
16. **Concierge welcome card:** it shows on first visit and tucks into a small "Ask the concierge" button when the visitor scrolls or uses the page, so it never covers content. If a guest minimizes it, it stays minimized for the browser session.
17. **Backend stack:** the backend is C# only: an ASP.NET Core Web API, which also hosts the AI concierge using the official Anthropic C# SDK. The database engine is still to be decided.
