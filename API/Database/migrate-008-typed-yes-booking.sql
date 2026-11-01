-- Updates the concierge's "How to book" document: a guest can now confirm a chat booking
-- by replying yes as well as by pressing Confirm booking. Text only, so the currently
-- deployed code still works after it runs, and running it twice changes nothing.
--
-- Run by hand as an admin account, on any database built before this file.

UPDATE knowledge_document
SET content = REPLACE(content,
    'nothing is booked until the guest presses it.',
    'nothing is booked until the guest presses it or replies yes.')
WHERE title = 'How to book';
