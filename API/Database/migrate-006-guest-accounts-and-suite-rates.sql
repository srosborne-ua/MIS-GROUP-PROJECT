-- For databases created from an older schema.sql (after migrate-005):
--   * guest accounts: guests sign in to book (guest_account, booking.guest_account_id)
--   * master suites cost more than the other rooms: $250 weeknight, $325 weekend
--   * the concierge's "Rates" and "How to book" documents match (concierge booking)
-- druid_app already has SELECT, INSERT, UPDATE, DELETE on mis321.*, so no new grants
-- are needed; druid_llm_ro gets nothing (no v_* view reads guest_account).
--
-- Run once by hand as an admin account, before deploying the code that uses it.
-- Existing reservations keep the totals they were booked at.

USE mis321;

CREATE TABLE guest_account (
    id                 INT UNSIGNED NOT NULL AUTO_INCREMENT,
    first_name         VARCHAR(60)  NOT NULL,
    last_name          VARCHAR(60)  NOT NULL,
    email              VARCHAR(254) NOT NULL,
    phone              VARCHAR(30)  NOT NULL,
    password_hash      VARCHAR(255) NOT NULL,
    last_signed_in_at  DATETIME NULL,
    created_at         DATETIME NOT NULL,
    updated_at         DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_guest_account_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

ALTER TABLE booking
    ADD COLUMN guest_account_id INT UNSIGNED NULL AFTER guest_id,
    ADD KEY ix_booking_guest_account (guest_account_id, check_in),
    ADD CONSTRAINT fk_booking_guest_account FOREIGN KEY (guest_account_id) REFERENCES guest_account (id) ON DELETE RESTRICT;

-- Only while the suites are still on the old shared defaults, so a rate staff already
-- set in the portal is kept. (The id filter keeps MySQL Workbench's safe-update mode happy.)
UPDATE room_type SET weekday_rate = 250.00, weekend_rate = 325.00
WHERE id > 0 AND code = 'suite' AND weekday_rate = 100.00 AND weekend_rate = 150.00;

UPDATE knowledge_document SET content =
    'Default rates are $100 per weeknight and $150 per weekend night for The King, The Double Queen, and The Twin, and $250 per weeknight and $325 per weekend night for The Master Suite. Friday and Saturday nights are weekend nights. These are default rates: staff can adjust rates, including special rates for specific dates, so the exact total for a stay is always shown on the booking page after choosing dates, and the concierge can look up exact totals too. New rates apply to new searches only; existing reservations keep the totals they were booked at. Prices shown are before taxes. Lodging tax and fees: TODO (not confirmed yet).'
WHERE id > 0 AND title = 'Rates and weekend nights';

UPDATE knowledge_document SET content =
    'Guests need a hotel account to book. ''Sign in'' in the site menu signs in or creates one (name, email, mobile phone, and a password of at least 8 characters). There are two ways to book. On the booking page (''Book a stay'' in the site menu): choose check-in and check-out dates, optionally filter by bed type (king, queen, or twin; the master suites have a king bed) and floor (first, second, or third). The page lists each open room type with the full cost of the stay and a room picked for the guest, who can choose a different one. Or ask Druid AI Assistant, the chat on every page: it finds a room and price, then shows the details with a Confirm booking button, and nothing is booked until the guest presses it. Bookings made in the chat are due at check-in, or the guest can pay by card online with Pay now (on the booked card, the confirmation page, or ''Your stays''). Booked stays are listed under ''Your stays'' in the site menu.'
WHERE id > 0 AND title = 'How to book';
