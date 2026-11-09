-- Remembers the latest Stripe Checkout session started for a stay, so "Check payment" can ask
-- Stripe directly whether it was paid (a lost webhook or a closed tab then heals itself).
-- A Checkout session id is not card data. A new nullable column that the currently deployed code
-- never reads, so this can safely run before the code that uses it deploys.
--
-- Run by hand as an admin account, on any database built before this file. Run it once.

ALTER TABLE payment ADD COLUMN stripe_session_id VARCHAR(255) NULL AFTER method;
