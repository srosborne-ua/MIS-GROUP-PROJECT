# Druid City Hotel — Booking Platform

An end-to-end hotel booking experience for Mr. Rogers' hotel in Tuscaloosa, Alabama. Guests can search availability, see the full cost of a stay, and book online. Staff can manage reservations, rooms, and rates.

**Status:** Sprint 1. This is the core site and standard reservation flow. There is no AI functionality and no backend yet.

## Running the site

The site is plain HTML, CSS, and JavaScript with no build step. You can either:

- open `Client/index.html` directly in a browser, or
- serve the `Client` folder with any static server, for example:

  ```sh
  python3 -m http.server 8000 --directory Client
  ```

  Then visit <http://localhost:8000>.

## Project structure

```
API/                         Empty for Sprint 1; the backend goes here
Client/
├── Resources/
│   ├── images/              Placeholder photography
│   ├── styles/index.css     All styling
│   └── scripts/index.js     All behavior, routing, and mock data
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

### Mock data and the future API

All data access goes through a single `api` object in `index.js`, in section 3, "Mock data store and API". Every method returns a Promise, so each method body can later be replaced with a `fetch()` call to the backend without changing any view code.

Mock data is saved to the browser's `localStorage`, so changes survive a page refresh. To reset it, use the **Reset sample data** link at the top of the Staff Portal.

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
