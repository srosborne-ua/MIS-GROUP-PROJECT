-- Druid City Hotel schema (MySQL 8.0.16+ so CHECK constraints are enforced).
--
-- Run by hand as an admin account (e.g. root in Workbench), never by the app.
-- The app connects as druid_app (SELECT, INSERT, UPDATE, DELETE on mis321).
--
-- Conventions:
--   * Stay nights are DATE (no time zone). Timestamps are DATETIME in UTC, set by the app.
--   * booking.check_out is exclusive (the morning the guest leaves).
--   * maintenance_block, event_block, and seasonal_rate use an inclusive last_night.
--   * Availability is never stored: it is computed from booking, maintenance_block,
--     and event_block overlaps. Double-booking is prevented in the service layer by
--     locking the room row (SELECT ... FOR UPDATE) before the overlap check.
--   * No card data, ever. payment holds only paid/unpaid and the card-on-file opt-in.
--
-- This script creates tables only. It does not drop anything, and it fails if a
-- table with the same name already exists in mis321.

USE mis321;

CREATE TABLE hotel (
    id                 INT UNSIGNED NOT NULL AUTO_INCREMENT,
    name               VARCHAR(120) NOT NULL,
    street             VARCHAR(160) NULL,
    city               VARCHAR(80)  NOT NULL,
    state              CHAR(2)      NOT NULL,
    postal_code        VARCHAR(10)  NULL,
    phone              VARCHAR(30)  NULL,
    check_in_time      TIME         NOT NULL DEFAULT '15:00:00',
    check_out_time     TIME         NOT NULL DEFAULT '11:00:00',
    max_online_nights  TINYINT UNSIGNED NOT NULL DEFAULT 30,
    PRIMARY KEY (id),
    CONSTRAINT chk_hotel_max_nights CHECK (max_online_nights >= 1)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE room_type (
    id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
    hotel_id      INT UNSIGNED NOT NULL,
    code          VARCHAR(20)  NOT NULL,              -- 'king', 'queen', 'twin', 'suite'
    name          VARCHAR(80)  NOT NULL,              -- 'The King'
    bed           ENUM('king','queen','twin') NOT NULL,
    bed_label     VARCHAR(80)  NOT NULL,
    sleeps        TINYINT UNSIGNED NOT NULL,
    size_sqft     SMALLINT UNSIGNED NULL,
    blurb         VARCHAR(500) NOT NULL,
    weekday_rate  DECIMAL(8,2) NOT NULL,
    weekend_rate  DECIMAL(8,2) NOT NULL,              -- Friday and Saturday nights
    PRIMARY KEY (id),
    UNIQUE KEY uq_room_type_code (hotel_id, code),
    CONSTRAINT fk_room_type_hotel FOREIGN KEY (hotel_id) REFERENCES hotel (id) ON DELETE RESTRICT,
    CONSTRAINT chk_room_type_sleeps CHECK (sleeps BETWEEN 1 AND 10),
    CONSTRAINT chk_room_type_weekday CHECK (weekday_rate > 0),
    CONSTRAINT chk_room_type_weekend CHECK (weekend_rate > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE room (
    id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
    hotel_id      INT UNSIGNED NOT NULL,
    room_type_id  INT UNSIGNED NOT NULL,
    room_number   SMALLINT UNSIGNED NOT NULL,
    floor         TINYINT UNSIGNED NOT NULL,
    is_active     BOOLEAN NOT NULL DEFAULT TRUE,
    PRIMARY KEY (id),
    UNIQUE KEY uq_room_number (hotel_id, room_number),
    CONSTRAINT fk_room_hotel FOREIGN KEY (hotel_id) REFERENCES hotel (id) ON DELETE RESTRICT,
    CONSTRAINT fk_room_type FOREIGN KEY (room_type_id) REFERENCES room_type (id) ON DELETE RESTRICT,
    CONSTRAINT chk_room_floor CHECK (floor = room_number DIV 100)   -- 214 is on floor 2
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- One row per guest email, holding the details from their first booking. A later
-- booking never rewrites this row; each booking keeps its own copy of the name and
-- phone entered with it (booking.guest_first_name etc.).
CREATE TABLE guest (
    id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
    first_name  VARCHAR(60)  NOT NULL,
    last_name   VARCHAR(60)  NOT NULL,
    email       VARCHAR(254) NOT NULL,
    phone       VARCHAR(30)  NOT NULL,
    created_at  DATETIME NOT NULL,
    updated_at  DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_guest_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- The API shows booking ids as 'DC-' + id.
CREATE TABLE booking (
    id                INT UNSIGNED NOT NULL AUTO_INCREMENT,
    room_id           INT UNSIGNED NOT NULL,
    room_type_id      INT UNSIGNED NOT NULL,          -- type at time of booking
    guest_id          INT UNSIGNED NOT NULL,
    guest_first_name  VARCHAR(60)  NOT NULL,          -- contact details as entered for this booking
    guest_last_name   VARCHAR(60)  NOT NULL,
    guest_phone       VARCHAR(30)  NOT NULL,
    check_in          DATE NOT NULL,
    check_out         DATE NOT NULL,                  -- exclusive
    guest_count       TINYINT UNSIGNED NOT NULL,
    status            ENUM('confirmed','cancelled') NOT NULL DEFAULT 'confirmed',
    source            ENUM('web','staff','concierge') NOT NULL,
    special_requests  VARCHAR(1000) NOT NULL DEFAULT '',
    total             DECIMAL(10,2) NOT NULL,         -- frozen at booking time
    created_at        DATETIME NOT NULL,
    cancelled_at      DATETIME NULL,
    PRIMARY KEY (id),
    KEY ix_booking_overlap (room_id, status, check_in, check_out),
    KEY ix_booking_guest (guest_id),
    CONSTRAINT fk_booking_room FOREIGN KEY (room_id) REFERENCES room (id) ON DELETE RESTRICT,
    CONSTRAINT fk_booking_room_type FOREIGN KEY (room_type_id) REFERENCES room_type (id) ON DELETE RESTRICT,
    CONSTRAINT fk_booking_guest FOREIGN KEY (guest_id) REFERENCES guest (id) ON DELETE RESTRICT,
    CONSTRAINT chk_booking_dates CHECK (check_out > check_in),
    CONSTRAINT chk_booking_guests CHECK (guest_count >= 1),
    CONSTRAINT chk_booking_total CHECK (total >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci AUTO_INCREMENT=1042;

-- Per-night price snapshot, so rate changes never alter existing bookings.
CREATE TABLE booking_night (
    booking_id  INT UNSIGNED NOT NULL,
    night_date  DATE NOT NULL,
    amount      DECIMAL(8,2) NOT NULL,
    kind        ENUM('weekday','weekend','special') NOT NULL,
    label       VARCHAR(80) NULL,
    PRIMARY KEY (booking_id, night_date),
    CONSTRAINT fk_booking_night_booking FOREIGN KEY (booking_id) REFERENCES booking (id) ON DELETE CASCADE,
    CONSTRAINT chk_booking_night_amount CHECK (amount >= 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Guest communication log (simulated emails and staff notes).
CREATE TABLE booking_message (
    id          INT UNSIGNED NOT NULL AUTO_INCREMENT,
    booking_id  INT UNSIGNED NOT NULL,
    sender      ENUM('system','staff','guest','concierge') NOT NULL,
    body        VARCHAR(2000) NOT NULL,
    created_at  DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_booking_message_booking (booking_id, created_at),
    CONSTRAINT fk_booking_message_booking FOREIGN KEY (booking_id) REFERENCES booking (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- One row per booking. No card number, expiry, CVC, or token columns, by design.
CREATE TABLE payment (
    id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
    booking_id    INT UNSIGNED NOT NULL,
    status        ENUM('unpaid','paid') NOT NULL DEFAULT 'unpaid',
    method        ENUM('online','at_hotel') NULL,
    card_on_file  BOOLEAN NOT NULL DEFAULT FALSE,
    amount        DECIMAL(10,2) NOT NULL,
    paid_at       DATETIME NULL,
    updated_at    DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_payment_booking (booking_id),
    CONSTRAINT fk_payment_booking FOREIGN KEY (booking_id) REFERENCES booking (id) ON DELETE CASCADE,
    CONSTRAINT chk_payment_amount CHECK (amount >= 0),
    CONSTRAINT chk_payment_paid_at CHECK (status = 'paid' OR paid_at IS NULL)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Price overrides by date range. room_type_id NULL applies to every room type.
-- A room-type-specific rate beats an all-types rate; overlaps are rejected in the service layer.
CREATE TABLE seasonal_rate (
    id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
    hotel_id      INT UNSIGNED NOT NULL,
    room_type_id  INT UNSIGNED NULL,
    first_night   DATE NOT NULL,
    last_night    DATE NOT NULL,                      -- inclusive
    nightly_rate  DECIMAL(8,2) NOT NULL,
    label         VARCHAR(80) NOT NULL,
    PRIMARY KEY (id),
    KEY ix_seasonal_rate_dates (first_night, last_night),
    CONSTRAINT fk_seasonal_rate_hotel FOREIGN KEY (hotel_id) REFERENCES hotel (id) ON DELETE RESTRICT,
    CONSTRAINT fk_seasonal_rate_room_type FOREIGN KEY (room_type_id) REFERENCES room_type (id) ON DELETE RESTRICT,
    CONSTRAINT chk_seasonal_rate_dates CHECK (last_night >= first_night),
    CONSTRAINT chk_seasonal_rate_amount CHECK (nightly_rate > 0)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Not used by the app yet (pending Mr. Rogers' confirmation of amenities).
CREATE TABLE amenity (
    id           INT UNSIGNED NOT NULL AUTO_INCREMENT,
    name         VARCHAR(80)  NOT NULL,
    description  VARCHAR(500) NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_amenity_name (name)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE room_type_amenity (
    room_type_id  INT UNSIGNED NOT NULL,
    amenity_id    INT UNSIGNED NOT NULL,
    PRIMARY KEY (room_type_id, amenity_id),
    KEY ix_room_type_amenity_amenity (amenity_id),
    CONSTRAINT fk_rta_room_type FOREIGN KEY (room_type_id) REFERENCES room_type (id) ON DELETE CASCADE,
    CONSTRAINT fk_rta_amenity FOREIGN KEY (amenity_id) REFERENCES amenity (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Concierge or guest requests tied to a stay. Not used by the app yet.
CREATE TABLE service_request (
    id            INT UNSIGNED NOT NULL AUTO_INCREMENT,
    booking_id    INT UNSIGNED NOT NULL,
    category      ENUM('housekeeping','amenity','transport','dining','maintenance','other') NOT NULL,
    details       VARCHAR(1000) NOT NULL,
    status        ENUM('open','in_progress','done','cancelled') NOT NULL DEFAULT 'open',
    source        ENUM('guest','concierge','staff') NOT NULL,
    created_at    DATETIME NOT NULL,
    completed_at  DATETIME NULL,
    PRIMARY KEY (id),
    KEY ix_service_request_booking (booking_id),
    CONSTRAINT fk_service_request_booking FOREIGN KEY (booking_id) REFERENCES booking (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Takes one room out of service. Serves the Holds endpoints (API id 'H-' + id).
CREATE TABLE maintenance_block (
    id           INT UNSIGNED NOT NULL AUTO_INCREMENT,
    room_id      INT UNSIGNED NOT NULL,
    first_night  DATE NOT NULL,
    last_night   DATE NOT NULL,                       -- inclusive
    reason       VARCHAR(200) NOT NULL,
    created_at   DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_maintenance_block_overlap (room_id, first_night, last_night),
    CONSTRAINT fk_maintenance_block_room FOREIGN KEY (room_id) REFERENCES room (id) ON DELETE RESTRICT,
    CONSTRAINT chk_maintenance_block_dates CHECK (last_night >= first_night)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Groups of rooms blocked for an event (API id 'B-' + id).
CREATE TABLE event_block (
    id           INT UNSIGNED NOT NULL AUTO_INCREMENT,
    name         VARCHAR(120) NOT NULL,
    first_night  DATE NOT NULL,
    last_night   DATE NOT NULL,                       -- inclusive
    created_at   DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_event_block_dates (first_night, last_night),
    CONSTRAINT chk_event_block_dates CHECK (last_night >= first_night)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

CREATE TABLE event_block_room (
    event_block_id  INT UNSIGNED NOT NULL,
    room_id         INT UNSIGNED NOT NULL,
    PRIMARY KEY (event_block_id, room_id),
    KEY ix_event_block_room_room (room_id),
    CONSTRAINT fk_event_block_room_block FOREIGN KEY (event_block_id) REFERENCES event_block (id) ON DELETE CASCADE,
    CONSTRAINT fk_event_block_room_room FOREIGN KEY (room_id) REFERENCES room (id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Hotel reference documents the Druid AI Assistant searches to answer guest
-- questions (RAG). Facts come from API/Concierge/hotel-info.json; anything not yet
-- confirmed by Mr. Rogers is written as TODO, and the concierge treats it as unknown.
CREATE TABLE knowledge_document (
    id       INT NOT NULL AUTO_INCREMENT,
    title    VARCHAR(255) NOT NULL,
    content  TEXT NOT NULL,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- The concierge's model-written SELECTs run against the v_* views below, never
-- against tables; they are the only objects druid_llm_ro can read (see
-- create-llm-ro-user.sql).
CREATE SQL SECURITY DEFINER VIEW v_knowledge_documents AS
    SELECT id, title, content FROM knowledge_document;

-- Read-only views for the concierge's room, rate, and availability questions. No guest
-- data: no names, emails, phones, payments, messages, booking ids, special requests,
-- hold reasons, or event names. Room/date occupancy is already public through the
-- booking page's search, which lists open room numbers.
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
