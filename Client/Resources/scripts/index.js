/* ==========================================================================
   Druid City Hotel — single-page client

   Layout of this file:
     1. Utilities (dates, money, escaping)
     2. Hotel configuration (room types, amenities)
     3. `api`: fetch() calls to the ASP.NET Core backend in /API
     4. Router (hash-based: #/home, #/search, #/booking, #/confirmation, #/rooms, #/account, #/pay, #/staff)
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

    // "07:00" -> "7:00 AM", "15:30" -> "3:30 PM"
    function fmtTime(hhmm) {
        const [h, m] = String(hhmm || '').split(':').map(Number);
        return ((h % 12) || 12) + ':' + pad(m) + ' ' + (h < 12 ? 'AM' : 'PM');
    }

    // The Monday on or before a date: schedule weeks run Monday to Sunday.
    const weekStart = (iso) => addDays(iso, -((parseISO(iso).getDay() + 6) % 7));

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

    const FLOOR_NAMES = { 1: 'First floor', 2: 'Second floor', 3: 'Third floor' };

    // Loaded from the API at startup (see init). Keyed by type: king, queen, twin, suite.
    let ROOM_TYPES = {};

    // Stand-in photography (README assumption 13). The Twin borrows the two-bed queen photo.
    const ROOM_PHOTOS = {
        king: ['room-king.avif', 'A king bed under a bold orange mural, with a writing desk and a mustard chair'],
        queen: ['room-queen.avif', 'Two queen beds facing a tall window onto the trees'],
        twin: ['room-queen.avif', 'Two made beds facing a tall window onto the trees'],
        suite: ['room-suite.avif', 'A king bed beyond a velvet sofa and armchair in an open parlor']
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
       3. API
       ----------------------------------------------------------------------
       Everything the views need goes through `api`, which calls the
       ASP.NET Core backend in the API folder. Every method returns a
       Promise that rejects with an Error whose message can be shown to
       the guest or staff member as-is.
       ====================================================================== */

    // The API serves this page, so API calls go to the same address the site is on.
    const API_BASE = '/api';

    async function request(path, options) {
        options = options || {};
        let res;
        try {
            res = await fetch(API_BASE + path, {
                method: options.method || 'GET',
                headers: options.body === undefined ? {} : { 'Content-Type': 'application/json' },
                body: options.body === undefined ? undefined : JSON.stringify(options.body)
            });
        } catch (e) {
            throw new Error('We can’t reach the reservation system right now. Please try again in a moment.');
        }
        if (res.status === 204) return null;
        const data = await res.json().catch(() => null);
        if (!res.ok) {
            const err = new Error((data && (data.message || data.title)) || 'Something went wrong. Please try again.');
            err.status = res.status;
            err.data = data;
            // 401: a session ended. signIn: 'guest' marks the guest account session;
            // anything else is the staff session (signed out, expired, or deactivated).
            if (res.status === 401) {
                if (data && data.signIn === 'guest') onGuestSignedOut();
                else onStaffSignedOut();
            }
            throw err;
        }
        return data;
    }

    const toQuery = (params) => '?' + new URLSearchParams(params).toString();
    const seg = (value) => encodeURIComponent(value);

    const CONCIERGE_OFFLINE = 'Sorry, I can’t connect right now. Please try again in a moment, or call the front desk at ###-###-####.';

    const api = {
        getRoomTypes() { return request('/rooms/types'); },
        getRooms() { return request('/rooms'); },

        getRates() { return request('/rates'); },

        // Default rates per room type: { king: { weekday, weekend }, queen: ..., twin: ..., suite: ... }.
        saveRates(rates) {
            const body = {};
            Object.keys(rates).forEach((type) => {
                body[type] = { weekday: Number(rates[type].weekday), weekend: Number(rates[type].weekend) };
            });
            return request('/rates', { method: 'PUT', body: { rates: body } });
        },

        addRateOverride(input) {
            return request('/rates/overrides', {
                method: 'POST',
                body: { date: input.date, amount: Number(input.amount), label: input.label }
            });
        },

        removeRateOverride(date) { return request('/rates/overrides/' + seg(date), { method: 'DELETE' }); },

        getRatePreview(start, days) { return request('/rates/preview' + toQuery({ start, days })); },

        // Available rooms for a date range, grouped by room type with the total cost.
        // `allowPast` lets staff book stays that have already started.
        searchAvailability(q) {
            return request('/rooms/availability' + toQuery({
                checkin: q.checkin || '',
                checkout: q.checkout || '',
                guests: Number(q.guests) || 1,
                bed: q.bed || 'any',
                floor: q.floor || 'any',
                allowPast: !!q.allowPast
            }));
        },

        checkRoom(number, checkin, checkout) {
            return request('/rooms/' + seg(number) + '/check' + toQuery({ checkin: checkin || '', checkout: checkout || '' }));
        },

        // One page of reservations ({ items, total, page, pageSize }), filtered on the
        // server. List items leave out nights and messages; getReservation has them.
        getReservations(filters) {
            const f = filters || {};
            return request('/reservations' + toQuery({ status: f.status || 'all', payment: f.payment || 'any', q: f.q || '', page: f.page || 1 }));
        },
        getReservationStats() { return request('/reservations/stats'); },
        getReservation(resId) { return request('/reservations/' + seg(resId)); },

        updateGuestDetails(resId, input) {
            return request('/reservations/' + seg(resId) + '/guest', {
                method: 'PUT',
                body: {
                    firstName: String(input.firstName || ''),
                    lastName: String(input.lastName || ''),
                    phone: String(input.phone || ''),
                    requests: String(input.requests || ''),
                    guests: Number(input.guests) || null
                }
            });
        },

        // Card details are validated in the browser and discarded. The API only
        // ever receives whether payment was taken and the card-on-file opt-in.
        // Guests must be signed in to their account; the server books under its email.
        createReservation(input) {
            const g = input.guest || {};
            return request('/reservations', {
                method: 'POST',
                body: {
                    room: Number(input.room) || null,
                    checkin: input.checkin,
                    checkout: input.checkout,
                    guests: Number(input.guests) || 1,
                    guest: {
                        firstName: String(g.firstName || ''),
                        lastName: String(g.lastName || ''),
                        email: String(g.email || ''),
                        phone: String(g.phone || ''),
                        requests: String(g.requests || '')
                    },
                    paymentStatus: input.paymentStatus === 'paid' ? 'paid' : 'unpaid',
                    cardOnFile: !!input.cardOnFile,
                    source: input.source === 'staff' ? 'staff' : 'web'
                }
            });
        },

        cancelReservation(resId) { return request('/reservations/' + seg(resId) + '/cancel', { method: 'POST' }); },

        setPaymentStatus(resId, status) {
            return request('/reservations/' + seg(resId) + '/payment-status', { method: 'PUT', body: { status } });
        },

        sendGuestMessage(resId, text) {
            return request('/reservations/' + seg(resId) + '/messages', { method: 'POST', body: { text: String(text) } });
        },

        // Status of every room for a single night.
        getRoomBoard(date) { return request('/rooms/board' + toQuery({ date })); },

        // Upcoming reservations, holds, and blocks for one room.
        getRoomSchedule(number) { return request('/rooms/' + seg(number) + '/schedule'); },

        getHolds() { return request('/holds'); },

        createHold(input) {
            return request('/holds', {
                method: 'POST',
                body: { room: Number(input.room) || null, start: input.start, end: input.end, reason: input.reason }
            });
        },

        releaseHold(holdId) { return request('/holds/' + seg(holdId), { method: 'DELETE' }); },

        getBlocks() { return request('/blocks'); },

        createBlock(input) {
            return request('/blocks', {
                method: 'POST',
                body: { name: input.name, start: input.start, end: input.end, rooms: input.rooms }
            });
        },

        removeBlock(blockId) { return request('/blocks/' + seg(blockId), { method: 'DELETE' }); },

        resetDemoData() { return request('/demo/reset', { method: 'POST' }); },

        // Staff sign-in. The session is an HttpOnly cookie the browser sends on its own.
        // Resolves to the signed-in employee, or null when nobody is signed in.
        async getCurrentEmployee() {
            try {
                return await request('/auth/me');
            } catch (err) {
                if (err.status === 401) return null;
                throw err;
            }
        },

        signIn(email, password) {
            return request('/auth/sign-in', { method: 'POST', body: { email: String(email), password: String(password) } });
        },

        signOut() { return request('/auth/sign-out', { method: 'POST' }); },

        changePassword(currentPassword, newPassword) {
            return request('/auth/password', {
                method: 'PUT',
                body: { currentPassword: String(currentPassword), newPassword: String(newPassword) }
            });
        },

        getEmployees() { return request('/employees'); },

        createEmployee(input) {
            return request('/employees', {
                method: 'POST',
                body: {
                    firstName: String(input.firstName || ''),
                    lastName: String(input.lastName || ''),
                    email: String(input.email || ''),
                    password: String(input.password || ''),
                    role: input.role === 'manager' ? 'manager' : 'staff'
                }
            });
        },

        setEmployeeActive(id, active) {
            return request('/employees/' + seg(id) + '/active', { method: 'PUT', body: { active: !!active } });
        },

        setEmployeeRole(id, role) {
            return request('/employees/' + seg(id) + '/role', { method: 'PUT', body: { role: role === 'manager' ? 'manager' : 'staff' } });
        },

        // Shifts starting on `start` and the following days (a week by default).
        getShifts(start, days) { return request('/shifts' + toQuery({ start, days: days || 7 })); },

        createShift(input) {
            return request('/shifts', {
                method: 'POST',
                body: {
                    employeeId: Number(input.employeeId) || null,
                    date: input.date,
                    start: input.start,
                    end: input.end,
                    note: String(input.note || '')
                }
            });
        },

        removeShift(shiftId) { return request('/shifts/' + seg(shiftId), { method: 'DELETE' }); },

        setEmployeePassword(id, password) {
            return request('/employees/' + seg(id) + '/password', { method: 'PUT', body: { password: String(password) } });
        },

        // Guest accounts. The session is an HttpOnly cookie, separate from the staff one.
        // Resolves to the signed-in guest, or null when nobody is signed in.
        getCurrentGuest() { return request('/account/me'); },

        registerGuest(input) {
            return request('/account', {
                method: 'POST',
                body: {
                    firstName: String(input.firstName || ''),
                    lastName: String(input.lastName || ''),
                    email: String(input.email || ''),
                    phone: String(input.phone || ''),
                    password: String(input.password || '')
                }
            });
        },

        signInGuest(email, password) {
            return request('/account/sign-in', { method: 'POST', body: { email: String(email), password: String(password) } });
        },

        signOutGuest() { return request('/account/sign-out', { method: 'POST' }); },

        // The signed-in guest's own reservations (latest arrival first), and one of them in full.
        getMyReservations() { return request('/account/reservations'); },
        getMyReservation(resId) { return request('/account/reservations/' + seg(resId)); },

        // Pay for one of the guest's own upcoming stays. Card details are validated in the
        // browser and discarded; the API only receives the card-on-file opt-in.
        payReservation(resId, cardOnFile) {
            return request('/account/reservations/' + seg(resId) + '/pay', { method: 'POST', body: { cardOnFile: !!cardOnFile } });
        },

        // The guest pressed Confirm booking on a concierge card. A room taken since the card
        // was shown rejects with status 409 and err.data.proposal, a fresh card (or null).
        confirmConciergeBooking(token) {
            return request('/concierge/bookings', { method: 'POST', body: { token: String(token) } });
        },

        // Druid AI Assistant (POST api/concierge). Streams the reply:
        // onText(delta) appends text, onReset() discards text shown so far, and
        // onEvent(event) receives the rest: { type: 'proposal', proposal } (a booking card)
        // or { type: 'sign_in' } (booking needs the guest to sign in first).
        // Resolves when the reply is complete; rejects with a guest-friendly message.
        async askConcierge(messages, page, onText, onReset, onEvent) {
            let response;
            try {
                response = await fetch(API_BASE + '/concierge', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json' },
                    body: JSON.stringify({ messages, page })
                });
            } catch (e) {
                throw new Error(CONCIERGE_OFFLINE);
            }

            if (!response.ok || !response.body) {
                let message = CONCIERGE_OFFLINE;
                try {
                    const data = await response.json();
                    if (response.status === 429 && data.message) message = data.message;
                } catch (e) { /* keep the default message */ }
                throw new Error(message);
            }

            // Newline-delimited JSON events: {type: 'text' | 'reset' | 'done' | 'error'}
            const reader = response.body.getReader();
            const decoder = new TextDecoder();
            let buffer = '';
            let done = false;
            let serverError = '';
            const handle = (line) => {
                if (!line.trim()) return;
                const event = JSON.parse(line);
                if (event.type === 'text') onText(event.text);
                else if (event.type === 'reset') onReset();
                else if (event.type === 'error') serverError = event.message || CONCIERGE_OFFLINE;
                else if (event.type === 'done') done = true;
                else if (onEvent) onEvent(event);
            };
            try {
                for (;;) {
                    const chunk = await reader.read();
                    if (chunk.done) break;
                    buffer += decoder.decode(chunk.value, { stream: true });
                    const lines = buffer.split('\n');
                    buffer = lines.pop();
                    lines.forEach(handle);
                }
                handle(buffer);
            } catch (e) {
                throw new Error(CONCIERGE_OFFLINE);
            }
            if (serverError) throw new Error(serverError);
            if (!done) throw new Error(CONCIERGE_OFFLINE);
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

    // Card fields shared by the booking form and the Pay now form. Card details are
    // checked here and then discarded: they're never stored, logged, or sent.
    function setupCardInputs(form) {
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
    }

    // validate() rules for the card fields.
    function cardRules(form) {
        return [
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
        ];
    }

    // An upcoming, unpaid stay the signed-in guest can pay for online.
    const canPayNow = (r) => !!r && r.status === 'confirmed' && r.paymentStatus !== 'paid' && r.checkout > todayISO();

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
        account: 'Your stays · Druid City Hotel',
        pay: 'Payment · Druid City Hotel',
        staff: 'Staff Portal · Druid City Hotel'
    };

    const VIEWS = {
        home: renderHome,
        search: renderSearch,
        booking: renderBooking,
        confirmation: renderConfirmation,
        rooms: renderRooms,
        account: renderAccount,
        pay: renderPay,
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
            // Choosing a different room updates the room named above the Reserve button.
            $('#search-results').addEventListener('change', (e) => {
                if (e.target.name !== 'room') return;
                const option = e.target.selectedOptions[0];
                $('[data-room-pick]', e.target.form).textContent = option.textContent;
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
        // Each type offers one room, picked by the server (highest open floor); the full
        // list sits behind "Choose a different room" for guests who care which one.
        const roomLabel = (r) => 'Room ' + r.number + ' · ' + FLOOR_NAMES[r.floor];
        results.innerHTML = groups.map((g) => {
            const t = ROOM_TYPES[g.type];
            const floors = Array.from(new Set(g.rooms.map((r) => r.floor)));
            const options = g.rooms.map((r) =>
                '<option value="' + r.number + '"' + (r.number === g.suggested.number ? ' selected' : '') + '>' +
                esc(roomLabel(r)) + '</option>').join('');
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
                    '<p class="result__pick"><span class="label-heading">Your room</span> ' +
                        '<span data-room-pick>' + esc(roomLabel(g.suggested)) + '</span></p>' +
                    (g.rooms.length > 1
                        ? '<details class="result__choose"><summary>Choose a different room</summary>' +
                            '<div class="field">' +
                                '<label for="' + fid + '">Room</label>' +
                                '<select id="' + fid + '" name="room">' + options + '</select>' +
                            '</div>' +
                          '</details>'
                        : '<input type="hidden" name="room" value="' + g.suggested.number + '">') +
                    '<button type="submit" class="btn">Reserve</button>' +
                '</form>' +
            '</article>';
        }).join('');
    }

    /* ---------- Booking and checkout ---------- */

    let bookingContext = null;
    // The reservation the guest just made. Looking a reservation up by id is staff only
    // (ids are guessable), so the confirmation page shows this copy instead.
    let lastBooking = null;

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

        setupCardInputs(form);

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
            if (payNow) rules.push(...cardRules(form));
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
                lastBooking = reservation;
                go('confirmation', { id: reservation.id }, true);
            } catch (err) {
                // The guest session ended: show the sign-in prompt in place of the form.
                if (err.status === 401) { router(); return; }
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
        $$('[data-booking-back]').forEach((a) => { a.href = '#/search?' + backParams.toString(); });

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

        notice.hidden = true;
        layout.hidden = false;

        // Booking needs a guest account. Show the stay and price either way, so the guest
        // sees what they're signing in for, but only offer the form once they're signed in.
        const guest = await ensureGuest();
        $('#booking-signin').hidden = !!guest;
        form.hidden = !guest;
        bookingContext = guest ? { room: info.room.number, checkin, checkout } : null;
        if (guest) {
            // Prefill from the account without overwriting anything typed on this visit.
            if (!form.firstName.value) form.firstName.value = guest.firstName;
            if (!form.lastName.value) form.lastName.value = guest.lastName;
            if (!form.phone.value) form.phone.value = guest.phone;
            form.email.value = guest.email;
        } else {
            $('#booking-signin-link').href = '#/account?' + new URLSearchParams({ next: location.hash }).toString();
        }

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
        const id = route.params.get('id') || '';
        let r = lastBooking && lastBooking.id === id ? lastBooking : null;
        // Reloaded or opened from a link: a signed-in guest can load their own reservation.
        if (!r && id && await ensureGuest()) {
            body.innerHTML = '<p class="status-line" role="status">Loading your reservation…</p>';
            try {
                r = await api.getMyReservation(id);
            } catch (err) {
                r = null;
            }
        }
        if (!r) {
            body.innerHTML = '<p class="lede">' + (id ? 'Your confirmation code is <strong>' + esc(id) + '</strong>. ' : '') +
                'Keep it handy. Sign in to see the stays booked on your account, or call the front desk if you need to change one.</p>' +
                '<div class="form-actions"><a class="btn" href="#/account">Your stays</a>' +
                '<a class="btn btn--ghost" href="#/home">Back to the hotel</a></div>';
            return;
        }
        const t = ROOM_TYPES[r.type];
        const cancelled = r.status === 'cancelled';
        body.innerHTML =
            '<div class="confirmation">' +
                '<div>' +
                    '<p class="lede">' + (cancelled ? 'This reservation was cancelled.'
                        : 'Thank you, ' + esc(r.guest.firstName) + '. Your room is held and waiting.') + '</p>' +
                    '<p class="confirmation__code"><span class="label-heading">Confirmation</span> ' + esc(r.id) + '</p>' +
                    '<dl class="summary__list">' +
                        '<div><dt>Room</dt><dd>' + esc(t.name) + ', room ' + r.room + '</dd></div>' +
                        '<div><dt>Arrive</dt><dd>' + esc(fmtDate(r.checkin)) + ', from 3 PM</dd></div>' +
                        '<div><dt>Depart</dt><dd>' + esc(fmtDate(r.checkout)) + ', by 11 AM</dd></div>' +
                        '<div><dt>Guests</dt><dd>' + r.guests + '</dd></div>' +
                        '<div><dt>Total</dt><dd>' + money(r.total) + '</dd></div>' +
                        '<div><dt>Payment</dt><dd>' + (r.paymentStatus === 'paid' ? 'Paid in full' : 'Due at check-in') +
                            (r.cardOnFile ? ' · Card kept on file' : '') + '</dd></div>' +
                        (r.guest.requests ? '<div><dt>Requests</dt><dd>' + esc(r.guest.requests) + '</dd></div>' : '') +
                    '</dl>' +
                '</div>' +
                '<div>' +
                    (cancelled ? '' :
                    '<h2 class="label-heading">What happens next</h2>' +
                    '<ol class="timeline">' +
                        '<li><strong>Now</strong> A confirmation is on its way to ' + esc(r.guest.email) + '.</li>' +
                        '<li><strong>Two days out</strong> We’ll send parking, directions, and a few supper suggestions.</li>' +
                        '<li><strong>Arrival</strong> Your room is ready from 3 PM. Running late? We’ll wait up.</li>' +
                    '</ol>' +
                    '<p class="fine-print">Prototype: emails are simulated and recorded in the Staff Portal.</p>') +
                    '<div class="form-actions">' +
                        (canPayNow(r) && guestUser ? '<a class="btn" href="#/pay?id=' + encodeURIComponent(r.id) + '">Pay now</a>' : '') +
                        '<a class="btn btn--ghost" href="#/account">Your stays</a>' +
                        '<a class="btn btn--ghost" href="#/home">Back to the hotel</a></div>' +
                '</div>' +
            '</div>';
    }

    /* ---------- Pay now ---------- */

    // The stay being paid for on #/pay.
    let payingFor = null;

    function setupPayForm() {
        const form = $('#pay-form');
        setupCardInputs(form);
        form.addEventListener('submit', async (e) => {
            e.preventDefault();
            if (!payingFor) return;
            if (!validate(cardRules(form))) return;
            const button = $('#pay-submit');
            const notice = $('#pay-notice');
            button.disabled = true;
            try {
                // Card fields are validated above and then discarded.
                const reservation = await api.payReservation(payingFor.id, form.cardOnFile.checked);
                form.reset();
                lastBooking = reservation;
                go('confirmation', { id: reservation.id }, true);
            } catch (err) {
                // The guest session ended: show the sign-in prompt instead.
                if (err.status === 401) { router(); return; }
                showNotice(notice, err.message);
                notice.focus();
            } finally {
                button.disabled = false;
            }
        });
    }

    async function renderPay(route) {
        const view = $('[data-view="pay"]');
        if (!view.dataset.ready) {
            setupPayForm();
            view.dataset.ready = 'true';
        }
        const id = route.params.get('id') || '';
        const notice = $('#pay-notice');
        const layout = $('#pay-layout');
        const form = $('#pay-form');
        clearErrors(form);
        showNotice(notice, '');
        layout.hidden = true;
        payingFor = null;

        if (!(await ensureGuest())) {
            notice.hidden = false;
            notice.innerHTML = 'Sign in to pay for your stay. <a href="#/account?' +
                esc(new URLSearchParams({ next: location.hash }).toString()) + '">Sign in</a>';
            return null;
        }
        let r;
        try {
            r = await api.getMyReservation(id);
        } catch (err) {
            if (err.status === 401) return renderPay(route);
            showNotice(notice, err.message);
            return null;
        }
        if (!canPayNow(r)) {
            notice.hidden = false;
            notice.innerHTML = esc(r.paymentStatus === 'paid' ? 'This stay is already paid in full.'
                : r.status === 'cancelled' ? 'This reservation was cancelled, so there’s nothing to pay.'
                : 'This stay has ended. Please settle it with the front desk.') +
                ' <a href="#/confirmation?id=' + encodeURIComponent(r.id) + '">See the reservation</a>';
            return null;
        }

        payingFor = r;
        layout.hidden = false;
        const t = ROOM_TYPES[r.type];
        $('#pay-submit').textContent = 'Pay ' + money(r.total);
        $('#pay-back').href = '#/confirmation?id=' + encodeURIComponent(r.id);
        $('#pay-summary').innerHTML =
            '<p class="summary__room">' + esc(t ? t.name : r.type) + '</p>' +
            '<p class="result__meta">Room ' + r.room + ' · Confirmation ' + esc(r.id) + '</p>' +
            '<dl class="summary__list">' +
                '<div><dt>Arrive</dt><dd>' + esc(fmtDate(r.checkin)) + ', from 3 PM</dd></div>' +
                '<div><dt>Depart</dt><dd>' + esc(fmtDate(r.checkout)) + ', by 11 AM</dd></div>' +
                '<div><dt>Nights</dt><dd>' + esc(nightBreakdown(r.nights)) + '</dd></div>' +
            '</dl>' +
            '<p class="summary__total"><span>Total</span> <strong>' + money(r.total) + '</strong></p>' +
            '<p class="fine-print">Before applicable taxes.</p>';
        return null;
    }

    /* ---------- Property and room information ---------- */

    async function renderRooms() {
        const { rates } = await api.getRates();
        const counts = {};
        (await api.getRooms()).forEach((r) => { counts[r.type] = (counts[r.type] || 0) + 1; });
        // Least to most expensive, as the rates come from the server.
        const order = Object.keys(rates).concat(Object.keys(ROOM_TYPES).filter((key) => !rates[key]));
        $('[data-room-types]').innerHTML = order.map((key) => {
            const t = ROOM_TYPES[key];
            const rate = rates[key];
            const photo = ROOM_PHOTOS[key];
            return '<article class="room-type">' +
                (photo ? '<img class="room-type__image" src="Resources/images/' + photo[0] + '" alt="' + esc(photo[1]) + '" loading="lazy">' : '') +
                '<h2 class="room-type__name">' + esc(t.name) + '</h2>' +
                '<p class="room-type__blurb">' + esc(t.blurb) + '</p>' +
                '<dl class="room-type__facts">' +
                    '<div class="room-type__fact--wide"><dt>Bed</dt><dd>' + esc(t.bedLabel) + '</dd></div>' +
                    '<div><dt>Sleeps</dt><dd>' + t.sleeps + '</dd></div>' +
                    '<div><dt>Size</dt><dd>' + esc(t.size) + '</dd></div>' +
                    (rate
                        ? '<div><dt>Weeknights</dt><dd>' + money(rate.weekday) + ' / night</dd></div>' +
                          '<div><dt>Fri &amp; Sat</dt><dd>' + money(rate.weekend) + ' / night</dd></div>'
                        : '') +
                '</dl>' +
                '<p class="fine-print">' + plural(counts[key], 'room') + ' of this kind</p>' +
            '</article>';
        }).join('') +
            '<p class="fine-print">Prices are before taxes. Special dates, such as home game weekends, may be priced differently; ' +
            'you’ll see your exact total when you choose dates.</p>' +
            '<p><a class="btn" href="#/search">Check availability</a></p>';
        const amounts = Object.values(rates).flatMap((r) => [r.weekday, r.weekend]);
        $('[data-rate-fact]').textContent = money(Math.min(...amounts)) + ' to ' + money(Math.max(...amounts)) +
            ' a night, by room; Friday and Saturday nights cost a little more';
    }

    /* ---------- Guest account ---------- */

    // The signed-in guest: undefined until checked with the server, null when signed out.
    let guestUser;
    // The concierge asked the guest to sign in to book, so it picks the booking back up after.
    let conciergeAwaitingSignIn = false;

    let guestCheck = null;  // the check in flight, shared by everyone waiting on it

    async function ensureGuest() {
        if (guestUser !== undefined) return guestUser;
        if (!guestCheck) {
            guestCheck = api.getCurrentGuest().then((guest) => {
                guestUser = guest;
                updateAccountNav();
            }, () => {
                // Offline: treat as signed out for now, and check again next time.
            }).finally(() => { guestCheck = null; });
        }
        await guestCheck;
        return guestUser === undefined ? null : guestUser;
    }

    function updateAccountNav() {
        $$('[data-account-link]').forEach((a) => { a.textContent = guestUser ? 'Your stays' : 'Sign in'; });
    }

    // Called by request() on a guest 401 (the guest session ended), and on sign-out.
    function onGuestSignedOut() {
        if (guestUser === null) return;
        guestUser = null;
        updateAccountNav();
        ['#account-upcoming', '#account-past', '#account-who', '#account-status'].forEach((sel) => { $(sel).innerHTML = ''; });
    }

    async function renderAccount(route) {
        const view = $('[data-view="account"]');
        if (!view.dataset.ready) {
            setupAccount();
            view.dataset.ready = 'true';
        }
        const guest = await ensureGuest();
        $('#account-signed-out').hidden = !!guest;
        $('#account-signed-in').hidden = !guest;

        if (!guest) {
            $('#account-title').textContent = 'Sign in to book.';
            $('#account-lede').textContent = route.params.get('next')
                ? 'Sign in, or create an account, and we’ll take you right back to your booking.'
                : 'A hotel account keeps your reservations together, and lets our concierge book a room for you in the chat.';
            return null;
        }

        $('#account-title').textContent = 'Your stays.';
        $('#account-lede').textContent = 'Every reservation made from your account, on the site or with our concierge.';
        $('#account-who').textContent = guest.firstName + ' ' + guest.lastName + ' (' + guest.email + ')';
        const status = $('#account-status');
        status.textContent = 'Loading your stays…';
        let stays;
        try {
            stays = await api.getMyReservations();
        } catch (err) {
            // A 401 has already signed the page out; show the sign-in forms instead.
            if (err.status === 401) return renderAccount(route);
            status.textContent = err.message;
            return null;
        }
        const today = todayISO();
        const isUpcoming = (r) => r.status === 'confirmed' && r.checkout > today;
        const upcoming = stays.filter(isUpcoming).reverse();  // soonest first
        const past = stays.filter((r) => !isUpcoming(r));
        status.textContent = stays.length ? plural(upcoming.length, 'upcoming stay') : 'No stays yet.';
        $('#account-upcoming').innerHTML = upcoming.length
            ? stayList(upcoming)
            : '<p class="hint">Nothing booked yet. <a href="#/search">Find a room</a>, or ask our concierge to book one for you.</p>';
        $('#account-past').innerHTML = past.length ? stayList(past) : '<p class="hint">None yet.</p>';
        return null;
    }

    function stayList(stays) {
        return '<ul class="stays">' + stays.map((r) => {
            const t = ROOM_TYPES[r.type];
            const cancelled = r.status === 'cancelled';
            return '<li class="stay">' +
                '<div>' +
                    '<p class="stay__name">' + esc(t ? t.name : r.type) + ', room ' + r.room + '</p>' +
                    '<p class="result__meta">' + esc(stayLine(r.checkin, r.checkout)) + ' · ' + plural(r.guests, 'guest') + '</p>' +
                '</div>' +
                '<div class="stay__side">' +
                    '<p class="stay__total">' + money(r.total) + '</p>' +
                    '<p class="result__meta">' + esc(r.id) + ' · ' +
                        (cancelled ? 'Cancelled' : r.paymentStatus === 'paid' ? 'Paid' : 'Due at check-in') + '</p>' +
                    (cancelled ? '' : '<p class="stay__links">' +
                        '<a href="#/confirmation?id=' + encodeURIComponent(r.id) + '">Details' +
                            '<span class="visually-hidden"> for ' + esc(r.id) + '</span></a>' +
                        (canPayNow(r) ? ' · <a href="#/pay?id=' + encodeURIComponent(r.id) + '">Pay now' +
                            '<span class="visually-hidden"> for ' + esc(r.id) + '</span></a>' : '') +
                    '</p>') +
                '</div>' +
            '</li>';
        }).join('') + '</ul>';
    }

    function setupAccount() {
        const sf = $('#guest-signin-form');
        sf.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#gs-message');
            showNotice(message, '');
            if (!validate([[sf.email, isEmail], [sf.password, required('Enter your password.')]])) return;
            const button = $('button[type="submit"]', sf);
            button.disabled = true;
            try {
                guestUser = await api.signInGuest(sf.email.value.trim(), sf.password.value);
            } catch (err) {
                showNotice(message, err.message);
                sf.password.value = '';
                sf.password.focus();
                return;
            } finally {
                button.disabled = false;
            }
            sf.reset();
            guestSignedIn();
        });

        const rf = $('#guest-register-form');
        rf.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#gr-message');
            showNotice(message, '');
            const ok = validate([
                [rf.firstName, required('Enter a first name.')],
                [rf.lastName, required('Enter a last name.')],
                [rf.email, isEmail],
                [rf.phone, isPhone],
                [rf.password, (v) => (v.length >= 8 ? '' : 'Use a password of at least 8 characters.')]
            ]);
            if (!ok) return;
            const button = $('button[type="submit"]', rf);
            button.disabled = true;
            try {
                guestUser = await api.registerGuest({
                    firstName: rf.firstName.value.trim(),
                    lastName: rf.lastName.value.trim(),
                    email: rf.email.value.trim(),
                    phone: rf.phone.value.trim(),
                    password: rf.password.value
                });
            } catch (err) {
                showNotice(message, err.message);
                return;
            } finally {
                button.disabled = false;
            }
            rf.reset();
            guestSignedIn();
        });

        $('[data-action="guest-sign-out"]').addEventListener('click', async () => {
            try {
                await api.signOutGuest();
            } catch (err) {
                $('#account-status').textContent = err.message;
                return;
            }
            onGuestSignedOut();
            router();
        });
    }

    // After signing in or creating an account: back to the page that sent the guest here
    // (only a route on this site), or their stays.
    function guestSignedIn() {
        updateAccountNav();
        if (conciergeAwaitingSignIn) {
            conciergeAwaitingSignIn = false;
            addConciergeNote('You’re signed in, ' + guestUser.firstName + '. Shall I go ahead and set up that booking?');
            setConciergeState('open', false);
        }
        const next = parseHash().params.get('next') || '';
        if (/^#\/(?!account\b)[a-z]+/.test(next)) location.replace(next);
        else router();
    }

    /* ---------- Staff portal ---------- */

    const STAFF_PANELS = ['reservations', 'new', 'rooms', 'events', 'rates', 'employees'];
    const STATUS_LABELS = { available: 'Open', reservation: 'Booked', hold: 'Hold', block: 'Event' };
    let selectedRoom = null;
    // The signed-in employee: undefined until checked with the server, null when signed out.
    let staffUser;
    const isManager = () => !!staffUser && staffUser.role === 'manager';

    async function renderStaff(route, changedView) {
        const panel = STAFF_PANELS.includes(route.sub) ? route.sub : 'reservations';
        if (!$('.view--staff').dataset.ready) {
            setupStaff();
            $('.view--staff').dataset.ready = 'true';
        }

        if (staffUser === undefined) {
            try {
                staffUser = await api.getCurrentEmployee();
            } catch (err) {
                showStaffSignIn(err.message);
                return null;
            }
        }
        if (!staffUser) {
            showStaffSignIn();
            return changedView ? null : $('#signin-title');
        }
        $('#staff-signin').hidden = true;
        $('#staff-portal').hidden = false;
        $('#staff-session').hidden = false;
        $('#staff-name').textContent = staffUser.firstName + ' ' + staffUser.lastName + (isManager() ? ' (manager)' : '');
        // The API enforces roles; this only hides controls staff can't use.
        $$('[data-manager-only]').forEach((el) => { el.hidden = !isManager(); });

        $$('[data-panel]').forEach((el) => { el.hidden = el.dataset.panel !== panel; });
        $$('[data-staff-tab]').forEach((a) => {
            if (a.dataset.staffTab === panel) a.setAttribute('aria-current', 'page');
            else a.removeAttribute('aria-current');
        });

        try {
            if (panel === 'reservations') await renderReservationsPanel(route.params.get('id'));
            if (panel === 'new') await renderNewPanel();
            if (panel === 'rooms') await renderRoomsPanel();
            if (panel === 'events') await renderEventsPanel();
            if (panel === 'rates') await renderRatesPanel();
            if (panel === 'employees') await renderEmployeesPanel();
        } catch (err) {
            // request() has already switched to the sign-in form for an ended session.
            if (err.status === 401) return changedView ? null : $('#signin-title');
            throw err;
        }
        if (changedView) return null;
        if (panel === 'reservations' && route.params.get('id')) return $('#res-detail h3');
        return $('[data-panel="' + panel + '"] .panel__title');
    }

    // Hides the portal and shows the sign-in form, with an optional message.
    function showStaffSignIn(message) {
        $('#staff-portal').hidden = true;
        $('#staff-session').hidden = true;
        $('#staff-signin').hidden = false;
        showNotice($('#si-message'), message || '');
    }

    // Called by request() on any 401. Clears what the last employee could see, so it
    // isn't left in the page, and shows the sign-in form if the portal is open.
    function onStaffSignedOut() {
        if (staffUser === null) return;
        staffUser = null;
        selectedRoom = null;
        ['#staff-stats', '#res-detail', '#res-table', '#res-count', '#room-grid', '#room-panel', '#room-summary',
            '#holds-list', '#blocks-list', '#overrides-list', '#rate-preview', '#rate-fields', '#emp-table', '#emp-status',
            '#sched-days', '#sched-week', '#er-employee', '#sh-employee'].forEach((sel) => {
            $(sel).innerHTML = '';
        });
        $('#res-pager').hidden = true;
        schedWeek = null;
        $$('#staff-portal form').forEach((f) => { f.reset(); clearErrors(f); });
        if (parseHash().name === 'staff') showStaffSignIn('Your staff session has ended. Please sign in again.');
    }

    function setupStaff() {
        // Sign-in and sign-out
        const sf = $('#signin-form');
        sf.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#si-message');
            showNotice(message, '');
            const ok = validate([
                [sf.email, isEmail],
                [sf.password, required('Enter your password.')]
            ]);
            if (!ok) return;
            const button = $('button[type="submit"]', sf);
            button.disabled = true;
            try {
                staffUser = await api.signIn(sf.email.value.trim(), sf.password.value);
            } catch (err) {
                showNotice(message, err.message);
                sf.password.value = '';
                sf.password.focus();
                return;
            } finally {
                button.disabled = false;
            }
            sf.reset();
            router();
        });
        $('[data-action="sign-out"]').addEventListener('click', async () => {
            try {
                await api.signOut();
            } catch (err) {
                window.alert(err.message);
                return;
            }
            onStaffSignedOut();
            showNotice($('#si-message'), '');
            $('#signin-title').focus();
        });

        $('[data-action="reset-demo"]').addEventListener('click', async () => {
            if (!window.confirm('Reset all reservations, holds, blocks, and rates to the sample data?')) return;
            try {
                await api.resetDemoData();
            } catch (err) {
                window.alert(err.message);
                return;
            }
            selectedRoom = null;
            router();
        });

        // Reservations
        $('#res-filters').addEventListener('input', () => {
            resPage = 1;
            renderReservationTable();
        });
        $('#res-filters').addEventListener('submit', (e) => e.preventDefault());
        $('#res-pager').addEventListener('click', async (e) => {
            const button = e.target.closest('[data-page]');
            if (!button) return;
            resPage += button.dataset.page === 'next' ? 1 : -1;
            await renderReservationTable();
            if (button.disabled) {
                const other = $('#res-pager [data-page]:not(:disabled)');
                if (other) other.focus();
            }
        });
        $('#res-detail').addEventListener('click', async (e) => {
            const button = e.target.closest('[data-action]');
            if (!button) return;
            const id = button.dataset.id;
            const editForm = $('#res-detail [data-form="guest"]');
            if (button.dataset.action === 'edit-guest') {
                editForm.hidden = false;
                editForm.firstName.focus();
                return;
            }
            if (button.dataset.action === 'close-edit') {
                editForm.hidden = true;
                $('#res-detail [data-action="edit-guest"]').focus();
                return;
            }
            try {
                if (button.dataset.action === 'cancel') {
                    if (!window.confirm('Cancel reservation ' + id + '? The room will be released.')) return;
                    await api.cancelReservation(id);
                }
                if (button.dataset.action === 'toggle-paid') {
                    await api.setPaymentStatus(id, button.dataset.next);
                }
            } catch (err) {
                window.alert(err.message);
            }
            await renderReservationsPanel(id);
            const again = $('#res-detail [data-action="' + button.dataset.action + '"]');
            (again || $('#res-detail h3')).focus();
        });
        $('#res-detail').addEventListener('submit', async (e) => {
            e.preventDefault();
            const form = e.target;
            if (form.dataset.form === 'guest') {
                await saveGuestDetails(form);
                return;
            }
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
            let released = 'Hold released.';
            try {
                await api.releaseHold(button.dataset.id);
            } catch (err) {
                released = err.message;
            }
            await renderRoomsPanel();
            $('#room-summary').textContent = released;
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
            try {
                await api.removeBlock(button.dataset.id);
            } catch (err) {
                window.alert(err.message);
            }
            await renderEventsPanel();
            $('[data-panel="events"] .panel__title').focus();
        });

        // Rates
        const rf = $('#rates-form');
        rf.addEventListener('submit', async (e) => {
            e.preventDefault();
            const positive = (v) => (Number(v) > 0 ? '' : 'Enter a positive amount.');
            const inputs = $$('#rate-fields input', rf);
            if (!validate(inputs.map((input) => [input, positive]))) return;
            const rates = {};
            inputs.forEach((input) => {
                rates[input.dataset.type] = rates[input.dataset.type] || {};
                rates[input.dataset.type][input.dataset.night] = input.value;
            });
            await api.saveRates(rates);
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
            let removed = 'Special rate removed.';
            try {
                await api.removeRateOverride(button.dataset.date);
            } catch (err) {
                removed = err.message;
            }
            await renderRatesPanel();
            $('#rates-message').textContent = removed;
            of.date.focus();
        });

        // Employees
        const isPassword = (v) => (v.length >= 12 ? '' : 'Use at least 12 characters.');
        const en = $('#emp-new-form');
        en.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#en-message');
            showNotice(message, '');
            const ok = validate([
                [en.firstName, required('Enter a first name.')],
                [en.lastName, required('Enter a last name.')],
                [en.email, isEmail],
                [en.password, isPassword]
            ]);
            if (!ok) return;
            let employee;
            try {
                employee = await api.createEmployee({
                    firstName: en.firstName.value, lastName: en.lastName.value, email: en.email.value,
                    password: en.password.value, role: en.role.value
                });
            } catch (err) {
                showNotice(message, err.message);
                return;
            }
            en.reset();
            await renderEmployeesPanel();
            $('#emp-status').textContent = 'Added ' + employee.firstName + ' ' + employee.lastName + '. They can sign in now.';
            $('[data-panel="employees"] .panel__title').focus();
        });
        $('#emp-table').addEventListener('click', async (e) => {
            const button = e.target.closest('[data-action="toggle-employee"], [data-action="toggle-role"]');
            if (!button) return;
            const action = button.dataset.action;
            const name = button.dataset.name;
            const next = button.dataset.next;
            if (action === 'toggle-employee' && next === 'inactive' &&
                !window.confirm('Deactivate ' + name + '? They won’t be able to sign in.')) return;
            if (action === 'toggle-role' &&
                !window.confirm(next === 'manager'
                    ? 'Make ' + name + ' a manager? They’ll be able to manage accounts, the schedule, and sample data.'
                    : 'Make ' + name + ' staff? They’ll lose manager tools.')) return;
            let status;
            try {
                if (action === 'toggle-employee') {
                    await api.setEmployeeActive(button.dataset.id, next === 'active');
                    status = name + (next === 'active' ? ' can sign in again.' : ' is deactivated.');
                } else {
                    await api.setEmployeeRole(button.dataset.id, next);
                    status = name + ' is now ' + (next === 'manager' ? 'a manager.' : 'staff.');
                }
            } catch (err) {
                status = err.message;
            }
            await renderEmployeesPanel();
            $('#emp-status').textContent = status;
            const again = $('#emp-table [data-action="' + action + '"][data-id="' + button.dataset.id + '"]');
            (again || $('[data-panel="employees"] .panel__title')).focus();
        });

        // Schedule
        $$('[data-week]').forEach((b) => b.addEventListener('click', async () => {
            const step = b.dataset.week;
            schedWeek = step === 'this' ? weekStart(todayISO()) : addDays(schedWeek || weekStart(todayISO()), step === 'next' ? 7 : -7);
            await renderSchedule();
            if (step === 'this') $('#sched-title').focus();
        }));
        const sh = $('#shift-form');
        sh.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#sh-message');
            showNotice(message, '');
            const ok = validate([
                [sh.employee, required('Choose an employee.')],
                [sh.date, (v) => (parseISO(v) ? '' : 'Choose a date.')],
                [sh.start, (v) => (/^\d{2}:\d{2}$/.test(v) ? '' : 'Enter a start time.')],
                [sh.end, (v) => (!/^\d{2}:\d{2}$/.test(v) ? 'Enter an end time.' : v === sh.start.value ? 'End time can’t equal the start.' : '')]
            ]);
            if (!ok) return;
            let shift;
            try {
                shift = await api.createShift({
                    employeeId: sh.employee.value, date: sh.date.value, start: sh.start.value, end: sh.end.value, note: sh.note.value
                });
            } catch (err) {
                showNotice(message, err.message);
                return;
            }
            // Keep the employee, date, and times for adding the next shift quickly.
            sh.note.value = '';
            schedWeek = weekStart(shift.date);
            await renderSchedule();
            showNotice(message, 'Added ' + shift.employeeName + ', ' + fmtDate(shift.date, 'day') + ', ' +
                fmtTime(shift.start) + ' – ' + fmtTime(shift.end) + '.');
        });
        $('#sched-days').addEventListener('click', async (e) => {
            const button = e.target.closest('[data-action="remove-shift"]');
            if (!button) return;
            if (!window.confirm('Remove ' + button.dataset.label + '?')) return;
            let status = 'Shift removed.';
            try {
                await api.removeShift(button.dataset.id);
            } catch (err) {
                status = err.message;
            }
            await renderSchedule();
            $('#sched-week').textContent += ' ' + status;
            $('#sched-title').focus();
        });
        const er = $('#emp-reset-form');
        er.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#er-message');
            showNotice(message, '');
            const ok = validate([
                [er.employee, required('Choose an employee.')],
                [er.password, isPassword]
            ]);
            if (!ok) return;
            const name = er.employee.selectedOptions[0].textContent;
            try {
                await api.setEmployeePassword(er.employee.value, er.password.value);
            } catch (err) {
                showNotice(message, err.message);
                return;
            }
            er.password.value = '';
            showNotice(message, 'New password set for ' + name + '.');
        });
        const eo = $('#emp-own-form');
        eo.addEventListener('submit', async (e) => {
            e.preventDefault();
            const message = $('#eo-message');
            showNotice(message, '');
            const ok = validate([
                [eo.currentPassword, required('Enter your current password.')],
                [eo.newPassword, isPassword]
            ]);
            if (!ok) return;
            try {
                await api.changePassword(eo.currentPassword.value, eo.newPassword.value);
            } catch (err) {
                showNotice(message, err.message);
                return;
            }
            eo.reset();
            showNotice(message, 'Password changed. You’re still signed in here; other devices will need the new one.');
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
        const [stats, board] = await Promise.all([api.getReservationStats(), api.getRoomBoard(todayISO())]);
        const open = board.filter((r) => r.status === 'available').length;
        $('#staff-stats').innerHTML =
            '<div><dt>Arrivals today</dt><dd>' + stats.arrivals + '</dd></div>' +
            '<div><dt>In house tonight</dt><dd>' + stats.inHouse + '</dd></div>' +
            '<div><dt>Open tonight</dt><dd>' + open + ' of ' + board.length + '</dd></div>' +
            '<div><dt>Not yet paid</dt><dd>' + money(stats.outstanding) + '</dd></div>';

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
        const canEdit = r.status === 'confirmed';
        const guestOptions = Array.from({ length: Math.max(t.sleeps, r.guests) }, (_, i) => i + 1).map((n) =>
            '<option value="' + n + '"' + (n === r.guests ? ' selected' : '') + '>' + n + '</option>').join('');
        const messages = r.messages.slice().reverse().map((m) =>
            '<li><p class="message__meta">' + esc(fmtTimestamp(m.at)) + ' · ' + (m.from === 'staff' ? 'Front desk' : m.from === 'concierge' ? 'Concierge' : 'Automatic') + '</p>' +
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
                        '<div><dt>Booked</dt><dd>' + esc(fmtTimestamp(r.createdAt)) + ({ staff: ' by front desk', concierge: ' with the concierge' }[r.source] || ' online') + '</dd></div>' +
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
                    (canEdit ? '<button type="button" class="btn btn--ghost" data-action="edit-guest" data-id="' + esc(r.id) + '">Edit guest details</button>' : '') +
                    (canCancel ? '<button type="button" class="btn btn--danger" data-action="cancel" data-id="' + esc(r.id) + '">Cancel reservation</button>' : '') +
                '</div>' +
                (canEdit
                    ? '<form class="staff-form section-gap" data-form="guest" data-id="' + esc(r.id) + '" aria-labelledby="eg-title" novalidate hidden>' +
                        '<h4 id="eg-title" class="label-heading">Edit guest details</h4>' +
                        '<div class="grid-2">' +
                            '<div class="field"><label for="eg-first">First name</label>' +
                                '<input type="text" id="eg-first" name="firstName" value="' + esc(g.firstName) + '" required></div>' +
                            '<div class="field"><label for="eg-last">Last name</label>' +
                                '<input type="text" id="eg-last" name="lastName" value="' + esc(g.lastName) + '" required></div>' +
                            '<div class="field"><label for="eg-phone">Phone</label>' +
                                '<input type="tel" id="eg-phone" name="phone" value="' + esc(g.phone) + '" required></div>' +
                            '<div class="field"><label for="eg-guests">Guests</label>' +
                                '<select id="eg-guests" name="guests">' + guestOptions + '</select></div>' +
                        '</div>' +
                        '<div class="field"><label for="eg-requests">Requests <span class="optional">Optional</span></label>' +
                            '<textarea id="eg-requests" name="requests" rows="2">' + esc(g.requests) + '</textarea></div>' +
                        '<p class="fine-print">Email, dates, and room stay as booked. To change those, cancel and rebook.</p>' +
                        '<div class="inline-actions">' +
                            '<button type="submit" class="btn">Save changes</button>' +
                            '<button type="button" class="btn-text" data-action="close-edit">Discard</button>' +
                        '</div>' +
                        '<p class="notice" role="alert" hidden></p>' +
                    '</form>'
                    : '') +
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

    async function saveGuestDetails(form) {
        const message = $('.notice', form);
        showNotice(message, '');
        const ok = validate([
            [form.firstName, required('Enter a first name.')],
            [form.lastName, required('Enter a last name.')],
            [form.phone, isPhone]
        ]);
        if (!ok) return;
        try {
            await api.updateGuestDetails(form.dataset.id, {
                firstName: form.firstName.value,
                lastName: form.lastName.value,
                phone: form.phone.value,
                requests: form.requests.value,
                guests: form.guests.value
            });
        } catch (err) {
            showNotice(message, err.message);
            return;
        }
        await renderReservationsPanel(form.dataset.id);
        $('#res-detail h3').focus();
    }

    // Filtering and paging happen on the server. Typing in the search box can start
    // several requests; only the newest one is drawn.
    let resPage = 1;
    let resRequest = 0;

    async function renderReservationTable() {
        const f = $('#res-filters');
        const pager = $('#res-pager');
        const ticket = ++resRequest;
        let result;
        try {
            result = await api.getReservations({ status: f.status.value, payment: f.payment.value, q: f.q.value.trim(), page: resPage });
        } catch (err) {
            if (ticket !== resRequest) return;
            $('#res-count').textContent = '';
            $('#res-table').innerHTML = '<p class="notice">' + esc(err.message) + '</p>';
            pager.hidden = true;
            return;
        }
        if (ticket !== resRequest) return;

        const list = result.items;
        const pages = Math.max(1, Math.ceil(result.total / result.pageSize));
        resPage = result.page;
        const first = (result.page - 1) * result.pageSize + 1;
        $('#res-count').textContent = pages > 1
            ? 'Showing ' + first + '–' + (first + list.length - 1) + ' of ' + plural(result.total, 'reservation')
            : plural(result.total, 'reservation');
        pager.hidden = pages <= 1;
        $('#res-page').textContent = 'Page ' + result.page + ' of ' + pages;
        $('[data-page="prev"]', pager).disabled = result.page <= 1;
        $('[data-page="next"]', pager).disabled = result.page >= pages;
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
        let groups;
        try {
            groups = await api.searchAvailability({
                checkin: nf.checkin.value, checkout: nf.checkout.value, guests: nf.guests.value, allowPast: true
            });
        } catch (err) {
            select.innerHTML = '';
            $('#sn-quote').textContent = err.message;
            return;
        }
        const open = groups
            .flatMap((g) => g.rooms.map((r) => ({ number: r.number, floor: r.floor, type: g.type })))
            .sort((a, b) => a.number - b.number);
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
        const types = Object.keys(rates);
        // A weeknight and a weekend field for each room type.
        $('#rate-fields').innerHTML = types.map((type) => ['weekday', 'weekend'].map((night) => {
            const id = 'rt-' + type + '-' + night;
            const name = ROOM_TYPES[type] ? ROOM_TYPES[type].name : type;
            return '<div class="field">' +
                '<label for="' + id + '">' + esc(name) + ', ' + (night === 'weekday' ? 'weeknight' : 'weekend night') + ' ($)</label>' +
                '<input type="number" id="' + id + '" data-type="' + esc(type) + '" data-night="' + night + '" min="1" step="1" required value="' +
                    esc(rates[type][night]) + '">' +
            '</div>';
        }).join('')).join('');
        $('#rates-message').textContent = '';

        const t = todayISO();
        const upcoming = overrides.filter((o) => o.date >= t);
        $('#overrides-list').innerHTML = upcoming.length
            ? '<ul class="line-list">' + upcoming.map((o) =>
                '<li><div><strong>' + esc(fmtDate(o.date)) + '</strong> · ' + money(o.amount) + '<p class="fine-print">' + esc(o.label) + '</p></div>' +
                '<button type="button" class="btn-text" data-action="remove-override" data-date="' + esc(o.date) + '">Remove<span class="visually-hidden"> special rate for ' + esc(fmtDate(o.date)) + '</span></button></li>').join('') + '</ul>'
            : '<p class="empty">No special date rates.</p>';

        const preview = await api.getRatePreview(t, 14);
        const typeName = (type) => (ROOM_TYPES[type] ? ROOM_TYPES[type].name.replace(/^The /, '') : type);
        const kindLabel = (n) => (n.kind === 'special' ? n.label : n.kind === 'weekend' ? 'Weekend' : 'Weeknight');
        $('#rate-preview').innerHTML =
            '<table class="data-table">' +
                '<caption class="visually-hidden">Nightly rates for the next 14 nights</caption>' +
                '<thead><tr><th scope="col">Night</th><th scope="col">Rate type</th>' +
                    types.map((type) => '<th scope="col">' + esc(typeName(type)) + '</th>').join('') + '</tr></thead>' +
                '<tbody>' + preview.map((p) =>
                    '<tr><td data-label="Night">' + esc(fmtDate(p.date, 'day')) + '</td>' +
                    '<td data-label="Rate type">' + esc(kindLabel(p.types[types[0]])) + '</td>' +
                    types.map((type) => '<td data-label="' + esc(typeName(type)) + '">' + money(p.types[type].amount) + '</td>').join('') +
                    '</tr>').join('') +
                '</tbody>' +
            '</table>';
    }

    /* Employees panel */

    // Monday of the schedule week on screen; null shows this week.
    let schedWeek = null;

    async function renderEmployeesPanel() {
        const [employees] = await Promise.all([api.getEmployees(), renderSchedule()]);
        const name = (p) => p.firstName + ' ' + p.lastName;
        const manager = isManager();
        $('#emp-status').textContent = plural(employees.filter((p) => p.active).length, 'active employee');
        $('#emp-table').innerHTML =
            '<table class="data-table">' +
                '<caption class="visually-hidden">Staff accounts</caption>' +
                '<thead><tr><th scope="col">Name</th><th scope="col">Email</th><th scope="col">Role</th><th scope="col">Last signed in</th>' +
                '<th scope="col">Status</th>' + (manager ? '<th scope="col"><span class="visually-hidden">Actions</span></th>' : '') + '</tr></thead>' +
                '<tbody>' + employees.map((p) => {
                    const self = p.id === staffUser.id;
                    const button = (action, next, label) =>
                        '<button type="button" class="btn-text" data-action="' + action + '" data-id="' + p.id + '"' +
                        ' data-name="' + esc(name(p)) + '" data-next="' + next + '">' + label +
                        '<span class="visually-hidden"> ' + esc(name(p)) + '</span></button>';
                    return '<tr>' +
                        '<td data-label="Name">' + esc(name(p)) + (self ? ' <span class="fine-print">(you)</span>' : '') + '</td>' +
                        '<td data-label="Email">' + esc(p.email) + '</td>' +
                        '<td data-label="Role">' + (p.role === 'manager' ? 'Manager' : 'Staff') + '</td>' +
                        '<td data-label="Last signed in">' + (p.lastSignedInAt ? esc(fmtTimestamp(p.lastSignedInAt)) : 'Never') + '</td>' +
                        '<td data-label="Status">' + (p.active ? '<span class="badge">Active</span>' : '<span class="badge badge--muted">Inactive</span>') + '</td>' +
                        (manager ? '<td data-label="Actions">' + (self ? '' :
                            '<span class="inline-actions inline-actions--tight">' +
                                button('toggle-role', p.role === 'manager' ? 'staff' : 'manager', p.role === 'manager' ? 'Make staff' : 'Make manager') +
                                button('toggle-employee', p.active ? 'inactive' : 'active', p.active ? 'Deactivate' : 'Reactivate') +
                            '</span>') + '</td>' : '') +
                    '</tr>';
                }).join('') +
                '</tbody>' +
            '</table>';

        if (!manager) return;
        const fill = (select, people, empty, suffix) => {
            const previous = select.value;
            select.innerHTML = people.length
                ? people.map((p) => '<option value="' + p.id + '">' + esc(name(p)) + (suffix ? suffix(p) : '') + '</option>').join('')
                : '<option value="">' + empty + '</option>';
            if (people.some((p) => String(p.id) === previous)) select.value = previous;
        };
        fill($('#er-employee'), employees.filter((p) => p.id !== staffUser.id), 'No other employees yet', (p) => (p.active ? '' : ' (inactive)'));
        fill($('#sh-employee'), employees.filter((p) => p.active), 'No active employees');
        if (!$('#sh-date').value) $('#sh-date').value = todayISO();
    }

    // One week of shifts, Monday to Sunday, as a list per day.
    async function renderSchedule() {
        if (!schedWeek) schedWeek = weekStart(todayISO());
        const week = schedWeek;
        const shifts = await api.getShifts(week, 7);
        if (week !== schedWeek) return;   // a newer week was asked for meanwhile

        const end = addDays(week, 6);
        $('#sched-week').textContent = (week === weekStart(todayISO()) ? 'This week: ' : 'Week of ') +
            fmtDate(week, 'short') + ' – ' + fmtDate(end, 'short') + ' · ' + plural(shifts.length, 'shift');
        const today = todayISO();
        $('#sched-days').innerHTML = Array.from({ length: 7 }, (_, i) => addDays(week, i)).map((day) => {
            const items = shifts.filter((s) => s.date === day);
            return '<h4 class="label-heading section-gap">' + esc(fmtDate(day, 'day')) + (day === today ? ' · Today' : '') + '</h4>' +
                (items.length
                    ? '<ul class="line-list">' + items.map((s) => {
                        const hours = fmtTime(s.start) + ' – ' + fmtTime(s.end) + (s.overnight ? ' (next day)' : '');
                        const who = s.employeeName + (s.employeeId === staffUser.id ? ' (you)' : '') + (s.employeeActive ? '' : ' (inactive)');
                        return '<li><div><strong>' + esc(hours) + '</strong> · ' + esc(who) +
                            (s.note ? '<p class="fine-print">' + esc(s.note) + '</p>' : '') + '</div>' +
                            (isManager()
                                ? '<button type="button" class="btn-text" data-action="remove-shift" data-id="' + esc(s.id) + '"' +
                                  ' data-label="' + esc(s.employeeName + '’s shift on ' + fmtDate(day, 'day') + ', ' + hours) + '">Remove' +
                                  '<span class="visually-hidden"> ' + esc(s.employeeName + '’s shift, ' + hours) + '</span></button>'
                                : '') +
                        '</li>';
                    }).join('') + '</ul>'
                    : '<p class="empty">No one scheduled.</p>');
        }).join('');
    }

    /* ---------- Druid AI Assistant (concierge widget) ---------- */

    // Shown on every view. States: 'intro' (welcome card), 'open' (chat panel),
    // 'min' (launcher bubble). Minimizing is remembered for the browser session.
    const CONCIERGE_STATE_KEY = 'druid-concierge-state';
    const CONCIERGE_WINDOW = 10;  // most recent messages sent to the server as context

    // Full conversation for display; only the last CONCIERGE_WINDOW messages are sent.
    const conciergeChat = [];
    let conciergeBusy = false;

    function conciergePage() {
        const name = parseHash().name;
        if (name === 'staff') return 'staff';
        if (name === 'search' || name === 'booking' || name === 'confirmation' || name === 'pay') return 'booking';
        return 'main';
    }

    function readConciergeState() {
        try { return sessionStorage.getItem(CONCIERGE_STATE_KEY); } catch (e) { return null; }
    }

    function rememberMinimized() {
        try { sessionStorage.setItem(CONCIERGE_STATE_KEY, 'min'); } catch (e) { /* not remembered */ }
    }

    function setConciergeState(state, moveFocus) {
        const root = $('[data-concierge]');
        root.dataset.state = state;
        $('[data-concierge-intro]', root).hidden = state !== 'intro';
        $('[data-concierge-panel]', root).hidden = state !== 'open';
        const launcher = $('[data-concierge-launcher]', root);
        launcher.hidden = state !== 'min';
        launcher.setAttribute('aria-expanded', String(state === 'open'));

        if (!moveFocus) return;
        if (state === 'open') {
            $('#concierge-input').focus();
            scrollConciergeLog();
        } else if (state === 'min') {
            launcher.focus();
        }
    }

    function scrollConciergeLog() {
        const log = $('[data-concierge-log]');
        log.scrollTop = log.scrollHeight;
    }

    function addConciergeBubble(role, text) {
        const li = document.createElement('li');
        li.className = 'concierge__msg concierge__msg--' + role;
        const who = document.createElement('span');
        who.className = 'visually-hidden';
        who.textContent = role === 'user' ? 'You: ' : 'Druid AI Assistant: ';
        const body = document.createElement('p');
        body.className = 'concierge__text';
        body.textContent = text;
        li.append(who, body);
        $('[data-concierge-log]').append(li);
        scrollConciergeLog();
        return body;
    }

    // Adds an assistant line that wasn't streamed from the server (for example after the
    // guest signs in), and keeps it in the history so the assistant knows it was said.
    function addConciergeNote(text) {
        conciergeChat.push({ role: 'assistant', content: text });
        addConciergeBubble('assistant', text);
    }

    // A card number (13 to 19 digits, spaces or dashes allowed) that passes the Luhn check.
    // The chat goes to an AI service, so card numbers are stopped before they're sent.
    function hasCardNumber(text) {
        return (String(text).match(/\d(?:[ -]?\d){12,18}/g) || []).some((m) => luhn(m.replace(/\D/g, '')));
    }

    async function sendConciergeMessage(text) {
        conciergeBusy = true;
        const form = $('[data-concierge-form]');
        const sendButton = $('button[type="submit"]', form);
        const typing = $('[data-concierge-typing]');
        sendButton.disabled = true;

        conciergeChat.push({ role: 'user', content: text });
        addConciergeBubble('user', text);
        typing.hidden = false;
        scrollConciergeLog();

        let bubble = null;
        let reply = '';
        // Cards from this reply, shown after its text: the latest booking offer, and
        // whether booking needs the guest to sign in first.
        let proposal = null;
        let signIn = false;
        try {
            await api.askConcierge(
                conciergeChat.slice(-CONCIERGE_WINDOW),
                conciergePage(),
                (delta) => {
                    reply += delta;
                    if (!bubble) {
                        typing.hidden = true;
                        bubble = addConciergeBubble('assistant', '');
                        bubble.parentNode.setAttribute('aria-busy', 'true');
                    }
                    bubble.textContent = reply;
                    scrollConciergeLog();
                },
                () => {
                    reply = '';
                    if (bubble) bubble.textContent = '';
                },
                (event) => {
                    if (event.type === 'proposal' && event.proposal) proposal = event.proposal;
                    else if (event.type === 'sign_in') signIn = true;
                }
            );
            if (!reply.trim()) throw new Error(CONCIERGE_OFFLINE);
            conciergeChat.push({ role: 'assistant', content: reply });
        } catch (err) {
            const message = err.message || CONCIERGE_OFFLINE;
            if (!bubble) bubble = addConciergeBubble('assistant', '');
            bubble.textContent = message;
            bubble.parentNode.classList.add('concierge__msg--error');
        } finally {
            typing.hidden = true;
            if (bubble) bubble.parentNode.removeAttribute('aria-busy');
            if (proposal) addProposalCard(proposal);
            if (signIn) addSignInCard();
            sendButton.disabled = false;
            conciergeBusy = false;
            scrollConciergeLog();
        }
    }

    /* Booking cards. The assistant only proposes; the guest's own press of Confirm
       booking books the room (POST api/concierge/bookings with the card's token). */

    // Open offers: card element -> { proposal, timer }.
    const conciergeOffers = new Map();

    function addConciergeCard(html) {
        const li = document.createElement('li');
        li.className = 'concierge__msg concierge__msg--card';
        li.innerHTML = '<span class="visually-hidden">Druid AI Assistant: </span><div class="concierge-card">' + html + '</div>';
        $('[data-concierge-log]').append(li);
        scrollConciergeLog();
        return $('.concierge-card', li);
    }

    function addProposalCard(p) {
        // One open offer at a time, so one press books one room.
        Array.from(conciergeOffers.keys()).forEach((card) => closeOffer(card, 'Replaced by the newer offer below.'));
        const card = addConciergeCard(
            '<p class="concierge-card__title">Ready to book</p>' +
            '<dl class="concierge-card__list">' +
                '<div><dt>Room</dt><dd>' + esc(p.typeName) + ', room ' + p.room + ' · ' + esc(FLOOR_NAMES[p.floor] || '') + '</dd></div>' +
                '<div><dt>Arrive</dt><dd>' + esc(fmtDate(p.checkin)) + ', from 3 PM</dd></div>' +
                '<div><dt>Depart</dt><dd>' + esc(fmtDate(p.checkout)) + ', by 11 AM</dd></div>' +
                '<div><dt>Guests</dt><dd>' + esc(plural(p.guests, 'guest')) + ' · ' + esc(p.guestName) + '</dd></div>' +
                (p.requests ? '<div><dt>Requests</dt><dd>' + esc(p.requests) + '</dd></div>' : '') +
                '<div><dt>Total</dt><dd><strong>' + money(p.total) + '</strong> · ' + esc(nightBreakdown(p.nights)) + '</dd></div>' +
            '</dl>' +
            '<p class="concierge-card__fine">Before taxes. Pay at check-in, or pay by card once it’s booked. This offer is good for 10 minutes.</p>' +
            '<div class="concierge-card__actions"><button type="button" class="btn" data-confirm-booking>Confirm booking</button></div>' +
            '<p class="concierge-card__status" data-card-status tabindex="-1"></p>');
        const timer = setTimeout(() => closeOffer(card, 'This offer has expired. Ask me again and I’ll recheck the room and price.'),
            (Number(p.expiresInSeconds) || 600) * 1000);
        conciergeOffers.set(card, { proposal: p, timer });
    }

    // Ends an offer: no more Confirm button, and a note saying why.
    function closeOffer(card, message) {
        const offer = conciergeOffers.get(card);
        if (offer) clearTimeout(offer.timer);
        conciergeOffers.delete(card);
        const button = $('[data-confirm-booking]', card);
        const hadFocus = button && document.activeElement === button;
        if (button) button.closest('.concierge-card__actions').remove();
        card.classList.add('concierge-card--closed');
        const status = $('[data-card-status]', card);
        status.textContent = message;
        if (hadFocus) status.focus();
    }

    async function confirmOffer(card) {
        const offer = conciergeOffers.get(card);
        if (!offer) return;
        const button = $('[data-confirm-booking]', card);
        const status = $('[data-card-status]', card);
        button.disabled = true;
        status.textContent = 'Booking…';
        let r;
        try {
            r = await api.confirmConciergeBooking(offer.proposal.token);
        } catch (err) {
            if (err.status === 401) {
                closeOffer(card, 'Your sign-in has ended. Sign in again, then ask me to set it up again.');
                addSignInCard();
            } else if (!err.status || err.status === 429 || err.status >= 500) {
                // Offline or busy: the offer still stands, so let them try again.
                status.textContent = err.message;
                button.disabled = false;
            } else {
                // Taken, repriced, or expired. A fresh card follows when one is available.
                closeOffer(card, err.message);
                if (err.data && err.data.proposal) addProposalCard(err.data.proposal);
            }
            return;
        }

        clearTimeout(offer.timer);
        conciergeOffers.delete(card);
        lastBooking = r;
        const t = ROOM_TYPES[r.type];
        card.classList.add('concierge-card--booked');
        $('.concierge-card__title', card).textContent = 'Booked';
        $('.concierge-card__fine', card).textContent = 'Before taxes. Due at check-in, or pay by card now.';
        // Pay now replaces Confirm booking; it opens the payment page for this stay.
        button.closest('.concierge-card__actions').innerHTML =
            '<a class="btn" href="#/pay?id=' + encodeURIComponent(r.id) + '">Pay now</a>';
        status.innerHTML = 'You’re booked. Confirmation <strong>' + esc(r.id) + '</strong>. ' +
            '<a href="#/confirmation?id=' + encodeURIComponent(r.id) + '">See your confirmation</a> · <a href="#/account">Your stays</a>';
        status.focus();
        // Part of the history the assistant sees, so later replies know the booking is made.
        conciergeChat.push({
            role: 'assistant',
            content: 'You’re booked: confirmation ' + r.id + ', ' + (t ? t.name : r.type) + ' room ' + r.room + ', ' +
                stayLine(r.checkin, r.checkout) + ', ' + money(r.total) + ' before taxes, due at check-in unless you pay now with the Pay now button.'
        });
    }

    // Booking needs a guest account: a button to sign in, which brings the guest back here.
    function addSignInCard() {
        conciergeAwaitingSignIn = true;
        const next = /^#\/(?!account\b)[a-z]+/.test(location.hash) ? location.hash : '';
        addConciergeCard(
            '<p>Booking needs a hotel account, so only you can see your stays.</p>' +
            '<div class="concierge-card__actions"><a class="btn" data-concierge-sign-in href="#/account' +
                (next ? '?' + esc(new URLSearchParams({ next }).toString()) : '') + '">Sign in or create an account</a></div>' +
            '<p class="concierge-card__fine">This chat stays right here while you do.</p>');
    }

    function initConcierge() {
        const root = $('[data-concierge]');
        if (!root) return;

        $$('[data-concierge-open]', root).forEach((b) => b.addEventListener('click', () => setConciergeState('open', true)));
        $$('[data-concierge-minimize]', root).forEach((b) => b.addEventListener('click', () => {
            rememberMinimized();
            setConciergeState('min', true);
        }));

        root.addEventListener('keydown', (e) => {
            if (e.key === 'Escape' && root.dataset.state === 'open') {
                rememberMinimized();
                setConciergeState('min', true);
            }
        });

        const log = $('[data-concierge-log]', root);
        log.addEventListener('click', (e) => {
            const confirm = e.target.closest('[data-confirm-booking]');
            if (confirm) confirmOffer(confirm.closest('.concierge-card'));
            // On a phone the chat covers the page, so tuck it away when a card link opens one
            // (sign in, see your confirmation, your stays). The conversation is kept.
            if (e.target.closest('.concierge-card a') && !window.matchMedia('(min-width: 36rem)').matches) {
                setConciergeState('min', false);
            }
        });

        const form = $('[data-concierge-form]', root);
        const input = $('#concierge-input');
        form.addEventListener('submit', (e) => {
            e.preventDefault();
            const text = input.value.trim();
            if (!text) {
                setFieldError(input, 'Type a question for the assistant.');
                input.focus();
                return;
            }
            if (hasCardNumber(text)) {
                setFieldError(input, 'For your security, please don’t type card numbers in the chat. To pay by card, use Pay now once your room is booked.');
                input.focus();
                return;
            }
            if (conciergeBusy) return;
            setFieldError(input, '');
            input.value = '';
            sendConciergeMessage(text);
        });

        // Enter sends; Shift+Enter adds a new line.
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && !e.shiftKey && !e.isComposing) {
                e.preventDefault();
                form.requestSubmit();
            }
        });

        // The welcome card must not sit over the page while someone is using it:
        // tuck it into the launcher once they scroll or focus anything outside the
        // widget. This is not remembered; only an explicit minimize is.
        const tuckIntro = (e) => {
            if (root.dataset.state !== 'intro') return;
            if (e.type === 'focusin' && root.contains(e.target)) return;
            if (e.type === 'scroll' && window.scrollY < 80) return;
            setConciergeState('min', false);
        };
        window.addEventListener('scroll', tuckIntro, { passive: true });
        document.addEventListener('focusin', tuckIntro);

        setConciergeState(readConciergeState() === 'min' ? 'min' : 'intro', false);
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

        // Set up the concierge before room types load, so it still appears if that call fails.
        initConcierge();

        // Whether a guest is signed in, for the menu's "Sign in" / "Your stays" link.
        ensureGuest();

        // Room types live on the server; every view needs them, so load them before routing.
        api.getRoomTypes().then((types) => {
            ROOM_TYPES = types;
            window.addEventListener('hashchange', router);
            router();
        }).catch((err) => {
            showNotice($('#api-offline'), err.message);
        });
    }

    init();
})();
