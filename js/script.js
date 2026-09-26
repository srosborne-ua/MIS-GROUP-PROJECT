/* ==========================================================================
   The Rogers Hotel — Sprint 1 front-end prototype
   Handles the search forms, room filtering, and the room details page.
   All data below is example data. There is no backend in this sprint.
   ========================================================================== */

(function () {
    'use strict';

    /* ----------------------------------------------------------------------
       Example data
       ---------------------------------------------------------------------- */

    // Example default nightly rates. A future staff portal will manage these.
    const RATES = { weekday: 100, weekend: 150 };

    const ROOM_TYPES = {
        'standard-king': 'Standard King',
        'standard-queen': 'Standard Queen',
        'double-queen': 'Double Queen',
        'twin': 'Twin Room',
        'deluxe-king': 'Deluxe King',
        'master-suite': 'Master Suite'
    };

    const STANDARD_AMENITIES = [
        'Free Wi-Fi',
        'Air conditioning',
        'Flat-screen TV',
        'Work desk',
        'Coffee maker',
        'Mini fridge',
        'Blackout curtains',
        'Private bathroom'
    ];

    const SUITE_AMENITIES = STANDARD_AMENITIES.concat([
        'Separate living area',
        'Soaking tub and walk-in shower'
    ]);

    const SUITE_FEATURES = [
        'Separate living room with sofa and lounge chairs',
        'Dining table that doubles as a meeting space',
        'Pull-out sofa bed for additional guests',
        'Oversized bathroom with soaking tub',
        'Upgraded bath amenities and robes',
        'Third-floor location away from the lobby'
    ];

    const ROOMS = [
        {
            id: 'standard-king-1',
            type: 'standard-king',
            name: 'Standard King',
            bed: 'king',
            floor: 1,
            maxGuests: 2,
            size: '320 sq ft',
            sleeping: 'One king bed. Sleeps up to 2 guests.',
            summary: 'A calm, well-appointed room with a king bed and a dedicated work area.',
            description: 'Our Standard King offers a comfortable king bed, a work desk with easy-to-reach outlets, ' +
                'and a quiet setting for a restful night. A good fit for solo travelers and couples.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'standard-queen-1',
            type: 'standard-queen',
            name: 'Standard Queen',
            bed: 'queen',
            floor: 1,
            maxGuests: 2,
            size: '300 sq ft',
            sleeping: 'One queen bed. Sleeps up to 2 guests.',
            summary: 'A comfortable queen room with convenient first-floor access.',
            description: 'The Standard Queen is a practical, comfortable room with a queen bed and everything you ' +
                'need for a short stay. First-floor rooms offer quick access to the lobby and parking.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'twin-1',
            type: 'twin',
            name: 'Twin Room',
            bed: 'twin',
            floor: 1,
            maxGuests: 2,
            size: '300 sq ft',
            sleeping: 'Two twin beds. Sleeps up to 2 guests.',
            summary: 'Two twin beds, ideal for colleagues or friends traveling together.',
            description: 'The Twin Room provides two separate twin beds, making it a comfortable choice for ' +
                'colleagues, friends, or family members who prefer their own bed.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'standard-king-2',
            type: 'standard-king',
            name: 'Standard King',
            bed: 'king',
            floor: 2,
            maxGuests: 2,
            size: '320 sq ft',
            sleeping: 'One king bed. Sleeps up to 2 guests.',
            summary: 'Our standard king layout on the quieter second floor.',
            description: 'This second-floor Standard King combines a comfortable king bed with a work desk and ' +
                'seating area, set away from the lobby for a quieter stay.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'double-queen-2',
            type: 'double-queen',
            name: 'Double Queen',
            bed: 'queen',
            floor: 2,
            maxGuests: 4,
            size: '360 sq ft',
            sleeping: 'Two queen beds. Sleeps up to 4 guests.',
            summary: 'Two queen beds with extra room for families and small groups.',
            description: 'The Double Queen features two queen beds and additional floor space, making it well ' +
                'suited to families and small groups visiting Tuscaloosa together.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'twin-2',
            type: 'twin',
            name: 'Twin Room',
            bed: 'twin',
            floor: 2,
            maxGuests: 2,
            size: '300 sq ft',
            sleeping: 'Two twin beds. Sleeps up to 2 guests.',
            summary: 'Two twin beds on the second floor, a short walk from the elevator.',
            description: 'This second-floor Twin Room offers two comfortable twin beds, a shared work desk, ' +
                'and plenty of storage for a longer stay.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'deluxe-king-3',
            type: 'deluxe-king',
            name: 'Deluxe King',
            bed: 'king',
            floor: 3,
            maxGuests: 2,
            size: '380 sq ft',
            sleeping: 'One king bed. Sleeps up to 2 guests.',
            summary: 'A larger king room on the top floor with a lounge chair and reading area.',
            description: 'The Deluxe King adds extra space to our king layout, with a lounge chair, reading lamp, ' +
                'and a larger bathroom. Its third-floor location keeps things quiet.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'standard-queen-3',
            type: 'standard-queen',
            name: 'Standard Queen',
            bed: 'queen',
            floor: 3,
            maxGuests: 2,
            size: '300 sq ft',
            sleeping: 'One queen bed. Sleeps up to 2 guests.',
            summary: 'A comfortable queen room on the quiet top floor.',
            description: 'This third-floor Standard Queen offers a queen bed, a work desk, and a restful ' +
                'setting away from the lobby.',
            amenities: STANDARD_AMENITIES
        },
        {
            id: 'master-suite-north',
            type: 'master-suite',
            name: 'North Master Suite',
            suite: true,
            bed: 'king',
            floor: 3,
            maxGuests: 4,
            size: '650 sq ft',
            sleeping: 'One king bed in a private bedroom, plus a queen sofa bed in the living room. Sleeps up to 4 guests.',
            summary: 'One of two master suites, with a private bedroom and separate living area.',
            description: 'The North Master Suite is one of two suites at The Rogers Hotel. A private bedroom with a ' +
                'king bed opens onto a separate living room with seating and a dining table, making it well suited ' +
                'to longer stays, family visits, and special occasions.',
            amenities: SUITE_AMENITIES
        },
        {
            id: 'master-suite-south',
            type: 'master-suite',
            name: 'South Master Suite',
            suite: true,
            bed: 'king',
            floor: 3,
            maxGuests: 4,
            size: '650 sq ft',
            sleeping: 'One king bed in a private bedroom, plus a queen sofa bed in the living room. Sleeps up to 4 guests.',
            summary: 'Our second master suite, with generous living space and an oversized bath.',
            description: 'The South Master Suite pairs a private king bedroom with a comfortable living room and ' +
                'an oversized bathroom with a soaking tub. A quiet, spacious option for extended or celebratory stays.',
            amenities: SUITE_AMENITIES
        }
    ];

    /* ----------------------------------------------------------------------
       Helpers
       ---------------------------------------------------------------------- */

    const params = new URLSearchParams(window.location.search);

    function byId(id) {
        return document.getElementById(id);
    }

    // Parse a YYYY-MM-DD string as a local date (avoids time zone shifts).
    function parseDate(value) {
        if (!value || !/^\d{4}-\d{2}-\d{2}$/.test(value)) {
            return null;
        }
        const parts = value.split('-').map(Number);
        const date = new Date(parts[0], parts[1] - 1, parts[2]);
        return isNaN(date.getTime()) ? null : date;
    }

    function toInputValue(date) {
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return date.getFullYear() + '-' + month + '-' + day;
    }

    function formatDate(value) {
        const date = parseDate(value);
        if (!date) {
            return 'Not selected';
        }
        return date.toLocaleDateString('en-US', { month: 'long', day: 'numeric', year: 'numeric' });
    }

    function formatGuests(value) {
        const count = parseInt(value, 10);
        if (!count) {
            return 'Not selected';
        }
        return count + (count === 1 ? ' guest' : ' guests');
    }

    function floorLabel(floor) {
        return { 1: '1st Floor', 2: '2nd Floor', 3: '3rd Floor' }[floor] || 'Floor ' + floor;
    }

    function bedLabel(bed) {
        return bed.charAt(0).toUpperCase() + bed.slice(1);
    }

    // Count weekday and weekend nights between two dates.
    // A Friday or Saturday night is charged the weekend rate.
    function getStay(checkinValue, checkoutValue) {
        const checkin = parseDate(checkinValue);
        const checkout = parseDate(checkoutValue);
        if (!checkin || !checkout || checkout <= checkin) {
            return null;
        }
        let weekday = 0;
        let weekend = 0;
        const night = new Date(checkin);
        while (night < checkout) {
            const day = night.getDay();
            if (day === 5 || day === 6) {
                weekend++;
            } else {
                weekday++;
            }
            night.setDate(night.getDate() + 1);
        }
        return {
            nights: weekday + weekend,
            weekday: weekday,
            weekend: weekend,
            total: weekday * RATES.weekday + weekend * RATES.weekend
        };
    }

    // Keep only the search values worth carrying between pages.
    function searchQuery(source, keys) {
        const out = new URLSearchParams();
        (keys || ['checkin', 'checkout', 'guests', 'type', 'bed', 'floor']).forEach(function (key) {
            source.getAll(key).forEach(function (value) {
                if (value && value !== 'any') {
                    out.append(key, value);
                }
            });
        });
        return out.toString();
    }

    function withQuery(page, query) {
        return query ? page + '?' + query : page;
    }

    /* ----------------------------------------------------------------------
       Search forms (homepage + rooms page)
       ---------------------------------------------------------------------- */

    function setFieldError(input, hasError) {
        input.classList.toggle('is-invalid', hasError);
        input.setAttribute('aria-invalid', hasError ? 'true' : 'false');
    }

    // Returns true when the form's dates are acceptable.
    function validateDates(form) {
        const checkin = form.querySelector('.js-checkin');
        const checkout = form.querySelector('.js-checkout');
        if (!checkin || !checkout) {
            return true;
        }
        const inDate = parseDate(checkin.value);
        const outDate = parseDate(checkout.value);

        const checkinMissing = checkin.required && !inDate;
        const checkoutBad = (checkout.required && !outDate) || (inDate && outDate && outDate <= inDate);

        setFieldError(checkin, checkinMissing);
        setFieldError(checkout, Boolean(checkoutBad));

        if (checkinMissing) {
            checkin.focus();
        } else if (checkoutBad) {
            checkout.focus();
        }
        return !checkinMissing && !checkoutBad;
    }

    function initSearchForm(form) {
        const checkin = form.querySelector('.js-checkin');
        const checkout = form.querySelector('.js-checkout');
        const today = toInputValue(new Date());

        // Pre-fill fields from the current URL (e.g. after searching from the homepage).
        Array.prototype.forEach.call(form.elements, function (field) {
            if (field.name && params.has(field.name)) {
                const value = params.get(field.name);
                if (field.tagName === 'SELECT') {
                    if (field.querySelector('option[value="' + CSS.escape(value) + '"]')) {
                        field.value = value;
                    }
                } else {
                    field.value = value;
                }
            }
        });

        if (checkin && checkout) {
            checkin.min = today;
            checkout.min = checkin.value || today;

            checkin.addEventListener('change', function () {
                setFieldError(checkin, false);
                checkout.min = checkin.value || today;
                // Suggest a one-night stay if check-out is empty or no longer valid.
                const inDate = parseDate(checkin.value);
                const outDate = parseDate(checkout.value);
                if (inDate && (!outDate || outDate <= inDate)) {
                    const next = new Date(inDate);
                    next.setDate(next.getDate() + 1);
                    checkout.value = toInputValue(next);
                }
            });

            checkout.addEventListener('change', function () {
                setFieldError(checkout, false);
            });
        }

        form.addEventListener('submit', function (event) {
            if (!validateDates(form)) {
                event.preventDefault();
                return;
            }
            // On the rooms page, update results in place instead of reloading.
            if (form.dataset.live === 'true') {
                event.preventDefault();
                form.dispatchEvent(new CustomEvent('search:update'));
            }
        });
    }

    document.querySelectorAll('.js-search-form').forEach(initSearchForm);

    /* ----------------------------------------------------------------------
       Rooms page
       ---------------------------------------------------------------------- */

    function roomCard(room, detailQuery) {
        const detailUrl = withQuery('room-details.html', 'room=' + encodeURIComponent(room.id) +
            (detailQuery ? '&' + detailQuery : ''));
        const suiteLabel = room.suite ? '<span class="suite-label">Suite</span>' : '';
        const amenities = room.amenities.slice(0, 4).join(' &middot; ');

        return '' +
            '<div class="col-md-6 col-xl-4">' +
            '  <article class="card room-card h-100' + (room.suite ? ' room-card--suite' : '') + '">' +
            '    <div class="photo-placeholder" role="img" aria-label="Photo placeholder: ' + room.name + '">PHOTO</div>' +
            '    <div class="card-body d-flex flex-column">' +
            '      <p class="room-meta mb-1">' + bedLabel(room.bed) + ' bed &middot; ' + floorLabel(room.floor) +
            '        &middot; Sleeps ' + room.maxGuests + '</p>' +
            '      <h3 class="h5 card-title mb-2">' + room.name + suiteLabel + '</h3>' +
            '      <p class="small mb-2">' + room.summary + '</p>' +
            '      <p class="room-card-amenities mb-3">' + amenities + '</p>' +
            '      <div class="mt-auto border-top pt-3">' +
            '        <p class="mb-0"><span class="price">$' + RATES.weekday + '</span>' +
            '          <span class="small text-body-secondary">/ night</span></p>' +
            '        <p class="small text-body-secondary mb-1">$' + RATES.weekend + ' Fri &amp; Sat nights</p>' +
            '        <span class="rate-label">Example default rate</span>' +
            '        <div class="d-flex gap-2 mt-3">' +
            '          <a class="btn btn-outline-dark btn-sm flex-fill" href="' + detailUrl + '"' +
            '             aria-label="View ' + room.name + ', ' + floorLabel(room.floor) + '">View Room</a>' +
            '          <a class="btn btn-primary btn-sm flex-fill" href="' + detailUrl + '#reserve"' +
            '             aria-label="Select ' + room.name + ', ' + floorLabel(room.floor) + '">Select Room</a>' +
            '        </div>' +
            '      </div>' +
            '    </div>' +
            '  </article>' +
            '</div>';
    }

    function initRoomsPage() {
        const results = byId('roomResults');
        if (!results) {
            return;
        }

        const form = byId('searchBar');
        const typeSelect = byId('bar-type');
        const guestsSelect = byId('bar-guests');
        const bedBoxes = document.querySelectorAll('input[name="filter-bed"]');
        const floorBoxes = document.querySelectorAll('input[name="filter-floor"]');

        // Apply bed/floor choices from the homepage search.
        params.getAll('bed').forEach(function (bed) {
            bedBoxes.forEach(function (box) {
                if (box.value === bed) { box.checked = true; }
            });
        });
        params.getAll('floor').forEach(function (floor) {
            floorBoxes.forEach(function (box) {
                if (box.value === floor) { box.checked = true; }
            });
        });

        function checkedValues(boxes) {
            return Array.prototype.filter.call(boxes, function (box) { return box.checked; })
                .map(function (box) { return box.value; });
        }

        function currentState() {
            const state = new URLSearchParams();
            state.set('checkin', byId('bar-checkin').value);
            state.set('checkout', byId('bar-checkout').value);
            state.set('guests', guestsSelect.value);
            state.set('type', typeSelect.value);
            checkedValues(bedBoxes).forEach(function (bed) { state.append('bed', bed); });
            checkedValues(floorBoxes).forEach(function (floor) { state.append('floor', floor); });
            return state;
        }

        function updateSummary(state) {
            byId('summaryCheckin').textContent = formatDate(state.get('checkin'));
            byId('summaryCheckout').textContent = formatDate(state.get('checkout'));
            byId('summaryGuests').textContent = state.get('guests');
            const stay = getStay(state.get('checkin'), state.get('checkout'));
            byId('summaryNights').textContent = stay ? String(stay.nights) : '—';
        }

        function render() {
            const state = currentState();
            const type = state.get('type');
            const guests = parseInt(state.get('guests'), 10) || 1;
            const beds = state.getAll('bed');
            const floors = state.getAll('floor').map(Number);

            const matches = ROOMS.filter(function (room) {
                return (type === 'any' || room.type === type) &&
                    (beds.length === 0 || beds.indexOf(room.bed) !== -1) &&
                    (floors.length === 0 || floors.indexOf(room.floor) !== -1) &&
                    room.maxGuests >= guests;
            });

            // Keep the URL in sync so "Back to Rooms" returns to the same view.
            const query = searchQuery(state);
            window.history.replaceState(null, '', withQuery(window.location.pathname, query));

            const detailQuery = searchQuery(state);
            results.innerHTML = matches.map(function (room) { return roomCard(room, detailQuery); }).join('');
            byId('noResults').classList.toggle('d-none', matches.length > 0);
            byId('resultsCount').textContent = 'Showing ' + matches.length + ' of ' + ROOMS.length + ' room options';

            updateSummary(state);
        }

        function clearFilters() {
            typeSelect.value = 'any';
            bedBoxes.forEach(function (box) { box.checked = false; });
            floorBoxes.forEach(function (box) { box.checked = false; });
            render();
        }

        form.addEventListener('search:update', render);
        typeSelect.addEventListener('change', render);
        guestsSelect.addEventListener('change', render);
        bedBoxes.forEach(function (box) { box.addEventListener('change', render); });
        floorBoxes.forEach(function (box) { box.addEventListener('change', render); });
        byId('clearFilters').addEventListener('click', clearFilters);
        byId('clearFiltersEmpty').addEventListener('click', clearFilters);

        byId('editSearchButton').addEventListener('click', function () {
            form.scrollIntoView({ behavior: 'smooth', block: 'center' });
            byId('bar-checkin').focus({ preventScroll: true });
        });

        render();
    }

    /* ----------------------------------------------------------------------
       Room details page
       ---------------------------------------------------------------------- */

    function initRoomDetailsPage() {
        const detail = byId('roomDetail');
        if (!detail) {
            return;
        }

        // Links back to the room list keep the guest's search.
        const backQuery = searchQuery(params);
        document.querySelectorAll('.js-rooms-link').forEach(function (link) {
            link.href = withQuery('rooms.html', backQuery);
        });

        const room = ROOMS.find(function (r) { return r.id === params.get('room'); });
        if (!room) {
            detail.classList.add('d-none');
            byId('roomNotFound').classList.remove('d-none');
            byId('breadcrumbRoom').textContent = 'Room not found';
            return;
        }

        document.title = room.name + ' | The Rogers Hotel';
        detail.classList.toggle('is-suite', Boolean(room.suite));

        byId('breadcrumbRoom').textContent = room.name;
        byId('mainPhoto').setAttribute('aria-label', 'Photo placeholder: ' + room.name);
        byId('roomMeta').textContent = floorLabel(room.floor) + ' · ' + bedLabel(room.bed) + ' bed';
        byId('roomName').innerHTML = room.name + (room.suite ? '<span class="suite-label">Suite</span>' : '');
        byId('roomDescription').textContent = room.description;
        byId('roomSleeping').textContent = room.sleeping;
        byId('detailType').textContent = ROOM_TYPES[room.type];
        byId('detailBed').textContent = bedLabel(room.bed);
        byId('detailFloor').textContent = floorLabel(room.floor);
        byId('detailSize').textContent = room.size;
        byId('detailGuests').textContent = formatGuests(room.maxGuests).replace('guests', 'guests max');

        byId('roomAmenities').innerHTML = room.amenities.map(function (item) {
            return '<li><i class="bi bi-check2" aria-hidden="true"></i>' + item + '</li>';
        }).join('');

        if (room.suite) {
            byId('suiteFeatures').classList.remove('d-none');
            byId('suiteFeatureList').innerHTML = SUITE_FEATURES.map(function (item) {
                return '<li><i class="bi bi-star" aria-hidden="true"></i>' + item + '</li>';
            }).join('');
        }

        // Stay summary
        const checkin = params.get('checkin');
        const checkout = params.get('checkout');
        const guests = parseInt(params.get('guests'), 10);
        const stay = getStay(checkin, checkout);

        byId('stayCheckin').textContent = formatDate(checkin);
        byId('stayCheckout').textContent = formatDate(checkout);
        byId('stayGuests').textContent = formatGuests(guests);

        if (stay) {
            const lines = [];
            if (stay.weekday) {
                lines.push(['Weekday nights (' + stay.weekday + ' × $' + RATES.weekday + ')',
                    '$' + stay.weekday * RATES.weekday]);
            }
            if (stay.weekend) {
                lines.push(['Fri/Sat nights (' + stay.weekend + ' × $' + RATES.weekend + ')',
                    '$' + stay.weekend * RATES.weekend]);
            }
            byId('estimateLines').innerHTML = lines.map(function (line) {
                return '<div><dt>' + line[0] + '</dt><dd>' + line[1] + '</dd></div>';
            }).join('');
            byId('estimateTotal').textContent = '$' + stay.total;
            byId('stayEstimate').classList.remove('d-none');
        } else {
            byId('stayMissing').classList.remove('d-none');
        }

        if (guests && guests > room.maxGuests) {
            const note = byId('capacityNote');
            note.textContent = 'This room sleeps up to ' + room.maxGuests + ' guests. For ' + guests +
                ' guests, consider a Double Queen or a Master Suite.';
            note.classList.remove('d-none');
        }

        // "Change dates or guests" returns to the homepage search with values filled in.
        byId('changeDates').href = withQuery('index.html', searchQuery(params, ['checkin', 'checkout', 'guests'])) + '#search';

        // Reservation review modal
        const summaryRows = [
            ['Room', room.name],
            ['Floor', floorLabel(room.floor)],
            ['Check-in', formatDate(checkin)],
            ['Check-out', formatDate(checkout)],
            ['Guests', formatGuests(guests)],
            ['Estimated total', stay ? '$' + stay.total + ' (' + stay.nights + (stay.nights === 1 ? ' night)' : ' nights)') : '—']
        ];
        byId('reserveSummary').innerHTML = summaryRows.map(function (row) {
            return '<div><dt>' + row[0] + '</dt><dd>' + row[1] + '</dd></div>';
        }).join('');
    }

    initRoomsPage();
    initRoomDetailsPage();
})();
