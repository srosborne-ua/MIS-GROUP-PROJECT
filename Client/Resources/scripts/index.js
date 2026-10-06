/* ==========================================================================
   Druid City Hotel — Sprint 1 single-page client

   Layout of this file:
     1. Utilities (dates, money, escaping)
     2. Hotel configuration (room types, amenities)
     3. Mock data store + `api` (swap these methods for fetch() calls to /API later;
        every method already returns a Promise, so the views will not change)
     4. Router (hash-based: #/home, #/search, #/booking, #/confirmation, #/rooms, #/staff)
     5. Views
     6. Startup
   ========================================================================== */

(function () {
    'use strict';

    /* ======================================================================
       1. Utilities
       ====================================================================== */

    const $ = (selector, root) => (root || document).querySelector(selector);
    const $$ = (selector, root) => Array.from((root || document).querySelectorAll(selector));

    function esc(value) {
        return String(value == null ? '' : value).replace(/[&<>"']/g, (c) => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
        }[c]));
    }

    const pad = (n) => String(n).padStart(2, '0');
    const toISO = (d) => d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());

    function parseISO(value) {
        if (!/^\d{4}-\d{2}-\d{2}$/.test(value || '')) return null;
        const [y, m, d] = value.split('-').map(Number);
        const date = new Date(y, m - 1, d);
        return date.getMonth() === m - 1 ? date : null;
    }

    function addDays(iso, days) {
        const d = parseISO(iso);
        d.setDate(d.getDate() + days);
        return toISO(d);
    }

    const todayISO = () => toISO(new Date());

    function nightsBetween(checkin, checkout) {
        const nights = [];
        for (let d = checkin; d < checkout; d = addDays(d, 1)) nights.push(d);
        return nights;
    }

    // Friday and Saturday nights are weekend nights.
    function isWeekendNight(iso) {
        const day = parseISO(iso).getDay();
        return day === 5 || day === 6;
    }

    function money(amount) {
        return new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' })
            .format(amount).replace(/\.00$/, '');
    }

    function fmtDate(iso, style) {
        const d = parseISO(iso);
        if (!d) return '';
        const options = style === 'short'
            ? { month: 'short', day: 'numeric' }
            : style === 'day'
                ? { weekday: 'short', month: 'short', day: 'numeric' }
                : { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric' };
        return d.toLocaleDateString('en-US', options);
    }

    function fmtTimestamp(iso) {
        return new Date(iso).toLocaleString('en-US', {
            month: 'short', day: 'numeric', hour: 'numeric', minute: '2-digit'
        });
    }

    const plural = (n, word, many) => n + ' ' + (n === 1 ? word : (many || word + 's'));

    // Returns an error message, or '' when the range is a valid future stay.
    function checkStayDates(checkin, checkout, allowPast) {
        if (!parseISO(checkin)) return 'Choose an arrival date.';
        if (!parseISO(checkout)) return 'Choose a departure date.';
        if (!allowPast && checkin < todayISO()) return 'Arrival can’t be in the past.';
        if (checkout <= checkin) return 'Departure must be after arrival.';
        if (nightsBetween(checkin, checkout).length > 30) return 'Online stays are limited to 30 nights. Call us for longer visits.';
        return '';
    }

    // Which date field a checkStayDates() message belongs to.
    const isArrivalError = (message) => /^(Choose an arrival|Arrival)/.test(message);

    // "201-205, 210" -> { rooms: [201..205, 210], invalid: [] }
    function parseRoomList(text, validNumbers) {
        const rooms = new Set();
        const invalid = [];
        String(text || '').split(',').map((s) => s.trim()).filter(Boolean).forEach((part) => {
            const range = part.match(/^(\d{3})\s*[-–]\s*(\d{3})$/);
            if (range) {
                const a = Number(range[1]);
                const b = Number(range[2]);
                if (a > b) { invalid.push(part); return; }
                for (let n = a; n <= b; n++) {
                    if (validNumbers.has(n)) rooms.add(n); else invalid.push(String(n));
                }
            } else if (/^\d{3}$/.test(part) && validNumbers.has(Number(part))) {
                rooms.add(Number(part));
            } else {
                invalid.push(part);
            }
        });
        return { rooms: Array.from(rooms).sort((a, b) => a - b), invalid };
    }

    // [201,202,203,207] -> "201–203, 207"
    function compressRooms(numbers) {
        const sorted = numbers.slice().sort((a, b) => a - b);
        const out = [];
        for (let i = 0; i < sorted.length; i++) {
            let j = i;
            while (j + 1 < sorted.length && sorted[j + 1] === sorted[j] + 1) j++;
            out.push(j > i ? sorted[i] + '–' + sorted[j] : String(sorted[i]));
            i = j;
        }
        return out.join(', ');
    }

    /* ======================================================================
       2. Hotel configuration
       ====================================================================== */

    const FLOORS = 3;
    const ROOMS_PER_FLOOR = 40;
    const SUITE_NUMBERS = [339, 340];
    const FLOOR_NAMES = { 1: 'First floor', 2: 'Second floor', 3: 'Third floor' };

    const ROOM_TYPES = {
        king: {
            name: 'The King',
            bed: 'king',
            bedLabel: 'One king bed',
            sleeps: 2,
            size: '340 sq ft',
            blurb: 'A generous king, a writing desk by the window, and a rain shower built for lingering.'
        },
        queen: {
            name: 'The Double Queen',
            bed: 'queen',
            bedLabel: 'Two queen beds',
            sleeps: 4,
            size: '380 sq ft',
            blurb: 'Room for the whole party: two queens, a lounge chair, and space to spread out on game weekend.'
        },
        twin: {
            name: 'The Twin',
            bed: 'twin',
            bedLabel: 'Two twin beds',
            sleeps: 2,
            size: '300 sq ft',
            blurb: 'Two crisply made twins for colleagues, siblings, or old roommates back for homecoming.'
        },
        suite: {
            name: 'The Master Suite',
            bed: 'king',
            bedLabel: 'One king bed and a separate parlor',
            sleeps: 4,
            size: '850 sq ft',
            blurb: 'Just two of them, on the top floor: a parlor for entertaining, a soaking tub, and a view over the oaks.'
        }
    };

    const AMENITIES = [
        ['Linens worth lingering in', 'High thread-count sheets, down-alternative duvets, and a pillow menu at the desk.'],
        ['Rain showers', 'Walk-in showers stocked with house-blended bath goods.'],
        ['Morning coffee, poured properly', 'Locally roasted coffee in the lobby from 6 AM, and a pour-over kit in every room.'],
        ['Fast, free Wi-Fi', 'Reliable enough for a deadline, quiet enough to ignore.'],
        ['Valet and on-site parking', 'Leave the car with us and walk downtown.'],
        ['A concierge who knows Tuscaloosa', 'Supper reservations, river walks, and how to get to the stadium on a Saturday.']
    ];

    /* ======================================================================
       3. Mock data store and API
       ----------------------------------------------------------------------
       Everything the views need goes through `api`. To connect the real
       backend, replace each method body with a fetch() to the API folder's
       service and return its JSON. Nothing below section 3 touches `db`.
       ====================================================================== */

    const STORAGE_KEY = 'druid-city-hotel:mock-db:v1';

    const DEFAULT_RATES = { weekday: 100, weekend: 150, suiteWeekday: 100, suiteWeekend: 150 };

    function buildRooms() {
        // 40 rooms per floor. Every third room is a king (~1/3 of the hotel);
        // the rest alternate queen and twin. 339 and 340 are the master suites.
        const rooms = [];
        for (let floor = 1; floor <= FLOORS; floor++) {
            for (let i = 1; i <= ROOMS_PER_FLOOR; i++) {
                const number = floor * 100 + i;
                let type;
                if (SUITE_NUMBERS.includes(number)) type = 'suite';
                else if (i % 3 === 0) type = 'king';
                else if (i % 2 === 0) type = 'queen';
                else type = 'twin';
                rooms.push({ number, floor, type });
            }
        }
        return rooms;
    }

    function nightlyRate(type, date, rates, overrides) {
        const special = overrides.find((o) => o.date === date);
        if (special) return { date, amount: special.amount, kind: 'special', label: special.label };
        const weekend = isWeekendNight(date);
        const amount = type === 'suite'
            ? (weekend ? rates.suiteWeekend : rates.suiteWeekday)
            : (weekend ? rates.weekend : rates.weekday);
        return { date, amount, kind: weekend ? 'weekend' : 'weekday' };
    }

    function priceStay(type, checkin, checkout, rates, overrides) {
        const nights = nightsBetween(checkin, checkout).map((d) => nightlyRate(type, d, rates, overrides));
        return { nights, total: nights.reduce((sum, n) => sum + n.amount, 0) };
    }

    function seedDb() {
        const t = todayISO();
        const rooms = buildRooms();
        const typeOf = (n) => rooms.find((r) => r.number === n).type;
        const db = {
            version: 1,
            rates: Object.assign({}, DEFAULT_RATES),
            rateOverrides: [],
            rooms,
            reservations: [],
            holds: [],
            blocks: [],
            nextId: 1042,
            nextHoldId: 1,
            nextBlockId: 1
        };

        const seeds = [
            // first, last, room, arrive offset, nights, guests, payment, card on file, requests, status
            ['Eleanor', 'Whitfield', 104, -1, 3, 2, 'paid', true, 'Late arrival, around 10 PM.'],
            ['Marcus', 'Bell', 212, 0, 3, 1, 'unpaid', false, ''],
            ['Priya', 'Natarajan', 339, 3, 3, 2, 'paid', true, 'Celebrating our anniversary.'],
            ['Thomas', 'Greer', 127, 1, 1, 1, 'unpaid', false, ''],
            ['Ruth', 'Abernathy', 232, 5, 2, 3, 'paid', false, 'Quiet room away from the elevator, please.'],
            ['Daniel', 'Okafor', 315, 7, 3, 2, 'unpaid', false, ''],
            ['Caroline', 'Hollis', 340, 12, 2, 4, 'paid', true, ''],
            ['Samuel', 'Price', 109, 2, 2, 1, 'paid', false, 'Need a receipt for work.'],
            ['Grace', 'Lee', 205, 4, 2, 2, 'unpaid', false, '', 'cancelled'],
            ['Henry', 'Castellanos', 136, -6, 2, 2, 'paid', false, '']
        ];

        seeds.forEach((s, i) => {
            const [first, last, room, offset, nights, guests, paymentStatus, cardOnFile, requests, status] = s;
            const checkin = addDays(t, offset);
            const checkout = addDays(checkin, nights);
            const type = typeOf(room);
            const quote = priceStay(type, checkin, checkout, db.rates, db.rateOverrides);
            const created = new Date(Date.now() - (14 - i) * 86400000).toISOString();
            const email = first.toLowerCase() + '.' + last.toLowerCase() + '@example.com';
            db.reservations.push({
                id: 'DC-' + (1030 + i),
                room, type, checkin, checkout, guests,
                guest: { firstName: first, lastName: last, email, phone: '(205) 555-01' + pad(10 + i), requests },
                nights: quote.nights,
                total: quote.total,
                paymentStatus,
                cardOnFile,
                source: i % 3 === 0 ? 'staff' : 'web',
                status: status || 'confirmed',
                createdAt: created,
                messages: [{ at: created, from: 'system', text: 'Confirmation emailed to ' + email + '.' }]
            });
        });

        db.holds.push({ id: 'H-' + db.nextHoldId++, room: 118, start: t, end: addDays(t, 2), reason: 'Maintenance: HVAC service', createdAt: new Date().toISOString() });
        db.blocks.push({
            id: 'B-' + db.nextBlockId++,
            name: 'Abernathy–Hollis wedding party',
            start: addDays(t, 20),
            end: addDays(t, 21),
            rooms: Array.from({ length: 10 }, (_, k) => 201 + k),
            createdAt: new Date().toISOString()
        });
        return db;
    }

    function loadDb() {
        try {
            const raw = localStorage.getItem(STORAGE_KEY);
            if (raw) {
                const parsed = JSON.parse(raw);
                if (parsed && parsed.version === 1) return parsed;
            }
        } catch (e) { /* storage unavailable: fall back to fresh sample data */ }
        return seedDb();
    }

    let db = loadDb();

    function saveDb() {
        try { localStorage.setItem(STORAGE_KEY, JSON.stringify(db)); } catch (e) { /* in-memory only */ }
    }

    const ok = (value) => Promise.resolve(JSON.parse(JSON.stringify(value)));
    const fail = (message) => Promise.reject(new Error(message));

    const overlaps = (aStart, aEndExcl, bStart, bEndExcl) => aStart < bEndExcl && bStart < aEndExcl;

    // Everything that occupies a room for any night in [checkin, checkout).
    // Holds and blocks store an inclusive last night.
    function commitmentsFor(roomNumber, checkin, checkout, ignore) {
        ignore = ignore || {};
        const found = [];
        db.reservations.forEach((r) => {
            if (r.status === 'confirmed' && r.room === roomNumber && r.id !== ignore.reservationId &&
                overlaps(r.checkin, r.checkout, checkin, checkout)) found.push({ kind: 'reservation', item: r });
        });
        db.holds.forEach((h) => {
            if (h.room === roomNumber && h.id !== ignore.holdId &&
                overlaps(h.start, addDays(h.end, 1), checkin, checkout)) found.push({ kind: 'hold', item: h });
        });
        db.blocks.forEach((b) => {
            if (b.rooms.includes(roomNumber) && b.id !== ignore.blockId &&
                overlaps(b.start, addDays(b.end, 1), checkin, checkout)) found.push({ kind: 'block', item: b });
        });
        return found;
    }

    function describeCommitment(c) {
        if (c.kind === 'reservation') return c.item.id + ' · ' + c.item.guest.firstName + ' ' + c.item.guest.lastName;
        if (c.kind === 'hold') return 'Hold · ' + c.item.reason;
        return 'Event · ' + c.item.name;
    }

    function findReservation(id) {
        return db.reservations.find((r) => r.id === id);
    }

    const api = {
        getRoomTypes() { return ok(ROOM_TYPES); },
        getRooms() { return ok(db.rooms); },

        getRates() { return ok({ rates: db.rates, overrides: db.rateOverrides }); },

        saveRates(input) {
            const next = {};
            for (const key of Object.keys(DEFAULT_RATES)) {
                const value = Number(input[key]);
                if (!(value > 0)) return fail('Every rate must be a positive number.');
                next[key] = Math.round(value * 100) / 100;
            }
            db.rates = next;
            saveDb();
            return ok(db.rates);
        },

        addRateOverride(input) {
            const amount = Number(input.amount);
            if (!parseISO(input.date)) return fail('Choose a date.');
            if (!(amount > 0)) return fail('Enter a positive rate.');
            db.rateOverrides = db.rateOverrides.filter((o) => o.date !== input.date);
            db.rateOverrides.push({ date: input.date, amount, label: String(input.label || '').trim() || 'Special rate' });
            db.rateOverrides.sort((a, b) => a.date.localeCompare(b.date));
            saveDb();
            return ok(db.rateOverrides);
        },

        removeRateOverride(date) {
            db.rateOverrides = db.rateOverrides.filter((o) => o.date !== date);
            saveDb();
            return ok(db.rateOverrides);
        },

        getRatePreview(start, days) {
            const out = [];
            for (let i = 0; i < days; i++) {
                const date = addDays(start, i);
                out.push({
                    date,
                    room: nightlyRate('king', date, db.rates, db.rateOverrides),
                    suite: nightlyRate('suite', date, db.rates, db.rateOverrides)
                });
            }
            return ok(out);
        },

        // Available rooms for a date range, grouped by room type with the total cost.
        searchAvailability(query) {
            const error = checkStayDates(query.checkin, query.checkout);
            if (error) return fail(error);
            const guests = Number(query.guests) || 1;
            const open = db.rooms.filter((room) => {
                const type = ROOM_TYPES[room.type];
                if (query.bed && query.bed !== 'any' && type.bed !== query.bed) return false;
                if (query.floor && query.floor !== 'any' && room.floor !== Number(query.floor)) return false;
                if (type.sleeps < guests) return false;
                return commitmentsFor(room.number, query.checkin, query.checkout).length === 0;
            });
            const groups = Object.keys(ROOM_TYPES).map((type) => {
                const rooms = open.filter((r) => r.type === type);
                if (!rooms.length) return null;
                return {
                    type,
                    rooms: rooms.map((r) => ({ number: r.number, floor: r.floor })),
                    quote: priceStay(type, query.checkin, query.checkout, db.rates, db.rateOverrides)
                };
            }).filter(Boolean);
            return ok(groups);
        },

        checkRoom(number, checkin, checkout) {
            const room = db.rooms.find((r) => r.number === Number(number));
            if (!room) return fail('We couldn’t find that room.');
            const conflicts = commitmentsFor(room.number, checkin, checkout);
            return ok({
                room,
                available: conflicts.length === 0,
                quote: priceStay(room.type, checkin, checkout, db.rates, db.rateOverrides)
            });
        },

        getReservations() { return ok(db.reservations); },

        getReservation(id) {
            const r = findReservation(id);
            return r ? ok(r) : fail('We couldn’t find reservation ' + id + '.');
        },

        // `input` never contains card details: only whether payment was taken
        // and whether the guest asked to keep a card on file.
        createReservation(input) {
            const room = db.rooms.find((r) => r.number === Number(input.room));
            if (!room) return fail('Choose a room.');
            const dateError = checkStayDates(input.checkin, input.checkout, input.source === 'staff');
            if (dateError) return fail(dateError);
            const guests = Number(input.guests) || 1;
            if (guests > ROOM_TYPES[room.type].sleeps) return fail('That room sleeps ' + ROOM_TYPES[room.type].sleeps + '.');
            if (commitmentsFor(room.number, input.checkin, input.checkout).length) {
                return fail('Room ' + room.number + ' is no longer open for those dates.');
            }
            const quote = priceStay(room.type, input.checkin, input.checkout, db.rates, db.rateOverrides);
            const now = new Date().toISOString();
            const g = input.guest;
            const reminderDate = addDays(input.checkin, -2) > todayISO() ? addDays(input.checkin, -2) : todayISO();
            const reservation = {
                id: 'DC-' + db.nextId++,
                room: room.number,
                type: room.type,
                checkin: input.checkin,
                checkout: input.checkout,
                guests,
                guest: {
                    firstName: String(g.firstName).trim(),
                    lastName: String(g.lastName).trim(),
                    email: String(g.email).trim(),
                    phone: String(g.phone).trim(),
                    requests: String(g.requests || '').trim()
                },
                nights: quote.nights,
                total: quote.total,
                paymentStatus: input.paymentStatus === 'paid' ? 'paid' : 'unpaid',
                cardOnFile: !!input.cardOnFile,
                source: input.source === 'staff' ? 'staff' : 'web',
                status: 'confirmed',
                createdAt: now,
                messages: [
                    { at: now, from: 'system', text: 'Confirmation emailed to ' + String(g.email).trim() + '.' },
                    { at: now, from: 'system', text: 'Pre-arrival note (parking, directions, check-in time) scheduled for ' + fmtDate(reminderDate) + '.' }
                ]
            };
            db.reservations.push(reservation);
            saveDb();
            return ok(reservation);
        },

        cancelReservation(id) {
            const r = findReservation(id);
            if (!r) return fail('Reservation not found.');
            r.status = 'cancelled';
            r.messages.push({ at: new Date().toISOString(), from: 'system', text: 'Reservation cancelled. Cancellation notice emailed to guest.' });
            saveDb();
            return ok(r);
        },

        setPaymentStatus(id, status) {
            const r = findReservation(id);
            if (!r) return fail('Reservation not found.');
            r.paymentStatus = status === 'paid' ? 'paid' : 'unpaid';
            saveDb();
            return ok(r);
        },

        sendGuestMessage(id, text) {
            const r = findReservation(id);
            if (!r) return fail('Reservation not found.');
            if (!String(text).trim()) return fail('Write a message first.');
            r.messages.push({ at: new Date().toISOString(), from: 'staff', text: String(text).trim() });
            saveDb();
            return ok(r);
        },

        // Status of every room for a single night.
        getRoomBoard(date) {
            const next = addDays(date, 1);
            return ok(db.rooms.map((room) => {
                const c = commitmentsFor(room.number, date, next);
                const top = c.find((x) => x.kind === 'reservation') || c.find((x) => x.kind === 'block') || c.find((x) => x.kind === 'hold');
                return Object.assign({}, room, { status: top ? top.kind : 'available', detail: top ? describeCommitment(top) : '' });
            }));
        },

        // Upcoming reservations, holds, and blocks for one room.
        getRoomSchedule(number) {
            const t = todayISO();
            const n = Number(number);
            const items = [];
            db.reservations.forEach((r) => {
                if (r.room === n && r.status === 'confirmed' && r.checkout > t) {
                    items.push({ kind: 'reservation', id: r.id, start: r.checkin, end: addDays(r.checkout, -1), label: r.guest.firstName + ' ' + r.guest.lastName });
                }
            });
            db.holds.forEach((h) => {
                if (h.room === n && h.end >= t) items.push({ kind: 'hold', id: h.id, start: h.start, end: h.end, label: h.reason });
            });
            db.blocks.forEach((b) => {
                if (b.rooms.includes(n) && b.end >= t) items.push({ kind: 'block', id: b.id, start: b.start, end: b.end, label: b.name });
            });
            items.sort((a, b) => a.start.localeCompare(b.start));
            return ok(items);
        },

        getHolds() {
            const t = todayISO();
            return ok(db.holds.filter((h) => h.end >= t).sort((a, b) => a.start.localeCompare(b.start)));
        },

        createHold(input) {
            const room = Number(input.room);
            if (!db.rooms.some((r) => r.number === room)) return fail('Choose a room.');
            if (!parseISO(input.start) || !parseISO(input.end)) return fail('Choose the first and last night.');
            if (input.end < input.start) return fail('The last night can’t be before the first.');
            if (!String(input.reason || '').trim()) return fail('Add a reason so the team knows why.');
            const conflicts = commitmentsFor(room, input.start, addDays(input.end, 1));
            if (conflicts.length) return fail('Room ' + room + ' is already taken: ' + conflicts.map(describeCommitment).join('; ') + '.');
            const hold = { id: 'H-' + db.nextHoldId++, room, start: input.start, end: input.end, reason: String(input.reason).trim(), createdAt: new Date().toISOString() };
            db.holds.push(hold);
            saveDb();
            return ok(hold);
        },

        releaseHold(id) {
            db.holds = db.holds.filter((h) => h.id !== id);
            saveDb();
            return ok(true);
        },

        getBlocks() {
            const t = todayISO();
            return ok(db.blocks.filter((b) => b.end >= t).sort((a, b) => a.start.localeCompare(b.start)));
        },

        createBlock(input) {
            if (!String(input.name || '').trim()) return fail('Name the event.');
            if (!parseISO(input.start) || !parseISO(input.end)) return fail('Choose the first and last night.');
            if (input.end < input.start) return fail('The last night can’t be before the first.');
            if (!input.rooms || !input.rooms.length) return fail('List at least one room.');
            const taken = input.rooms
                .map((n) => ({ n, c: commitmentsFor(n, input.start, addDays(input.end, 1)) }))
                .filter((x) => x.c.length);
            if (taken.length) {
                return fail('These rooms are already taken on those nights: ' +
                    taken.map((x) => x.n + ' (' + describeCommitment(x.c[0]) + ')').join(', ') + '.');
            }
            const block = { id: 'B-' + db.nextBlockId++, name: String(input.name).trim(), start: input.start, end: input.end, rooms: input.rooms.slice(), createdAt: new Date().toISOString() };
            db.blocks.push(block);
            saveDb();
            return ok(block);
        },

        removeBlock(id) {
            db.blocks = db.blocks.filter((b) => b.id !== id);
            saveDb();
            return ok(true);
        },

        resetDemoData() {
            db = seedDb();
            saveDb();
            return ok(true);
        }
    };

    /* ======================================================================
       Form helpers
       ====================================================================== */

    function setFieldError(input, message) {
        const errorId = input.id + '-error';
        let el = document.getElementById(errorId);
        if (!el) {
            el = document.createElement('p');
            el.id = errorId;
            el.className = 'field-error';
            (input.closest('.field') || input.parentNode).appendChild(el);
            const described = (input.getAttribute('aria-describedby') || '').split(' ').filter(Boolean);
            described.push(errorId);
            input.setAttribute('aria-describedby', described.join(' '));
        }
        el.textContent = message || '';
        el.hidden = !message;
        if (message) input.setAttribute('aria-invalid', 'true');
        else input.removeAttribute('aria-invalid');
    }

    // rules: [[input, () => 'message' or '']]. Returns true when all pass.
    function validate(rules) {
        let first = null;
        rules.forEach(([input, check]) => {
            const message = check(input.value.trim());
            setFieldError(input, message);
            if (message && !first) first = input;
        });
        if (first) first.focus();
        return !first;
    }

    function clearErrors(form) {
        $$('[aria-invalid]', form).forEach((input) => setFieldError(input, ''));
    }

    const required = (label) => (v) => (v ? '' : label);
    const isEmail = (v) => (/^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v) ? '' : 'Enter an email address like name@example.com.');
    const isPhone = (v) => (v.replace(/\D/g, '').length >= 10 ? '' : 'Enter a phone number with area code.');

    function luhn(digits) {
        let sum = 0;
        for (let i = 0; i < digits.length; i++) {
            let d = Number(digits[digits.length - 1 - i]);
            if (i % 2 === 1) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
        }
        return sum % 10 === 0;
    }

    function showNotice(el, message) {
        el.textContent = message || '';
        el.hidden = !message;
    }

    function setDateBounds(input, min) {
        if (min) input.min = min; else input.removeAttribute('min');
    }

    /* ======================================================================
       4. Router
       ====================================================================== */

    const TITLES = {
        home: 'Druid City Hotel · Tuscaloosa, Alabama',
        search: 'Availability · Druid City Hotel',
        booking: 'Reservation · Druid City Hotel',
        confirmation: 'Confirmed · Druid City Hotel',
        rooms: 'Rooms & Amenities · Druid City Hotel',
        staff: 'Staff Portal · Druid City Hotel'
    };

    const VIEWS = {
        home: renderHome,
        search: renderSearch,
        booking: renderBooking,
        confirmation: renderConfirmation,
        rooms: renderRooms,
        staff: renderStaff
    };

    let previousRoute = null;

    function parseHash() {
        const raw = location.hash.replace(/^#\/?/, '');
        const [path, query] = raw.split('?');
        const parts = (path || '').split('/').filter(Boolean);
        return { name: parts[0] || 'home', sub: parts[1] || '', params: new URLSearchParams(query || '') };
    }

    function go(path, params, replace) {
        const qs = params ? new URLSearchParams(params).toString() : '';
        const hash = '#/' + path + (qs ? '?' + qs : '');
        if (replace) location.replace(hash);
        else location.hash = hash;
    }

    async function router() {
        const route = parseHash();
        if (!VIEWS[route.name]) {
            history.replaceState(null, '', '#/home');
            route.name = 'home';
            route.sub = '';
        }

        const changedView = !previousRoute || previousRoute.name !== route.name;
        $$('.view').forEach((v) => { v.hidden = v.dataset.view !== route.name; });
        $$('[data-nav]').forEach((a) => {
            if (a.dataset.nav === route.name) a.setAttribute('aria-current', 'page');
            else a.removeAttribute('aria-current');
        });
        document.title = TITLES[route.name];
        closeNav();

        const focusTarget = await VIEWS[route.name](route, changedView);

        if (previousRoute) {
            const target = focusTarget || $('[data-view="' + route.name + '"] h1');
            if (changedView) window.scrollTo(0, 0);
            if (target) {
                target.focus({ preventScroll: !changedView });
                if (!changedView) target.scrollIntoView({ block: 'start', behavior: 'smooth' });
            }
        }
        previousRoute = route;
    }

    function closeNav() {
        const toggle = $('.nav-toggle');
        toggle.setAttribute('aria-expanded', 'false');
        $('#site-nav').classList.remove('is-open');
    }

    /* ======================================================================
       5. Views
       ====================================================================== */

    /* ---------- Shared bits ---------- */

    function renderAmenities() {
        const html = AMENITIES.map(([title, text]) =>
            '<li><h3>' + esc(title) + '</h3><p>' + esc(text) + '</p></li>').join('');
        $$('[data-amenities]').forEach((ul) => { ul.innerHTML = html; });
    }

    function nightBreakdown(nights) {
        const groups = [];
        nights.forEach((n) => {
            const label = n.kind === 'special' ? n.label : n.kind === 'weekend' ? 'weekend night' : 'weeknight';
            const key = n.kind + '|' + n.amount + '|' + label;
            const g = groups.find((x) => x.key === key);
            if (g) g.count++; else groups.push({ key, kind: n.kind, label, amount: n.amount, count: 1 });
        });
        return groups.map((g) => g.kind === 'special'
            ? g.count + ' × ' + money(g.amount) + ' (' + g.label + ')'
            : plural(g.count, g.label) + ' at ' + money(g.amount)).join(' · ');
    }

    function stayLine(checkin, checkout) {
        const n = nightsBetween(checkin, checkout).length;
        return fmtDate(checkin, 'day') + ' – ' + fmtDate(checkout, 'day') + ' · ' + plural(n, 'night');
    }

    function fillDateDefaults(checkinInput, checkoutInput, checkin, checkout) {
        const t = todayISO();
        setDateBounds(checkinInput, t);
        setDateBounds(checkoutInput, addDays(t, 1));
        checkinInput.value = checkin || t;
        checkoutInput.value = checkout || addDays(checkinInput.value, 1);
    }

    // Keep departure after arrival as the guest edits dates.
    function linkDateInputs(checkinInput, checkoutInput) {
        checkinInput.addEventListener('change', () => {
            if (!parseISO(checkinInput.value)) return;
            const minOut = addDays(checkinInput.value, 1);
            checkoutInput.min = minOut;
            if (!checkoutInput.value || checkoutInput.value <= checkinInput.value) checkoutInput.value = minOut;
        });
    }

    /* ---------- Home ---------- */

    function renderHome() {
        const form = $('#home-search');
        if (!form.dataset.ready) {
            fillDateDefaults(form.checkin, form.checkout, todayISO(), addDays(todayISO(), 2));
            linkDateInputs(form.checkin, form.checkout);
            form.addEventListener('submit', (e) => {
                e.preventDefault();
                const error = checkStayDates(form.checkin.value, form.checkout.value);
                setFieldError(form.checkout, '');
                setFieldError(form.checkin, '');
                if (error) {
                    const input = isArrivalError(error) ? form.checkin : form.checkout;
                    setFieldError(input, error);
                    input.focus();
                    return;
                }
                go('search', { checkin: form.checkin.value, checkout: form.checkout.value, guests: form.guests.value });
            });
            form.dataset.ready = 'true';
        }
    }

    /* ---------- Search and availability ---------- */

    async function renderSearch(route) {
        const form = $('#search-form');
        const p = route.params;

        if (!form.dataset.ready) {
            linkDateInputs(form.checkin, form.checkout);
            form.addEventListener('submit', (e) => {
                e.preventDefault();
                go('search', {
                    checkin: form.checkin.value,
                    checkout: form.checkout.value,
                    guests: form.guests.value,
                    bed: form.bed.value,
                    floor: form.floor.value
                });
            });
            $('#search-results').addEventListener('submit', (e) => {
                e.preventDefault();
                const f = e.target;
                go('booking', {
                    room: f.room.value,
                    checkin: f.dataset.checkin,
                    checkout: f.dataset.checkout,
                    guests: f.dataset.guests
                });
            });
            form.dataset.ready = 'true';
        }

        fillDateDefaults(form.checkin, form.checkout, p.get('checkin'), p.get('checkout'));
        form.guests.value = ['1', '2', '3', '4'].includes(p.get('guests')) ? p.get('guests') : '2';
        form.bed.value = ['king', 'queen', 'twin'].includes(p.get('bed')) ? p.get('bed') : 'any';
        form.floor.value = ['1', '2', '3'].includes(p.get('floor')) ? p.get('floor') : 'any';

        const status = $('#search-status');
        const results = $('#search-results');
        setFieldError(form.checkin, '');
        setFieldError(form.checkout, '');

        if (!p.get('checkin')) {
            status.textContent = 'Choose your dates to see open rooms and the full cost of your stay.';
            results.innerHTML = '';
            return;
        }

        const query = {
            checkin: form.checkin.value,
            checkout: form.checkout.value,
            guests: Number(form.guests.value),
            bed: form.bed.value,
            floor: form.floor.value
        };

        let groups;
        try {
            groups = await api.searchAvailability(query);
        } catch (err) {
            const input = isArrivalError(err.message) ? form.checkin : form.checkout;
            setFieldError(input, err.message);
            status.textContent = '';
            results.innerHTML = '';
            return;
        }

        const total = groups.reduce((s, g) => s + g.rooms.length, 0);
        if (!total) {
            status.textContent = 'Nothing open with those preferences. Try another floor or bed, or different dates.';
            results.innerHTML = '';
            return;
        }

        status.textContent = plural(total, 'room') + ' open · ' + stayLine(query.checkin, query.checkout);
        results.innerHTML = groups.map((g) => {
            const t = ROOM_TYPES[g.type];
            const floors = Array.from(new Set(g.rooms.map((r) => r.floor)));
            const options = g.rooms.map((r) =>
                '<option value="' + r.number + '">Room ' + r.number + ' · ' + esc(FLOOR_NAMES[r.floor]) + '</option>').join('');
            const fid = 'pick-' + g.type;
            return '<article class="result">' +
                '<div class="result__info">' +
                    '<h2 class="result__name">' + esc(t.name) + '</h2>' +
                    '<p class="result__meta">' + esc(t.bedLabel) + ' · Sleeps ' + t.sleeps + ' · ' + esc(t.size) + '</p>' +
                    '<p>' + esc(t.blurb) + '</p>' +
                    '<p class="result__meta">' + plural(g.rooms.length, 'room') + ' open on ' +
                        esc(floors.map((f) => FLOOR_NAMES[f].replace(' floor', '').toLowerCase()).join(', ')) + ' ' + (floors.length > 1 ? 'floors' : 'floor') + '</p>' +
                '</div>' +
                '<div class="result__price">' +
                    '<p class="result__total">' + money(g.quote.total) + '</p>' +
                    '<p class="result__meta">Total for your stay, before taxes</p>' +
                    '<p class="result__meta">' + esc(nightBreakdown(g.quote.nights)) + '</p>' +
                '</div>' +
                '<form class="result__action" data-checkin="' + esc(query.checkin) + '" data-checkout="' + esc(query.checkout) + '" data-guests="' + query.guests + '">' +
                    '<div class="field">' +
                        '<label for="' + fid + '">Room</label>' +
                        '<select id="' + fid + '" name="room">' + options + '</select>' +
                    '</div>' +
                    '<button type="submit" class="btn">Reserve</button>' +
                '</form>' +
            '</article>';
        }).join('');
    }

    /* ---------- Booking and checkout ---------- */

    let bookingContext = null;

    function setupBookingForm() {
        const form = $('#booking-form');
        const cardFields = $('#card-fields');

        const syncPayment = () => {
            const payNow = form.payWhen.value === 'now';
            cardFields.hidden = !payNow;
            $$('input', cardFields).forEach((input) => { input.disabled = !payNow; });
            if (!payNow) $$('input', cardFields).forEach((input) => setFieldError(input, ''));
            $('#booking-submit').textContent = payNow ? 'Pay & confirm' : 'Confirm reservation';
        };
        $$('input[name="payWhen"]', form).forEach((r) => r.addEventListener('change', syncPayment));
        syncPayment();

        // Group card digits in fours as the guest types.
        form.cardNumber.addEventListener('input', () => {
            const digits = form.cardNumber.value.replace(/\D/g, '').slice(0, 19);
            form.cardNumber.value = digits.replace(/(.{4})/g, '$1 ').trim();
        });
        form.cardExp.addEventListener('input', (e) => {
            const digits = form.cardExp.value.replace(/\D/g, '').slice(0, 4);
            form.cardExp.value = digits.length > 2 || (digits.length === 2 && e.inputType !== 'deleteContentBackward')
                ? digits.slice(0, 2) + '/' + digits.slice(2) : digits;
        });

        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!bookingContext) return;
            const payNow = form.payWhen.value === 'now';

            const rules = [
                [form.firstName, required('Enter a first name.')],
                [form.lastName, required('Enter a last name.')],
                [form.email, isEmail],
                [form.phone, isPhone]
            ];
            if (payNow) {
                rules.push(
                    [form.cardName, required('Enter the name on the card.')],
                    [form.cardNumber, (v) => {
                        const digits = v.replace(/\D/g, '');
                        if (digits.length < 13) return 'Enter the full card number.';
                        return luhn(digits) ? '' : 'That card number doesn’t look right.';
                    }],
                    [form.cardExp, (v) => {
                        const m = v.match(/^(\d{2})\s*\/\s*(\d{2})$/);
                        if (!m || Number(m[1]) < 1 || Number(m[1]) > 12) return 'Use MM/YY.';
                        const lastDay = new Date(2000 + Number(m[2]), Number(m[1]), 0, 23, 59, 59);
                        return lastDay < new Date() ? 'This card has expired.' : '';
                    }],
                    [form.cardCvc, (v) => (/^\d{3,4}$/.test(v) ? '' : 'Enter the 3 or 4 digit code.')],
                    [form.cardZip, (v) => (/^\d{5}$/.test(v) ? '' : 'Enter a 5 digit ZIP code.')]
                );
            }
            if (!validate(rules)) return;

            const button = $('#booking-submit');
            button.disabled = true;
            try {
                const reservation = await api.createReservation({
                    room: bookingContext.room,
                    checkin: bookingContext.checkin,
                    checkout: bookingContext.checkout,
                    guests: form.guests.value,
                    guest: {
                        firstName: form.firstName.value,
                        lastName: form.lastName.value,
                        email: form.email.value,
                        phone: form.phone.value,
                        requests: form.requests.value
                    },
                    // Card fields are validated above and then discarded.
                    paymentStatus: payNow ? 'paid' : 'unpaid',
                    cardOnFile: payNow && form.cardOnFile.checked,
                    source: 'web'
                });
                form.reset();
                syncPayment();
                go('confirmation', { id: reservation.id }, true);
            } catch (err) {
                showNotice($('#booking-unavailable'), err.message + ' ');
                $('#booking-unavailable').insertAdjacentHTML('beforeend',
                    '<a href="' + esc($('#booking-back').getAttribute('href')) + '">See what’s open</a>');
                $('#booking-unavailable').focus();
            } finally {
                button.disabled = false;
            }
        });
    }

    async function renderBooking(route) {
        const p = route.params;
        const checkin = p.get('checkin');
        const checkout = p.get('checkout');
        const roomNumber = p.get('room');
        const notice = $('#booking-unavailable');
        const layout = $('#booking-layout');
        const form = $('#booking-form');

        if (!form.dataset.ready) {
            setupBookingForm();
            notice.tabIndex = -1;
            form.dataset.ready = 'true';
        }
        clearErrors(form);

        const backParams = new URLSearchParams({ checkin: checkin || '', checkout: checkout || '', guests: p.get('guests') || '2' });
        $('#booking-back').href = '#/search?' + backParams.toString();

        let problem = checkStayDates(checkin, checkout);
        let info = null;
        if (!problem) {
            try {
                info = await api.checkRoom(roomNumber, checkin, checkout);
                if (!info.available) problem = 'Room ' + info.room.number + ' was just taken for those dates.';
            } catch (err) {
                problem = err.message;
            }
        }

        if (problem) {
            bookingContext = null;
            layout.hidden = true;
            notice.hidden = false;
            notice.innerHTML = esc(problem) + ' <a href="#/search?' + esc(backParams.toString()) + '">See what’s open</a>';
            return;
        }

        bookingContext = { room: info.room.number, checkin, checkout };
        notice.hidden = true;
        layout.hidden = false;

        const t = ROOM_TYPES[info.room.type];
        const guests = Math.min(Number(p.get('guests')) || 1, t.sleeps);
        form.guests.innerHTML = Array.from({ length: t.sleeps }, (_, i) =>
            '<option value="' + (i + 1) + '"' + (i + 1 === guests ? ' selected' : '') + '>' + plural(i + 1, 'guest') + '</option>').join('');

        $('#booking-summary').innerHTML =
            '<p class="summary__room">' + esc(t.name) + '</p>' +
            '<p class="result__meta">Room ' + info.room.number + ' · ' + esc(FLOOR_NAMES[info.room.floor]) + ' · ' + esc(t.bedLabel) + '</p>' +
            '<dl class="summary__list">' +
                '<div><dt>Arrive</dt><dd>' + esc(fmtDate(checkin)) + ', from 3 PM</dd></div>' +
                '<div><dt>Depart</dt><dd>' + esc(fmtDate(checkout)) + ', by 11 AM</dd></div>' +
                '<div><dt>Nights</dt><dd>' + esc(nightBreakdown(info.quote.nights)) + '</dd></div>' +
            '</dl>' +
            '<p class="summary__total"><span>Total</span> <strong>' + money(info.quote.total) + '</strong></p>' +
            '<p class="fine-print">Before applicable taxes.</p>';
    }

    /* ---------- Confirmation ---------- */

    async function renderConfirmation(route) {
        const body = $('#confirmation-body');
        let r;
        try {
            r = await api.getReservation(route.params.get('id'));
        } catch (err) {
            body.innerHTML = '<p class="notice">' + esc(err.message) + ' <a href="#/home">Return home</a></p>';
            return;
        }
        const t = ROOM_TYPES[r.type];
        body.innerHTML =
            '<div class="confirmation">' +
                '<div>' +
                    '<p class="lede">Thank you, ' + esc(r.guest.firstName) + '. Your room is held and waiting.</p>' +
                    '<p class="confirmation__code"><span class="label-heading">Confirmation</span> ' + esc(r.id) + '</p>' +
                    '<dl class="summary__list">' +
                        '<div><dt>Room</dt><dd>' + esc(t.name) + ', room ' + r.room + '</dd></div>' +
                        '<div><dt>Arrive</dt><dd>' + esc(fmtDate(r.checkin)) + ', from 3 PM</dd></div>' +
                        '<div><dt>Depart</dt><dd>' + esc(fmtDate(r.checkout)) + ', by 11 AM</dd></div>' +
                        '<div><dt>Guests</dt><dd>' + r.guests + '</dd></div>' +
                        '<div><dt>Total</dt><dd>' + money(r.total) + '</dd></div>' +
                        '<div><dt>Payment</dt><dd>' + (r.paymentStatus === 'paid' ? 'Paid in full' : 'Due at check-in') +
                            (r.cardOnFile ? ' · Card kept on file' : '') + '</dd></div>' +
                    '</dl>' +
                '</div>' +
                '<div>' +
                    '<h2 class="label-heading">What happens next</h2>' +
                    '<ol class="timeline">' +
                        '<li><strong>Now</strong> A confirmation is on its way to ' + esc(r.guest.email) + '.</li>' +
                        '<li><strong>Two days out</strong> We’ll send parking, directions, and a few supper suggestions.</li>' +
                        '<li><strong>Arrival</strong> Your room is ready from 3 PM. Running late? We’ll wait up.</li>' +
                    '</ol>' +
                    '<p class="fine-print">Prototype: emails are simulated and recorded in the Staff Portal.</p>' +
                    '<div class="form-actions"><a class="btn btn--ghost" href="#/home">Back to the hotel</a></div>' +
                '</div>' +
            '</div>';
    }

    /* ---------- Property and room information ---------- */

    async function renderRooms() {
        const { rates } = await api.getRates();
        const counts = {};
        (await api.getRooms()).forEach((r) => { counts[r.type] = (counts[r.type] || 0) + 1; });
        $('[data-room-types]').innerHTML = Object.keys(ROOM_TYPES).map((key) => {
            const t = ROOM_TYPES[key];
            const from = key === 'suite' ? Math.min(rates.suiteWeekday, rates.suiteWeekend) : Math.min(rates.weekday, rates.weekend);
            return '<article class="room-type">' +
                '<h2 class="room-type__name">' + esc(t.name) + '</h2>' +
                '<p class="room-type__blurb">' + esc(t.blurb) + '</p>' +
                '<dl class="room-type__facts">' +
                    '<div><dt>Bed</dt><dd>' + esc(t.bedLabel) + '</dd></div>' +
                    '<div><dt>Sleeps</dt><dd>' + t.sleeps + '</dd></div>' +
                    '<div><dt>Size</dt><dd>' + esc(t.size) + '</dd></div>' +
                    '<div><dt>From</dt><dd>' + money(from) + ' / night</dd></div>' +
                '</dl>' +
                '<p class="fine-print">' + plural(counts[key], 'room') + ' of this kind</p>' +
            '</article>';
        }).join('') + '<p><a class="btn" href="#/search">Check availability</a></p>';
        $('[data-rate-fact]').textContent = money(rates.weekday) + ' weeknights, ' + money(rates.weekend) + ' Friday and Saturday nights';
    }

    /* ---------- Staff portal ---------- */

    const STAFF_PANELS = ['reservations', 'new', 'rooms', 'events', 'rates'];
    const STATUS_LABELS = { available: 'Open', reservation: 'Booked', hold: 'Hold', block: 'Event' };
    let selectedRoom = null;

    async function renderStaff(route, changedView) {
        const panel = STAFF_PANELS.includes(route.sub) ? route.sub : 'reservations';
        if (!$('.view--staff').dataset.ready) {
            setupStaff();
            $('.view--staff').dataset.ready = 'true';
        }
        $$('[data-panel]').forEach((el) => { el.hidden = el.dataset.panel !== panel; });
        $$('[data-staff-tab]').forEach((a) => {
            if (a.dataset.staffTab === panel) a.setAttribute('aria-current', 'page');
            else a.removeAttribute('aria-current');
        });

        if (panel === 'reservations') await renderReservationsPanel(route.params.get('id'));
        if (panel === 'new') await renderNewPanel();
        if (panel === 'rooms') await renderRoomsPanel();
        if (panel === 'events') await renderEventsPanel();
        if (panel === 'rates') await renderRatesPanel();

        if (changedView) return null;
        if (panel === 'reservations' && route.params.get('id')) return $('#res-detail h3');
        return $('[data-panel="' + panel + '"] .panel__title');
    }

    function setupStaff() {
        $('[data-action="reset-demo"]').addEventListener('click', async () => {
            if (!window.confirm('Reset all reservations, holds, blocks, and rates to the sample data?')) return;
            await api.resetDemoData();
            selectedRoom = null;
            router();
        });

        // Reservations
        $('#res-filters').addEventListener('input', () => renderReservationTable());
        $('#res-filters').addEventListener('submit', (e) => e.preventDefault());
        $('#res-detail').addEventListener('click', async (e) => {
            const button = e.target.closest('[data-action]');
            if (!button) return;
            const id = button.dataset.id;
            if (button.dataset.action === 'cancel') {
                if (!window.confirm('Cancel reservation ' + id + '? The room will be released.')) return;
                await api.cancelReservation(id);
            }
            if (button.dataset.action === 'toggle-paid') {
                await api.setPaymentStatus(id, button.dataset.next);
            }
            await renderReservationsPanel(id);
            const again = $('#res-detail [data-action="' + button.dataset.action + '"]');
            (again || $('#res-detail h3')).focus();
        });
        $('#res-detail').addEventListener('submit', async (e) => {
            e.preventDefault();
            const form = e.target;
            try {
                await api.sendGuestMessage(form.dataset.id, form.message.value);
                await renderReservationsPanel(form.dataset.id);
                $('#res-detail textarea').focus();
            } catch (err) {
                setFieldError(form.message, err.message);
                form.message.focus();
            }
        });

        // New reservation
        const nf = $('#staff-new-form');
        linkDateInputs(nf.checkin, nf.checkout);
        ['checkin', 'checkout', 'guests'].forEach((name) => nf[name].addEventListener('change', updateStaffRoomOptions));
        nf.room.addEventListener('change', updateStaffQuote);
        nf.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#sn-message');
            showNotice(message, '');
            const ok = validate([
                [nf.checkin, (v) => (parseISO(v) ? '' : 'Choose an arrival date.')],
                [nf.checkout, (v) => (v > nf.checkin.value ? '' : 'Departure must be after arrival.')],
                [nf.room, required('No open rooms for these dates.')],
                [nf.firstName, required('Enter a first name.')],
                [nf.lastName, required('Enter a last name.')],
                [nf.email, isEmail],
                [nf.phone, isPhone]
            ]);
            if (!ok) return;
            try {
                const r = await api.createReservation({
                    room: nf.room.value,
                    checkin: nf.checkin.value,
                    checkout: nf.checkout.value,
                    guests: nf.guests.value,
                    guest: { firstName: nf.firstName.value, lastName: nf.lastName.value, email: nf.email.value, phone: nf.phone.value, requests: nf.requests.value },
                    paymentStatus: nf.paymentStatus.value,
                    cardOnFile: false,
                    source: 'staff'
                });
                nf.reset();
                go('staff/reservations', { id: r.id });
            } catch (err) {
                showNotice(message, err.message);
            }
        });

        // Rooms and holds
        const roomDate = $('#room-date');
        roomDate.value = todayISO();
        roomDate.addEventListener('change', () => { if (parseISO(roomDate.value)) renderRoomsPanel(); });
        $('#room-grid').addEventListener('click', (e) => {
            const cell = e.target.closest('[data-room]');
            if (!cell) return;
            selectedRoom = Number(cell.dataset.room);
            renderRoomsPanel().then(() => {
                const heading = $('#room-panel h3');
                if (heading) {
                    heading.focus({ preventScroll: true });
                    if (window.matchMedia('(max-width: 63.99rem)').matches) heading.scrollIntoView({ behavior: 'smooth', block: 'start' });
                }
            });
        });
        $('#room-panel').addEventListener('submit', async (e) => {
            e.preventDefault();
            const f = e.target;
            const message = $('.notice', f);
            try {
                await api.createHold({ room: selectedRoom, start: f.start.value, end: f.end.value, reason: f.reason.value });
                await renderRoomsPanel();
                $('#room-summary').textContent = 'Hold placed on room ' + selectedRoom + '.';
                $('#room-panel h3').focus();
            } catch (err) {
                showNotice(message, err.message);
            }
        });
        const releaseHandler = async (e) => {
            const button = e.target.closest('[data-action="release-hold"]');
            if (!button) return;
            await api.releaseHold(button.dataset.id);
            await renderRoomsPanel();
            $('#room-summary').textContent = 'Hold released.';
            ($('#room-panel h3') || $('[data-panel="rooms"] .panel__title')).focus();
        };
        $('#room-panel').addEventListener('click', releaseHandler);
        $('#holds-list').addEventListener('click', releaseHandler);

        // Event blocks
        const bf = $('#block-form');
        linkDateInputs(bf.start, bf.end);
        $$('[data-fill-floor]', bf).forEach((b) => b.addEventListener('click', () => {
            const f = Number(b.dataset.fillFloor);
            bf.rooms.value = (f * 100 + 1) + '-' + (f * 100 + ROOMS_PER_FLOOR);
            bf.rooms.focus();
        }));
        bf.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#bl-message');
            showNotice(message, '');
            const validNumbers = new Set((await api.getRooms()).map((r) => r.number));
            const parsed = parseRoomList(bf.rooms.value, validNumbers);
            const ok = validate([
                [bf.name, required('Name the event.')],
                [bf.start, (v) => (parseISO(v) ? '' : 'Choose the first night.')],
                [bf.end, (v) => (!parseISO(v) ? 'Choose the last night.' : v < bf.start.value ? 'Last night can’t be before the first.' : '')],
                [bf.rooms, () => (parsed.invalid.length ? 'Not rooms we have: ' + parsed.invalid.join(', ') + '.' : parsed.rooms.length ? '' : 'List at least one room.')]
            ]);
            if (!ok) return;
            try {
                const block = await api.createBlock({ name: bf.name.value, start: bf.start.value, end: bf.end.value, rooms: parsed.rooms });
                bf.reset();
                await renderEventsPanel();
                showNotice(message, 'Blocked ' + plural(block.rooms.length, 'room') + ' for ' + block.name + '.');
            } catch (err) {
                showNotice(message, err.message);
            }
        });
        $('#blocks-list').addEventListener('click', async (e) => {
            const button = e.target.closest('[data-action="remove-block"]');
            if (!button) return;
            if (!window.confirm('Release every room in this block?')) return;
            await api.removeBlock(button.dataset.id);
            await renderEventsPanel();
            $('[data-panel="events"] .panel__title').focus();
        });

        // Rates
        const rf = $('#rates-form');
        rf.addEventListener('submit', async (e) => {
            e.preventDefault();
            const positive = (v) => (Number(v) > 0 ? '' : 'Enter a positive amount.');
            if (!validate([[rf.weekday, positive], [rf.weekend, positive], [rf.suiteWeekday, positive], [rf.suiteWeekend, positive]])) return;
            await api.saveRates({ weekday: rf.weekday.value, weekend: rf.weekend.value, suiteWeekday: rf.suiteWeekday.value, suiteWeekend: rf.suiteWeekend.value });
            await renderRatesPanel();
            $('#rates-message').textContent = 'Saved. New searches use these rates.';
        });
        const of = $('#override-form');
        of.addEventListener('submit', async (e) => {
            e.preventDefault();
            const ok = validate([
                [of.date, (v) => (parseISO(v) ? '' : 'Choose a date.')],
                [of.amount, (v) => (Number(v) > 0 ? '' : 'Enter a positive amount.')],
                [of.label, required('Add a short reason.')]
            ]);
            if (!ok) return;
            await api.addRateOverride({ date: of.date.value, amount: of.amount.value, label: of.label.value });
            of.reset();
            await renderRatesPanel();
            $('#rates-message').textContent = 'Special rate added.';
        });
        $('#overrides-list').addEventListener('click', async (e) => {
            const button = e.target.closest('[data-action="remove-override"]');
            if (!button) return;
            await api.removeRateOverride(button.dataset.date);
            await renderRatesPanel();
            $('#rates-message').textContent = 'Special rate removed.';
            of.date.focus();
        });
    }

    /* Reservations panel */

    function paymentBadge(r) {
        return r.paymentStatus === 'paid'
            ? '<span class="badge badge--paid">Paid</span>'
            : '<span class="badge badge--unpaid">Not paid</span>';
    }

    function statusBadge(r) {
        const t = todayISO();
        if (r.status === 'cancelled') return '<span class="badge badge--muted">Cancelled</span>';
        if (r.checkout <= t) return '<span class="badge badge--muted">Checked out</span>';
        if (r.checkin <= t) return '<span class="badge">In house</span>';
        return '<span class="badge">Confirmed</span>';
    }

    async function renderReservationsPanel(id) {
        const t = todayISO();
        const all = await api.getReservations();
        const board = await api.getRoomBoard(t);
        const active = all.filter((r) => r.status === 'confirmed');
        const arrivals = active.filter((r) => r.checkin === t).length;
        const inHouse = active.filter((r) => r.checkin <= t && r.checkout > t).length;
        const open = board.filter((r) => r.status === 'available').length;
        const outstanding = active.filter((r) => r.paymentStatus === 'unpaid' && r.checkout > t).reduce((s, r) => s + r.total, 0);
        $('#staff-stats').innerHTML =
            '<div><dt>Arrivals today</dt><dd>' + arrivals + '</dd></div>' +
            '<div><dt>In house tonight</dt><dd>' + inHouse + '</dd></div>' +
            '<div><dt>Open tonight</dt><dd>' + open + ' of ' + board.length + '</dd></div>' +
            '<div><dt>Not yet paid</dt><dd>' + money(outstanding) + '</dd></div>';

        await renderReservationDetail(id);
        await renderReservationTable();
    }

    async function renderReservationDetail(id) {
        const host = $('#res-detail');
        if (!id) { host.innerHTML = ''; return; }
        let r;
        try {
            r = await api.getReservation(id);
        } catch (err) {
            host.innerHTML = '<p class="notice">' + esc(err.message) + '</p>';
            return;
        }
        const t = ROOM_TYPES[r.type];
        const g = r.guest;
        const canCancel = r.status === 'confirmed' && r.checkout > todayISO();
        const messages = r.messages.slice().reverse().map((m) =>
            '<li><p class="message__meta">' + esc(fmtTimestamp(m.at)) + ' · ' + (m.from === 'staff' ? 'Front desk' : 'Automatic') + '</p>' +
            '<p>' + esc(m.text) + '</p></li>').join('');

        host.innerHTML =
            '<article class="detail" aria-labelledby="detail-title">' +
                '<div class="detail__head">' +
                    '<h3 id="detail-title" class="detail__title" tabindex="-1">' + esc(g.firstName + ' ' + g.lastName) +
                        ' <span class="detail__id">' + esc(r.id) + '</span></h3>' +
                    '<a class="btn-text" href="#/staff/reservations">Close</a>' +
                '</div>' +
                '<div class="detail__cols">' +
                    '<dl class="summary__list">' +
                        '<div><dt>Email</dt><dd><a href="mailto:' + esc(g.email) + '">' + esc(g.email) + '</a></dd></div>' +
                        '<div><dt>Phone</dt><dd><a href="tel:' + esc(g.phone.replace(/[^\d+]/g, '')) + '">' + esc(g.phone) + '</a></dd></div>' +
                        '<div><dt>Guests</dt><dd>' + r.guests + '</dd></div>' +
                        '<div><dt>Requests</dt><dd>' + (g.requests ? esc(g.requests) : '—') + '</dd></div>' +
                        '<div><dt>Booked</dt><dd>' + esc(fmtTimestamp(r.createdAt)) + (r.source === 'staff' ? ' by front desk' : ' online') + '</dd></div>' +
                    '</dl>' +
                    '<dl class="summary__list">' +
                        '<div><dt>Room</dt><dd>' + r.room + ' · ' + esc(t.name) + '</dd></div>' +
                        '<div><dt>Stay</dt><dd>' + esc(stayLine(r.checkin, r.checkout)) + '</dd></div>' +
                        '<div><dt>Rates</dt><dd>' + esc(nightBreakdown(r.nights)) + '</dd></div>' +
                        '<div><dt>Total</dt><dd>' + money(r.total) + '</dd></div>' +
                        '<div><dt>Payment</dt><dd>' + paymentBadge(r) + (r.cardOnFile ? ' Card on file' : ' No card on file') + '</dd></div>' +
                        '<div><dt>Status</dt><dd>' + statusBadge(r) + '</dd></div>' +
                    '</dl>' +
                '</div>' +
                '<div class="inline-actions">' +
                    '<button type="button" class="btn btn--ghost" data-action="toggle-paid" data-id="' + esc(r.id) + '" data-next="' + (r.paymentStatus === 'paid' ? 'unpaid' : 'paid') + '">' +
                        (r.paymentStatus === 'paid' ? 'Mark as not paid' : 'Mark as paid') + '</button>' +
                    (canCancel ? '<button type="button" class="btn btn--danger" data-action="cancel" data-id="' + esc(r.id) + '">Cancel reservation</button>' : '') +
                '</div>' +
                '<h4 class="label-heading section-gap">Guest messages</h4>' +
                '<form class="message-form" data-id="' + esc(r.id) + '" novalidate>' +
                    '<div class="field">' +
                        '<label for="msg-' + esc(r.id) + '">Send a note to ' + esc(g.firstName) + '</label>' +
                        '<textarea id="msg-' + esc(r.id) + '" name="message" rows="2"></textarea>' +
                    '</div>' +
                    '<button type="submit" class="btn btn--ghost">Send</button>' +
                '</form>' +
                '<p class="fine-print">Prototype: messages are logged here, not emailed.</p>' +
                '<ol class="messages">' + messages + '</ol>' +
            '</article>';
    }

    async function renderReservationTable() {
        const f = $('#res-filters');
        const q = f.q.value.trim().toLowerCase();
        const t = todayISO();
        const list = (await api.getReservations()).filter((r) => {
            if (f.status.value === 'upcoming' && !(r.status === 'confirmed' && r.checkout > t)) return false;
            if (f.status.value === 'past' && !(r.status === 'confirmed' && r.checkout <= t)) return false;
            if (f.status.value === 'cancelled' && r.status !== 'cancelled') return false;
            if (f.payment.value !== 'any' && r.paymentStatus !== f.payment.value) return false;
            if (q) {
                const hay = [r.id, r.room, r.guest.firstName, r.guest.lastName, r.guest.email].join(' ').toLowerCase();
                if (!hay.includes(q)) return false;
            }
            return true;
        }).sort((a, b) => a.checkin.localeCompare(b.checkin) || a.room - b.room);

        $('#res-count').textContent = plural(list.length, 'reservation');
        if (!list.length) {
            $('#res-table').innerHTML = '<p class="empty">No reservations match.</p>';
            return;
        }
        $('#res-table').innerHTML =
            '<table class="data-table">' +
                '<caption class="visually-hidden">Reservations</caption>' +
                '<thead><tr><th scope="col">Confirmation</th><th scope="col">Guest</th><th scope="col">Room</th>' +
                '<th scope="col">Stay</th><th scope="col">Total</th><th scope="col">Payment</th><th scope="col">Status</th></tr></thead>' +
                '<tbody>' + list.map((r) =>
                    '<tr>' +
                        '<td data-label="Confirmation"><a href="#/staff/reservations?id=' + esc(r.id) + '">' + esc(r.id) + '</a></td>' +
                        '<td data-label="Guest">' + esc(r.guest.firstName + ' ' + r.guest.lastName) + '</td>' +
                        '<td data-label="Room">' + r.room + '</td>' +
                        '<td data-label="Stay">' + esc(fmtDate(r.checkin, 'short') + ' – ' + fmtDate(r.checkout, 'short')) + '</td>' +
                        '<td data-label="Total">' + money(r.total) + '</td>' +
                        '<td data-label="Payment">' + paymentBadge(r) + '</td>' +
                        '<td data-label="Status">' + statusBadge(r) + '</td>' +
                    '</tr>').join('') +
                '</tbody>' +
            '</table>';
    }

    /* New reservation panel */

    async function renderNewPanel() {
        const nf = $('#staff-new-form');
        if (!parseISO(nf.checkin.value)) {
            nf.checkin.value = todayISO();
            nf.checkout.value = addDays(todayISO(), 1);
            nf.checkout.min = addDays(todayISO(), 1);
        }
        showNotice($('#sn-message'), '');
        await updateStaffRoomOptions();
    }

    async function updateStaffRoomOptions() {
        const nf = $('#staff-new-form');
        const select = nf.room;
        const previous = select.value;
        if (nf.checkout.value <= nf.checkin.value || !parseISO(nf.checkin.value)) {
            select.innerHTML = '';
            $('#sn-quote').textContent = 'Choose valid dates to see open rooms.';
            return;
        }
        const guests = Number(nf.guests.value);
        const board = await api.getRooms();
        const open = [];
        for (const room of board) {
            if (ROOM_TYPES[room.type].sleeps < guests) continue;
            const check = await api.checkRoom(room.number, nf.checkin.value, nf.checkout.value);
            if (check.available) open.push(room);
        }
        select.innerHTML = [1, 2, 3].map((floor) => {
            const rooms = open.filter((r) => r.floor === floor);
            if (!rooms.length) return '';
            return '<optgroup label="' + FLOOR_NAMES[floor] + '">' + rooms.map((r) =>
                '<option value="' + r.number + '">' + r.number + ' · ' + esc(ROOM_TYPES[r.type].name.replace('The ', '')) + '</option>').join('') + '</optgroup>';
        }).join('');
        if (open.some((r) => String(r.number) === previous)) select.value = previous;
        await updateStaffQuote();
    }

    async function updateStaffQuote() {
        const nf = $('#staff-new-form');
        if (!nf.room.value) {
            $('#sn-quote').textContent = 'No rooms open for these dates and party size.';
            return;
        }
        const info = await api.checkRoom(nf.room.value, nf.checkin.value, nf.checkout.value);
        $('#sn-quote').textContent = 'Room ' + info.room.number + ': ' + money(info.quote.total) + ' total · ' + nightBreakdown(info.quote.nights);
    }

    /* Rooms and holds panel */

    async function renderRoomsPanel() {
        const date = $('#room-date').value || todayISO();
        const board = await api.getRoomBoard(date);
        const counts = { available: 0, reservation: 0, hold: 0, block: 0 };
        board.forEach((r) => { counts[r.status]++; });
        $('#room-summary').textContent = fmtDate(date) + ': ' + counts.available + ' open, ' + counts.reservation + ' booked, ' +
            counts.hold + ' on hold, ' + counts.block + ' blocked for events.';

        $('#room-grid').innerHTML = [1, 2, 3].map((floor) =>
            '<h3 class="label-heading">' + FLOOR_NAMES[floor] + '</h3>' +
            '<ul class="room-grid">' + board.filter((r) => r.floor === floor).map((r) => {
                const label = 'Room ' + r.number + ', ' + ROOM_TYPES[r.type].name.replace('The ', '') + ', ' + STATUS_LABELS[r.status] + (r.detail ? ': ' + r.detail : '');
                return '<li><button type="button" class="room-cell room-cell--' + r.status + '" data-room="' + r.number + '"' +
                    ' aria-pressed="' + (selectedRoom === r.number) + '" aria-label="' + esc(label) + '" title="' + esc(label) + '">' +
                    '<span class="room-cell__no">' + r.number + '</span>' +
                    '<span class="room-cell__status" aria-hidden="true">' + (r.type === 'suite' ? 'Suite · ' : '') + STATUS_LABELS[r.status] + '</span>' +
                '</button></li>';
            }).join('') + '</ul>'
        ).join('');

        await renderRoomPanel(date);

        const holds = await api.getHolds();
        $('#holds-list').innerHTML = holds.length
            ? '<ul class="line-list">' + holds.map((h) =>
                '<li><div><strong>Room ' + h.room + '</strong> · ' + esc(fmtDate(h.start, 'short')) + (h.end !== h.start ? ' – ' + esc(fmtDate(h.end, 'short')) : '') +
                '<p class="fine-print">' + esc(h.reason) + '</p></div>' +
                '<button type="button" class="btn-text" data-action="release-hold" data-id="' + esc(h.id) + '">Release<span class="visually-hidden"> hold on room ' + h.room + '</span></button></li>').join('') + '</ul>'
            : '<p class="empty">No rooms on hold.</p>';
    }

    async function renderRoomPanel(date) {
        const host = $('#room-panel');
        if (!selectedRoom) {
            host.innerHTML = '<p class="empty">Select a room to see its schedule or place a hold.</p>';
            return;
        }
        const room = (await api.getRooms()).find((r) => r.number === selectedRoom);
        const t = ROOM_TYPES[room.type];
        const schedule = await api.getRoomSchedule(room.number);
        const kindLabel = { reservation: 'Booked', hold: 'Hold', block: 'Event' };
        host.innerHTML =
            '<h3 class="detail__title" tabindex="-1">Room ' + room.number + '</h3>' +
            '<p class="result__meta">' + esc(t.name) + ' · ' + esc(t.bedLabel) + ' · ' + esc(FLOOR_NAMES[room.floor]) + '</p>' +
            '<h4 class="label-heading section-gap">Coming up</h4>' +
            (schedule.length
                ? '<ul class="line-list">' + schedule.map((s) =>
                    '<li><div><span class="badge">' + kindLabel[s.kind] + '</span> ' +
                    esc(fmtDate(s.start, 'short')) + (s.end !== s.start ? ' – ' + esc(fmtDate(s.end, 'short')) : '') + ' (nights)' +
                    '<p class="fine-print">' + (s.kind === 'reservation' ? '<a href="#/staff/reservations?id=' + esc(s.id) + '">' + esc(s.id) + '</a> · ' : '') + esc(s.label) + '</p></div>' +
                    (s.kind === 'hold' ? '<button type="button" class="btn-text" data-action="release-hold" data-id="' + esc(s.id) + '">Release</button>' : '') +
                    '</li>').join('') + '</ul>'
                : '<p class="empty">Nothing scheduled.</p>') +
            '<form class="hold-form section-gap" novalidate>' +
                '<h4 class="label-heading">Place a hold</h4>' +
                '<div class="grid-2">' +
                    '<div class="field"><label for="hold-start">First night</label><input type="date" id="hold-start" name="start" value="' + esc(date) + '" required></div>' +
                    '<div class="field"><label for="hold-end">Last night</label><input type="date" id="hold-end" name="end" value="' + esc(date) + '" required></div>' +
                '</div>' +
                '<div class="field"><label for="hold-reason">Reason</label><input type="text" id="hold-reason" name="reason" placeholder="Maintenance, VIP request, deep clean…" required></div>' +
                '<button type="submit" class="btn">Hold room ' + room.number + '</button>' +
                '<p class="notice" role="alert" hidden></p>' +
            '</form>';
    }

    /* Event blocks panel */

    async function renderEventsPanel() {
        const bf = $('#block-form');
        if (!bf.start.value) {
            const t = todayISO();
            bf.start.min = t;
            bf.end.min = t;
        }
        const blocks = await api.getBlocks();
        $('#blocks-list').innerHTML = blocks.length
            ? '<ul class="line-list">' + blocks.map((b) =>
                '<li><div><strong>' + esc(b.name) + '</strong>' +
                '<p class="fine-print">' + esc(fmtDate(b.start, 'day')) + (b.end !== b.start ? ' – ' + esc(fmtDate(b.end, 'day')) : '') +
                ' · ' + plural(b.rooms.length, 'room') + ': ' + esc(compressRooms(b.rooms)) + '</p></div>' +
                '<button type="button" class="btn-text" data-action="remove-block" data-id="' + esc(b.id) + '">Remove<span class="visually-hidden"> ' + esc(b.name) + '</span></button></li>').join('') + '</ul>'
            : '<p class="empty">No event blocks scheduled.</p>';
    }

    /* Rates panel */

    async function renderRatesPanel() {
        const { rates, overrides } = await api.getRates();
        const rf = $('#rates-form');
        Object.keys(rates).forEach((key) => { rf[key].value = rates[key]; });
        $('#rates-message').textContent = '';

        const t = todayISO();
        const upcoming = overrides.filter((o) => o.date >= t);
        $('#overrides-list').innerHTML = upcoming.length
            ? '<ul class="line-list">' + upcoming.map((o) =>
                '<li><div><strong>' + esc(fmtDate(o.date)) + '</strong> · ' + money(o.amount) + '<p class="fine-print">' + esc(o.label) + '</p></div>' +
                '<button type="button" class="btn-text" data-action="remove-override" data-date="' + esc(o.date) + '">Remove<span class="visually-hidden"> special rate for ' + esc(fmtDate(o.date)) + '</span></button></li>').join('') + '</ul>'
            : '<p class="empty">No special date rates.</p>';

        const preview = await api.getRatePreview(t, 14);
        const kindLabel = (n) => (n.kind === 'special' ? n.label : n.kind === 'weekend' ? 'Weekend' : 'Weeknight');
        $('#rate-preview').innerHTML =
            '<table class="data-table">' +
                '<caption class="visually-hidden">Nightly rates for the next 14 nights</caption>' +
                '<thead><tr><th scope="col">Night</th><th scope="col">Rate type</th><th scope="col">Rooms</th><th scope="col">Master suites</th></tr></thead>' +
                '<tbody>' + preview.map((p) =>
                    '<tr><td data-label="Night">' + esc(fmtDate(p.date, 'day')) + '</td>' +
                    '<td data-label="Rate type">' + esc(kindLabel(p.room)) + '</td>' +
                    '<td data-label="Rooms">' + money(p.room.amount) + '</td>' +
                    '<td data-label="Master suites">' + money(p.suite.amount) + '</td></tr>').join('') +
                '</tbody>' +
            '</table>';
    }

    /* ======================================================================
       6. Startup
       ====================================================================== */

    function init() {
        renderAmenities();

        const toggle = $('.nav-toggle');
        toggle.addEventListener('click', () => {
            const open = toggle.getAttribute('aria-expanded') !== 'true';
            toggle.setAttribute('aria-expanded', String(open));
            $('#site-nav').classList.toggle('is-open', open);
        });

        // The skip link must not change the hash, or the router would treat it as a route.
        $('[data-skip-link]').addEventListener('click', (e) => {
            e.preventDefault();
            const h1 = $('.view:not([hidden]) h1');
            (h1 || $('#main')).focus();
        });

        // Clear a field's error as soon as the guest starts correcting it.
        document.addEventListener('input', (e) => {
            if (e.target.getAttribute && e.target.getAttribute('aria-invalid') === 'true') setFieldError(e.target, '');
        });

        window.addEventListener('hashchange', router);
        router();
    }

    init();
})();
