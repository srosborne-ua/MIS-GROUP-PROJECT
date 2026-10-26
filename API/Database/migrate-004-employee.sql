-- For databases created from an older schema.sql: adds the employee table behind
-- the Staff Portal sign-in. druid_app already has SELECT, INSERT, UPDATE, DELETE on
-- mis321.*, so no new grants are needed; druid_llm_ro gets nothing.
--
-- Run once by hand as an admin account. Then add the first employee with
--   dotnet run --project API -- add-employee

USE mis321;

CREATE TABLE employee (
    id                 INT UNSIGNED NOT NULL AUTO_INCREMENT,
    first_name         VARCHAR(60)  NOT NULL,
    last_name          VARCHAR(60)  NOT NULL,
    email              VARCHAR(254) NOT NULL,
    password_hash      VARCHAR(255) NOT NULL,
    is_active          BOOLEAN NOT NULL DEFAULT TRUE,
    last_signed_in_at  DATETIME NULL,
    created_at         DATETIME NOT NULL,
    updated_at         DATETIME NOT NULL,
    PRIMARY KEY (id),
    UNIQUE KEY uq_employee_email (email)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;
