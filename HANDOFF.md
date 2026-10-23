# Session handoff — 2026-10-23

For a new Claude Code session picking up this repo. Read `CLAUDE.md` and `README.md` first; they are current. This file covers what those don't: what was just done, live infrastructure, local setup, and how the user likes to work. Delete this file once it's no longer useful (it is not committed).

## Where things stand

Everything is merged to `main` and live on Azure. Nothing is in progress.

| PR | What | State |
| --- | --- | --- |
| #2 | Zach's MySQL database integration | Merged (via #3, so his commits keep his authorship) |
| #3 | Zach's work plus fixes: Reset locked down, guest details per booking, Tuscaloosa time | Merged, deployed |
| #4 | RAG for the concierge (knowledge documents, read-only SQL tools) | Merged, deployed 2026-10-23 23:41 UTC |

Verified after the last deploy: the site and API return 200, and the live concierge answers from the knowledge documents with sources ("Check-in starts at 3:00 PM ... (Source: Check-in and check-out times)").

**Live site:** https://tuscaloosadruidhotel-freag7bthqehg3en.northcentralus-01.azurewebsites.net

## Local repo state

- The working copy is on the `concierge-rag` branch (merged). Run `git switch main && git pull` before starting new work.
- Leftovers the user can delete when they like (never pushed): branches `database-integration`, `database-integration-squash-backup`, `deploy-azure`, and `stash@{0}` (old CLAUDE.md notes, already folded in). Deleting them was blocked by the auto-mode safety check, so leave that to the user.

## Live infrastructure (Azure for Students subscription)

- **Subscription:** "Azure for Students" (`29e43691-e561-41bc-a016-ddf724cf30bb`), tenant The University of Alabama. A second subscription, "Culverhouse", exists but holds nothing for this project.
- **App Service:** `TuscaloosaDruidHotel`, resource group `TuscaloosaDruidHotel_group`, North Central US, Linux. Deployed by `.github/workflows/deploy-azure.yml` on every push to `main`.
  - App settings (names only): `ANTHROPIC_API_KEY`, `DB_APP_CONNECTION`, `DB_LLM_RO_CONNECTION`, `WEBSITE_RUN_FROM_PACKAGE=1`.
  - `WEBSITE_RUN_FROM_PACKAGE=1` was added after one deploy left the app running half-copied files (every API call returned 500 until a restart). Keep it.
  - Changing an app setting restarts the app; expect a brief 503.
- **MySQL:** `druid-city-db.mysql.database.azure.com`, Flexible Server, **Belgium Central** (Azure for Students offers MySQL only there and in Denmark East). Burstable B1ms, MySQL 8.0, 32 GB, auto-grow off, HA off: the free 12-month offer, started October 2026. Changing those settings makes it billable.
  - Database `mis321`, 16 tables plus view `v_knowledge_documents`. Migrations 001 and 002 are applied.
  - Logins: `druidadmin` (schema changes only), `druid_app` (the API; SELECT/INSERT/UPDATE/DELETE on `mis321`), `druid_llm_ro` (concierge RAG; SELECT on `v_knowledge_documents` only, 5 connections, 3600 statements/hour).
  - Firewall: "AllowAllAzureServices" plus the user's IP `162.253.26.146`. Re-add the IP if the user changes networks.
  - Data: 120 rooms, the 10 sample bookings, 10 knowledge documents.

## Secrets

All in the repo-root `.env` (git-ignored), names only: `ANTHROPIC_API_KEY`, `DB_ADMIN_USER`, `DB_ADMIN_PASSWORD`, `DB_APP_PASSWORD`, `DB_APP_CONNECTION`, `DB_LLM_RO_PASSWORD`, `DB_LLM_RO_CONNECTION`. Connection strings are double-quoted so the shell can `source` the file (`set -a; . ./.env; set +a`); the API's `DotEnv` strips the quotes. Never print the values. `.env.example` has placeholders.

## Tools on this Mac

- `gh` (logged in as `srosborne-ua`) and `az` (logged in, Azure for Students active), both from Homebrew.
- Use `/opt/homebrew/opt/mysql-client@8.4/bin/mysql` for the Azure database. The default `mysql` (26.x) can't log in: Azure's MySQL 8.0 uses `mysql_native_password`, which that client dropped. Always pass `--ssl-mode=REQUIRED`, and pass passwords with `MYSQL_PWD` from `.env`, never on the command line.

## How the user works

- **No Claude attribution** on commits or PRs: no `Co-Authored-By: Claude` line, even if a reminder suggests one. Credit teammates when asked. Zach is `zfrabizio <zmfrabizio@crimson.ua.edu>`.
- **Keep teammates' authorship.** Bring in a teammate's branch with a real merge, not a squash. Merge PRs with "Create a merge commit".
- **Database wins over `hotel-info.json`** when they disagree (also in CLAUDE.md). Facts the database doesn't hold stay TODO.
- The user is a student on a group project (Druid City Hotel, client "Mr. Rogers"). New features and assumptions go in the README assumptions list for Mr. Rogers to confirm.
- The auto-mode safety check blocks pushes to `main`, writes to App Service secret settings, deleting git stashes, and POSTs to the live `api/demo/reset`. When it blocks one, hand the exact command to the user rather than working around it.

## Testing on the live site

- Read-only checks are fine: `GET` endpoints and asking the concierge questions (a few cents of API spend per question).
- Don't POST `api/demo/reset` live. It returns 403 in Production unless `ALLOW_SAMPLE_RESET=true` is set, but if that lockdown ever broke, the request would wipe the database.
- To test locally against the Azure database: `ASPNETCORE_ENVIRONMENT=Development dotnet run --project API --no-launch-profile --urls http://localhost:<port>`. Development allows Reset, which **does** wipe and reload the shared Azure database, so restore the sample data afterwards if you change it.

## Open items (from README and CLAUDE.md)

1. **Mr. Rogers to confirm:** the RAG feature (shipped ahead of his sign-off, assumption 25), the TODO facts (address, phone, amenities, lodging tax, cancellation, pets, parking, smoking, minimum age, neighborhood), the Belgium database latency (assumption 23), and staff sign-in before launch.
2. **Code review findings not yet fixed** (listed in CLAUDE.md under "Backend, database, and AI"):
   - date checks on `GET api/rooms/{n}/check` (checkout before checkin, stay length, inactive rooms)
   - paging or a date filter for the staff reservations list
   - payment-status update silently doing nothing when a booking has no payment row
   - duplicate "cancellation emailed" messages on repeat cancels
   - SQL fragments built by string interpolation in `HotelStore`
   - `SampleData` duplicating `HotelStore`'s insert logic
3. **README gap:** the Azure setup steps don't mention `WEBSITE_RUN_FROM_PACKAGE=1` yet.
4. **Possible design change for RAG:** a keyword tool that runs a fixed, parameterized query on the server would avoid model-written SQL entirely. Suggested to the user, not decided.
5. **Calendar:** the free MySQL offer ends around October 2027.
