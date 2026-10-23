-- For databases created from an older schema.sql: each booking keeps its own copy of
-- the guest's name and phone, so a later booking with the same email can't rewrite
-- the contact details shown on earlier reservations.
--
-- Run once by hand as an admin account. Already applied to the Azure database.

USE mis321;

ALTER TABLE booking
    ADD COLUMN guest_first_name VARCHAR(60) NOT NULL DEFAULT '' AFTER guest_id,
    ADD COLUMN guest_last_name  VARCHAR(60) NOT NULL DEFAULT '' AFTER guest_first_name,
    ADD COLUMN guest_phone      VARCHAR(30) NOT NULL DEFAULT '' AFTER guest_last_name;

UPDATE booking b JOIN guest g ON g.id = b.guest_id
SET b.guest_first_name = g.first_name, b.guest_last_name = g.last_name, b.guest_phone = g.phone;

ALTER TABLE booking
    ALTER COLUMN guest_first_name DROP DEFAULT,
    ALTER COLUMN guest_last_name DROP DEFAULT,
    ALTER COLUMN guest_phone DROP DEFAULT;
