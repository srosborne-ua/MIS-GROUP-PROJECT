-- Concierge knowledge: confirmation/cancellation emails and guest self-cancel.
-- Data only, so the currently deployed code keeps working; safe to run before deploying.
-- Run as admin:  mysql ... < migrate-011-email-and-cancel-knowledge.sql

USE mis321;

INSERT INTO knowledge_document (title, content)
SELECT 'Emails and notifications',
       'Every booking sends the guest a confirmation email at the address on their account, whether it was made on the booking page, in the chat, or by staff. It has the reservation number, room, dates, total before taxes, and how it is paid, plus a link to ''Your stays''. Cancelling a reservation sends a cancellation email, and ''Forgot password'' on the sign-in page emails a one-time reset link. These are automated messages from a do-not-reply address, so guests should not reply to them; for help they should call the front desk. They can land in spam, so guests who do not see one should check there. Their stays are always listed under ''Your stays'' either way.'
WHERE NOT EXISTS (SELECT 1 FROM knowledge_document WHERE title = 'Emails and notifications');

UPDATE knowledge_document
SET content = 'Guests can cancel an upcoming stay that is not yet paid themselves under ''Your stays'' in the site menu, any time before check-out; the room is released and a cancellation email is sent. To cancel a stay that is already paid, or to change a stay, call the front desk. TODO: any cancellation deadline, fee, or refund policy is not confirmed yet.'
WHERE id > 0 AND title = 'Cancellation policy';
