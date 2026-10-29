-- For databases built before per-room-type rates (after migrate-006): every room type
-- gets its own default rates, rising with the room (weeknight / weekend, Friday and
-- Saturday nights): Twin $90 / $135, King $110 / $160, Double Queen $130 / $185,
-- Master Suite $250 / $325. Also updates the concierge's "Rates and weekend nights" document.
--
-- This replaces whatever rates are set now. Existing reservations keep the totals they
-- were booked at. Safe to run before or after deploying the code that edits rates per type.
--
-- Run once by hand as an admin account. (The id filter keeps MySQL Workbench's
-- safe-update mode happy.)

USE mis321;

UPDATE room_type SET weekday_rate =  90.00, weekend_rate = 135.00 WHERE id > 0 AND code = 'twin';
UPDATE room_type SET weekday_rate = 110.00, weekend_rate = 160.00 WHERE id > 0 AND code = 'king';
UPDATE room_type SET weekday_rate = 130.00, weekend_rate = 185.00 WHERE id > 0 AND code = 'queen';
UPDATE room_type SET weekday_rate = 250.00, weekend_rate = 325.00 WHERE id > 0 AND code = 'suite';

UPDATE knowledge_document SET content =
    'Default nightly rates rise with the room. The Twin: $90 per weeknight and $135 per weekend night. The King: $110 and $160. The Double Queen: $130 and $185. The Master Suite: $250 and $325. Friday and Saturday nights are weekend nights. These are default rates: staff can adjust rates, including special rates for specific dates, so the exact total for a stay is always shown on the booking page after choosing dates, and the concierge can look up exact totals too. New rates apply to new searches only; existing reservations keep the totals they were booked at. Prices shown are before taxes. Lodging tax and fees: TODO (not confirmed yet).'
WHERE id > 0 AND title = 'Rates and weekend nights';
