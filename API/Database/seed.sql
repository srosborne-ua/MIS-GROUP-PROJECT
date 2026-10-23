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

-- Default rates: $100 weeknight, $150 weekend (Friday and Saturday nights). Suites start at the same rates.
INSERT INTO room_type (hotel_id, code, name, bed, bed_label, sleeps, size_sqft, blurb, weekday_rate, weekend_rate) VALUES
(1, 'king',  'The King',         'king',  'One king bed',                       2, 340,
    'A generous king, a writing desk by the window, and a rain shower built for lingering.', 100.00, 150.00),
(1, 'queen', 'The Double Queen', 'queen', 'Two queen beds',                     4, 380,
    'Room for the whole party: two queens, a lounge chair, and space to spread out on game weekend.', 100.00, 150.00),
(1, 'twin',  'The Twin',         'twin',  'Two twin beds',                      2, 300,
    'Two crisply made twins for colleagues, siblings, or old roommates back for homecoming.', 100.00, 150.00),
(1, 'suite', 'The Master Suite', 'king',  'One king bed and a separate parlor', 4, 850,
    'Just two of them, on the top floor: a parlor for entertaining, a soaking tub, and a view over the oaks.', 100.00, 150.00);

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

COMMIT;

-- Check: expect king 38, queen 41, suite 2, twin 39 (120 total).
SELECT rt.code, COUNT(*) AS rooms
FROM room r JOIN room_type rt ON rt.id = r.room_type_id
GROUP BY rt.code
ORDER BY rt.code;
