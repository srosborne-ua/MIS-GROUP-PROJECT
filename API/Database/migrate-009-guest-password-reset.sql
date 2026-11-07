-- Guest "forgot password": one-time reset links. Only the SHA-256 of each token is stored.
-- A new table that the currently deployed code never touches, so this can safely run
-- before the code that uses it deploys. Safe to run twice.
--
-- Run by hand as an admin account, on any database built before this file.

CREATE TABLE IF NOT EXISTS guest_password_reset (
    id                INT UNSIGNED NOT NULL AUTO_INCREMENT,
    guest_account_id  INT UNSIGNED NOT NULL,
    token_hash        CHAR(64)     NOT NULL,
    expires_at        DATETIME     NOT NULL,
    created_at        DATETIME     NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_guest_password_reset_token (token_hash),
    KEY ix_guest_password_reset_account (guest_account_id, created_at),
    CONSTRAINT fk_guest_password_reset_account FOREIGN KEY (guest_account_id) REFERENCES guest_account (id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
