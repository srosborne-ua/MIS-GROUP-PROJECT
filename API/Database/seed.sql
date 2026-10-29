-- Reference data for the Druid City Hotel: the hotel, its four room types, and
-- the 120 rooms. Values match the original in-memory HotelStore.
--
-- Run once by hand as an admin account after schema.sql. It fails (and changes
-- nothing it hasn't already inserted) if this data is already loaded.
--
-- Sample reservations, the sample hold, and the sample event block are not here:
-- their dates are relative to today, so the API writes them when staff click
-- "Reset sample data" (POST api/demo/reset), using druid_app.

USE mis321;

START TRANSACTION;

INSERT INTO hotel (id, name, city, state, check_in_time, check_out_time, max_online_nights)
VALUES (1, 'Druid City Hotel', 'Tuscaloosa', 'AL', '15:00:00', '11:00:00', 30);

-- Default rates (weeknight / weekend, Friday and Saturday nights) rise with the room:
-- Twin $90 / $135, King $110 / $160, Double Queen $130 / $185, Master Suite $250 / $325.
INSERT INTO room_type (hotel_id, code, name, bed, bed_label, sleeps, size_sqft, blurb, weekday_rate, weekend_rate) VALUES
(1, 'king',  'The King',         'king',  'One king bed',                       2, 340,
    'A generous king, a writing desk by the window, and a rain shower built for lingering.', 110.00, 160.00),
(1, 'queen', 'The Double Queen', 'queen', 'Two queen beds',                     4, 380,
    'Room for the whole party: two queens, a lounge chair, and space to spread out on game weekend.', 130.00, 185.00),
(1, 'twin',  'The Twin',         'twin',  'Two twin beds',                      2, 300,
    'Two crisply made twins for colleagues, siblings, or old roommates back for homecoming.', 90.00, 135.00),
(1, 'suite', 'The Master Suite', 'king',  'One king bed and a separate parlor', 4, 850,
    'Just two of them, on the top floor: a parlor for entertaining, a soaking tub, and a view over the oaks.', 250.00, 325.00);

-- 3 floors x 40 rooms: 101-140, 201-240, 301-340. Every third room is a king;
-- the rest alternate queen (even) and twin (odd). 339 and 340 are the master suites.
-- Result: 38 king, 41 queen, 39 twin, 2 suite.
INSERT INTO room (hotel_id, room_type_id, room_number, floor)
WITH RECURSIVE
    seq (i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM seq WHERE i < 40),
    floors (f) AS (SELECT 1 UNION ALL SELECT 2 UNION ALL SELECT 3),
    numbered AS (
        SELECT f, i, f * 100 + i AS room_number,
               CASE
                   WHEN f * 100 + i IN (339, 340) THEN 'suite'
                   WHEN i % 3 = 0 THEN 'king'
                   WHEN i % 2 = 0 THEN 'queen'
                   ELSE 'twin'
               END AS type_code
        FROM floors CROSS JOIN seq
    )
SELECT 1, rt.id, n.room_number, n.f
FROM numbered n
JOIN room_type rt ON rt.hotel_id = 1 AND rt.code = n.type_code
ORDER BY n.room_number;

-- Concierge knowledge documents (RAG). Facts from API/Concierge/hotel-info.json;
-- TODO marks anything not known yet. Keep the two in sync.
INSERT INTO knowledge_document (title, content) VALUES
    ('Check-in and check-out times',
     'Check-in is at 3:00 PM. Check-out is at 11:00 AM.'),
    ('Cancellation policy',
     'TODO: the cancellation policy is not confirmed yet.'),
    ('Pet policy',
     'TODO: the pet policy is not confirmed yet.'),
    ('Parking',
     'TODO: parking details (self-park, valet, cost) are not confirmed yet.'),
    ('Amenities',
     'TODO: the amenities list is not confirmed yet.'),
    ('House policies: smoking and minimum age',
     'Smoking policy: TODO (not confirmed yet). Minimum check-in age: TODO (not confirmed yet).'),
    ('Rates and weekend nights',
     'Default nightly rates rise with the room. The Twin: $90 per weeknight and $135 per weekend night. The King: $110 and $160. The Double Queen: $130 and $185. The Master Suite: $250 and $325. Friday and Saturday nights are weekend nights. These are default rates: staff can adjust rates, including special rates for specific dates, so the exact total for a stay is always shown on the booking page after choosing dates, and the concierge can look up exact totals too. New rates apply to new searches only; existing reservations keep the totals they were booked at. Prices shown are before taxes. Lodging tax and fees: TODO (not confirmed yet).'),
    ('Rooms and bed types',
     'The hotel has 120 rooms on 3 floors, 40 rooms per floor. Room numbers start with the floor: 101-140 on the first floor, 201-240 on the second, 301-340 on the third. About one third of rooms have a king bed; the rest are split between queen rooms and twin rooms. Room types: The King: one king bed, 340 sq ft, sleeps 2. A generous king, a writing desk by the window, and a rain shower built for lingering. The Double Queen: two queen beds, 380 sq ft, sleeps 4. Room for the whole party: two queens, a lounge chair, and space to spread out on game weekend. The Twin: two twin beds, 300 sq ft, sleeps 2. Two crisply made twins for colleagues, siblings, or old roommates back for homecoming. The Master Suite: one king bed and a separate parlor, 850 sq ft, sleeps 4. Just two of them, on the top floor: a parlor for entertaining, a soaking tub, and a view over the oaks. The 2 master suites count within the 120 rooms.'),
    ('How to book',
     'Guests need a hotel account to book. ''Sign in'' in the site menu signs in or creates one (name, email, mobile phone, and a password of at least 8 characters). There are two ways to book. On the booking page (''Book a stay'' in the site menu): choose check-in and check-out dates, optionally filter by bed type (king, queen, or twin; the master suites have a king bed) and floor (first, second, or third). The page lists each open room type with the full cost of the stay and a room picked for the guest, who can choose a different one. Or ask Druid AI Assistant, the chat on every page: it finds a room and price, then shows the details with a Confirm booking button, and nothing is booked until the guest presses it. Bookings made in the chat are due at check-in, or the guest can pay by card online with Pay now (on the booked card, the confirmation page, or ''Your stays''). Booked stays are listed under ''Your stays'' in the site menu.'),
    ('Tuscaloosa and the neighborhood',
     'The hotel is in Tuscaloosa, Alabama. TODO: neighborhood description and nearby attractions (for example, distance to campus and the stadium) are not confirmed yet.');

COMMIT;

-- Check: expect king 38, queen 41, suite 2, twin 39 (120 total).
SELECT rt.code, COUNT(*) AS rooms
FROM room r JOIN room_type rt ON rt.id = r.room_type_id
GROUP BY rt.code
ORDER BY rt.code;
