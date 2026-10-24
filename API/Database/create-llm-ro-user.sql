-- Creates druid_llm_ro, the read-only login the concierge uses for its model-written
-- queries. It can read the concierge views (knowledge documents, hotel, room types,
-- rooms, special rates, room unavailability) and nothing else: no tables, no guest,
-- payment, or booking_message data, and no writes.
--
-- Run once by hand as an admin account, after schema.sql (or, for an older database,
-- after migrate-002 and migrate-003, skipping migrate-003's GRANT lines). Replace
-- CHANGE_ME with a strong password and don't commit the real one; put the matching
-- connection string in .env (and the App Service settings) as DB_LLM_RO_CONNECTION.
--
-- Caps: at most 5 connections at once and 3600 statements an hour. Query time is
-- capped per session by the API (max_execution_time) and its command timeout.

CREATE USER 'druid_llm_ro'@'%' IDENTIFIED BY 'CHANGE_ME'
    WITH MAX_USER_CONNECTIONS 5 MAX_QUERIES_PER_HOUR 3600;

GRANT SELECT ON mis321.v_knowledge_documents TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_hotel TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_room_types TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_rooms TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_special_rates TO 'druid_llm_ro'@'%';
GRANT SELECT ON mis321.v_room_unavailable TO 'druid_llm_ro'@'%';
