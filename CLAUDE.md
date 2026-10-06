# CLAUDE.md

Guidance for Claude Code when working in this repository.

## Project

End-to-end hotel booking platform for Mr. Rogers' hotel in Tuscaloosa, Alabama (branded **Druid City Hotel** in the client). Guests search availability, see the full cost of a stay, and book online. Staff manage reservations, availability, rates, and guest communication.

The long-term project is an "AI Concierge for Hospitality Bookings", but **Sprint 1 is design and core reservation flow only: no AI functionality.**

Success definition from the client: the site should make life easy, both for guests booking and for staff managing.

See [README.md](README.md) for the route table, hotel model details, and the running list of assumptions to confirm with Mr. Rogers.

## Running

No build step, no dependencies. Open `Client/index.html` directly, or serve the folder:

```sh
python3 -m http.server 8000 --directory Client
```

There is no test suite or linter. Verify changes by loading the site and walking through the affected views on both a narrow (phone) and wide (desktop) viewport.

## Structure

```
API/                          Empty in Sprint 1. Backend goes here later.
Client/
├── Resources/
│   ├── images/               Placeholder photography (must be replaced before launch)
│   ├── styles/index.css      All styling
│   └── scripts/index.js      All behavior, routing, and mock data
└── index.html                The only HTML file
```

## Hard rules

- **Single-page app.** `index.html` is the only HTML file. Never create additional HTML files.
- **No inline styles or scripts** in `index.html`. All styling goes in `index.css`; all behavior goes in `index.js`. `index.html` only links to those two files (plus Google Fonts).
- **Plain HTML, CSS, and JavaScript.** No frameworks, libraries, or build tools unless the user explicitly asks.
- **Hash-based routing** (`#/home`, `#/search`, `#/booking`, `#/confirmation`, `#/rooms`, `#/staff/...`). Views must be linkable, back/forward must work, and unknown routes fall back to Home. The site must run without special server configuration.
- **Each site area is a view inside `index.html`**: a `<section class="view" data-view="...">` shown/hidden by the router.
- **Leave `API/` empty** in Sprint 1.
- **Payment form is UI only.** Validate in the browser, then discard. Never store, log, or transmit card numbers, expiry, or CVC. A reservation records only paid/not-paid status and whether the guest opted into card-on-file. Card-on-file is an optional checkbox.
- **Scope changes go through Mr. Rogers.** Do not add significant features beyond the spec without the user confirming; flag ideas instead.

## Code conventions (`index.js`)

The file is one IIFE in `'use strict'` mode, organized into numbered sections. Keep new code in the matching section:

1. Utilities (dates, money, escaping)
2. Hotel configuration (room types, amenities)
3. Mock data store and `api`
4. Router
5. Views
6. Startup

- **All data access goes through the `api` object.** Every `api` method returns a Promise so its body can later be swapped for a `fetch()` call to the backend without touching view code. Views must never read or write the mock store directly.
- Mock data persists in `localStorage`. The Staff Portal has a "Reset sample data" link.
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

The portal currently has no sign-in (see README assumptions).

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
