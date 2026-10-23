-- For databases created from an older schema.sql: adds the concierge's knowledge
-- documents (RAG) and the read-only view the concierge queries. After this, run
-- create-llm-ro-user.sql to create druid_llm_ro.
--
-- Run once by hand as an admin account.

USE mis321;

CREATE TABLE knowledge_document (
    id       INT NOT NULL AUTO_INCREMENT,
    title    VARCHAR(255) NOT NULL,
    content  TEXT NOT NULL,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE SQL SECURITY DEFINER VIEW v_knowledge_documents AS
    SELECT id, title, content FROM knowledge_document;

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
     'Default rates are $100 per weeknight and $150 per weekend night. Friday and Saturday nights are weekend nights. These are default rates: staff can adjust rates, including special rates for specific dates, so the exact total for a stay is always shown on the booking page after choosing dates. New rates apply to new searches only; existing reservations keep the totals they were booked at. Prices shown are before taxes. Lodging tax and fees: TODO (not confirmed yet). Master suites use the same default rates as the other rooms.'),
    ('Rooms and bed types',
     'The hotel has 120 rooms on 3 floors, 40 rooms per floor. Room numbers start with the floor: 101-140 on the first floor, 201-240 on the second, 301-340 on the third. About one third of rooms have a king bed; the rest are split between queen rooms and twin rooms. Room types: The King: one king bed, 340 sq ft, sleeps 2. A generous king, a writing desk by the window, and a rain shower built for lingering. The Double Queen: two queen beds, 380 sq ft, sleeps 4. Room for the whole party: two queens, a lounge chair, and space to spread out on game weekend. The Twin: two twin beds, 300 sq ft, sleeps 2. Two crisply made twins for colleagues, siblings, or old roommates back for homecoming. The Master Suite: one king bed and a separate parlor, 850 sq ft, sleeps 4. Just two of them, on the top floor: a parlor for entertaining, a soaking tub, and a view over the oaks. The 2 master suites count within the 120 rooms.'),
    ('How to book',
     'Guests book on the booking page (''Book a stay'' in the site menu). Choose check-in and check-out dates, then optionally filter by bed type (king, queen, twin, suite) and floor (first, second, or third). The page lists available rooms with the full cost of the stay. Pick a room and complete the reservation form.'),
    ('Tuscaloosa and the neighborhood',
     'The hotel is in Tuscaloosa, Alabama. TODO: neighborhood description and nearby attractions (for example, distance to campus and the stadium) are not confirmed yet.');
