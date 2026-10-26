-- For databases created from an older schema.sql (after migrate-004): adds employee
-- roles (manager / staff) and the shift schedule. druid_app already has SELECT, INSERT,
-- UPDATE, DELETE on mis321.*, so no new grants are needed; druid_llm_ro gets nothing.
--
-- Run once by hand as an admin account.

USE mis321;

ALTER TABLE employee
    ADD COLUMN role ENUM('manager','staff') NOT NULL DEFAULT 'staff' AFTER password_hash;

-- Everyone with an account could manage accounts before roles existed; keep that.
-- (WHERE id > 0 keeps MySQL Workbench's safe-update mode happy.)
UPDATE employee SET role = 'manager' WHERE id > 0;

CREATE TABLE shift (
    id           INT UNSIGNED NOT NULL AUTO_INCREMENT,
    employee_id  INT UNSIGNED NOT NULL,
    shift_date   DATE NOT NULL,
    start_time   TIME NOT NULL,
    end_time     TIME NOT NULL,
    note         VARCHAR(80) NOT NULL DEFAULT '',
    created_at   DATETIME NOT NULL,
    PRIMARY KEY (id),
    KEY ix_shift_date (shift_date),
    KEY ix_shift_employee (employee_id, shift_date),
    CONSTRAINT fk_shift_employee FOREIGN KEY (employee_id) REFERENCES employee (id) ON DELETE RESTRICT,
    CONSTRAINT chk_shift_times CHECK (end_time <> start_time)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
