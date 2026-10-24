-- For databases created from an older schema.sql: adds the read-only views that let
-- the concierge answer room, rate, and availability questions with its own SELECTs,
-- and grants druid_llm_ro SELECT on them.
--
-- The views hold no guest data: no names, emails, phones, payments, messages,
-- booking ids, special requests, hold reasons, or event names. Room/date occupancy is
-- already public through the booking page's search, which lists open room numbers.
--
-- Run once by hand as an admin account, after migrate-002. The GRANT lines need
-- druid_llm_ro to exist already; if it doesn't, skip them and run
-- create-llm-ro-user.sql afterwards, which grants every view.

USE mis321;

CREATE SQL SECURITY DEFINER VIEW v_hotel AS
    SELECT name, street, city, state, postal_code, phone, check_in_time, check_out_time, max_online_nights
    FROM hotel;

CREATE SQL SECURITY DEFINER VIEW v_room_types AS
    SELECT t.code AS type_code, t.name, t.bed, t.bed_label, t.sleeps, t.size_sqft, t.blurb AS description,
           t.weekday_rate, t.weekend_rate,
           (SELECT COUNT(*) FROM room r WHERE r.room_type_id = t.id AND r.is_active) AS active_rooms
    FROM room_type t;

CREATE SQL SECURITY DEFINER VIEW v_rooms AS
    SELECT r.room_number, r.floor, t.code AS type_code, t.name AS type_name, t.bed, t.sleeps, t.size_sqft, r.is_active
    FROM room r JOIN room_type t ON t.id = r.room_type_id;

-- Current and future special rates. type_code NULL means every room type.
CREATE SQL SECURITY DEFINER VIEW v_special_rates AS
    SELECT t.code AS type_code, s.first_night, s.last_night, s.nightly_rate, s.label
    FROM seasonal_rate s LEFT JOIN room_type t ON t.id = s.room_type_id
    WHERE s.last_night >= CURDATE() - INTERVAL 1 DAY;

-- Nights each room is taken (inclusive first_night..last_night), current and future
-- only. kind is 'booked', 'maintenance', or 'event'. CURDATE() is the server's UTC
-- date, so one day of slack keeps tonight in the view for Tuscaloosa.
CREATE SQL SECURITY DEFINER VIEW v_room_unavailable AS
    SELECT r.room_number, b.check_in AS first_night, b.check_out - INTERVAL 1 DAY AS last_night, 'booked' AS kind
    FROM booking b JOIN room r ON r.id = b.room_id
    WHERE b.status = 'confirmed' AND b.check_out > CURDATE() - INTERVAL 1 DAY
    UNION ALL
    SELECT r.room_number, m.first_night, m.last_night, 'maintenance'
    FROM maintenance_block m JOIN room r ON r.id = m.room_id
    WHERE m.last_night >= CURDATE() - INTERVAL 1 DAY
    UNION ALL
    SELECT r.room_number, e.first_night, e.last_night, 'event'
    FROM event_block e
    JOIN event_block_room ebr ON ebr.event_block_id = e.id
    JOIN room r ON r.id = ebr.room_id
    WHERE e.last_night >= CURDATE() - INTERVAL 1 DAY;

GRANT SELECT ON mis321.v_hotel TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_room_types TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_rooms TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_special_rates TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_room_unavailable TO 'druid_llm_ro'@'%';
