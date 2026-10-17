# Flight booking: supplier integration

`booking-api` searches two flight suppliers at the same time and returns their offers in one common format. It books an offer by paying from the user's wallet. The two suppliers are in-memory mocks (`aerohub-mock`, `skylink-mock`) built from the task description.

| Service | Host port | What |
|---|---|---|
| booking-api | 8080 | ASP.NET Core 8 Web API (controllers), EF Core + PostgreSQL |
| aerohub-mock | 8081 | AeroHub: token auth, `/search`, `/book` |
| skylink-mock | 8082 | SkyLink: signed request bodies, `/shopping`, `/order`, `/ticketing`, `GET /order/{n}` |
| postgres | 5432 | PostgreSQL 17 used by booking-api |

## Run

```bash
docker compose up --build
```

booking-api applies its database migrations on startup, so an empty database is ready without any manual step. It answers on `http://localhost:8080` once the log shows `Now listening on: http://[::]:8080`.

The supplier credentials in `docker-compose.yml` are the demo values from the task (`flamingo` / `secret`, `FLM001` / `k3y-123`). Both the mocks and booking-api read them from the same variables. They are defaults only, and the following environment variables (or a `.env` file next to `docker-compose.yml`) override them:

| Variable | Default |
|---|---|
| `AEROHUB_USERNAME` / `AEROHUB_PASSWORD` | `flamingo` / `secret` |
| `SKYLINK_PARTNER_ID` / `SKYLINK_PARTNER_KEY` | `FLM001` / `k3y-123` |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | `booking` / `booking` |
| `POSTGRES_HOST_PORT` | `5432` (set another port if 5432 is already in use on your machine) |

To look at the data:

```bash
docker compose exec postgres psql -U booking -d flightbooking
# tables: wallets, wallet_transactions, offers, bookings, booking_status_histories
```

Non-secret settings live in `src/BookingApi/appsettings.json`. Each one can be overridden with an environment variable (e.g. `Search__TimeoutSeconds=3`). Every setting is validated on startup. A missing setting, a non-positive timeout or interval, or a currency rate ≤ 0 stops the app with a clear message, instead of running on a silent default.

| Setting | Default | Meaning |
|---|---|---|
| `Pricing:RatesToIqd` | USD 1550, EUR 1680 | Exchange rates |
| `Search:TimeoutSeconds` | 5 | Total search budget for all suppliers |
| `Search:OfferLifetimeMinutes` | 30 | How long an `offerId` can be booked |
| `AeroHub:BookTimeoutSeconds`, `SkyLink:BookTimeoutSeconds` | 15 | Our limit for one booking call |
| `PendingPoller:IntervalSeconds` | 10 | How often Pending bookings are checked |
| `PendingPoller:PendingTimeoutSeconds` | 300 | Pending longer than this → RequiresReview |
| `PendingPoller:StatusTimeoutSeconds` | 10 | Limit for one status call |

## Tests

```bash
dotnet test
```

Needs the .NET 8 SDK (or newer) and **Docker running**. The tests that involve money, idempotency, concurrency and the endpoints run against a real PostgreSQL container started by Testcontainers (one container for the whole run). EF Core's in-memory provider is not used anywhere: it has no transactions, row locks or unique-index blocking, so it cannot prove the money rules.

The tests required by section 7 of the task:

| Task requirement | Test |
|---|---|
| AeroHub mapping, TK + 785 → TK785 | `AeroHubSupplierTests.Mapping_AeroHub_sample_maps_to_common_model_with_TK_785_as_TK785` |
| SkyLink join, two segments → stops = 1 | `SkyLinkSupplierTests.Mapping_SkyLink_sample_joins_solutions_flights_segments_into_common_model`, `…Mapping_SkyLink_two_segment_flight_has_one_stop_first_departure_and_last_arrival` |
| Rounding 454,305 / 455,000 / 455,000.01 | `PriceCalculatorTests.Rounding_rounds_up_to_next_1000` |
| De-duplication | `OfferDeduplicatorTests.Deduplication_returns_TK785_from_both_suppliers_once_at_lower_price` |
| Parallel search, slow AeroHub → TimedOut | `SearchEndpointsTests.Parallel_search_slow_AeroHub_is_TimedOut_and_SkyLink_results_are_still_returned` |
| Idempotency, sequential and simultaneous | `BookingEndpointsTests.Booking_two_identical_requests_with_same_key_debit_once_and_create_one_booking`, `…Booking_concurrent_requests_with_same_key_debit_once_and_book_once` |
| Insufficient balance | `BookingEndpointsTests.Booking_insufficient_balance_returns_402_with_no_booking_and_no_debit` |
| Booking outcomes | `BookingEndpointsTests.Booking_AeroHub_CONFIRMED_becomes_Confirmed_with_pnr_and_ticket`, `…_AeroHub_FAILED_requires_review_and_is_not_refunded`, `…_AeroHub_timeout_requires_review_with_may_exist_note`, `…_SkyLink_ISSUING_becomes_Pending_then_job_makes_it_Confirmed`, `…_SkyLink_B022_requires_review` |
| AeroHub token expiry | `AeroHubSupplierTests.TokenExpiry_one_401_then_one_new_token_and_one_successful_retry` |

The booking-outcome tests go through the real AeroHub and SkyLink adapters, the logging handler and the database. Only the HTTP transport underneath is replaced by a stub. Other tests cover:
- several bookings for one user at the same moment never take the balance below zero;
- an unexpected supplier exception still ends in RequiresReview;
- the 5-minute rule of the Pending job, using a fake clock;
- a status change made by another booking-api instance is not overwritten;
- secrets are removed from the logs.

## Try it with curl

The commands below are for bash (Linux, macOS, Git Bash or WSL). In Windows PowerShell `curl` means `Invoke-WebRequest`, so run them from Git Bash or WSL instead. The search date must be today or later.

**1. Deposit**
```bash
curl -s -X POST http://localhost:8080/wallets/u1/deposit \
  -H "Content-Type: application/json" \
  -d '{"amountIqd": 2000000}'
# {"userId":"u1","balanceIqd":2000000}
```

**2. Search** (takes up to 5 s: AeroHub's mock waits 1–8 s, so it is often `TimedOut`)
```bash
curl -s "http://localhost:8080/search?from=BGW&to=IST&date=2026-11-20&adults=1"
```
Both mocks always return TK785 08:30. It appears **once**, from SkyLink at 446,000 IQD, because AeroHub's copy costs 455,000. Copy an `offerId` from `offers`: `SL-…` is a SkyLink offer, `AH-…` an AeroHub offer.

**3. Book**
```bash
curl -s -X POST http://localhost:8080/bookings \
  -H "Content-Type: application/json" \
  -H "Idempotency-Key: demo-1" \
  -d '{"userId":"u1","offerId":"<offerId>","passenger":{"firstName":"ALI","lastName":"HASSAN","birthDate":"1990-01-15"}}'
```
- A SkyLink offer usually returns `"status":"Pending"`. The mock issues the ticket 10–30 s after `/ticketing`.
- An AeroHub offer returns `Confirmed` (60%), `RequiresReview` after a `FAILED` (20%), or `RequiresReview` after our 15 s timeout (20%).

Run the same command again and you get the same `bookingId`, with no second debit. Change the body but keep `Idempotency-Key: demo-1` and you get `409`.

**4. Get the booking** (for a SkyLink booking, wait about 40 s first to see `Confirmed`)
```bash
curl -s http://localhost:8080/bookings/<bookingId>
```
```json
{ "bookingId": "b-…", "status": "Confirmed", "priceIqd": 446000, "supplier": "SkyLink",
  "pnr": "…", "ticketNumber": "…",
  "history": [
    { "at": "…", "from": null,      "to": "Created",   "note": "Booking created for offer SL-… (TK785 BGW-IST, SkyLink)." },
    { "at": "…", "from": "Created", "to": "Paid",      "note": "Wallet debited 446000 IQD" },
    { "at": "…", "from": "Paid",    "to": "Pending",   "note": "SkyLink order 917000000001 is ISSUING" },
    { "at": "…", "from": "Pending", "to": "Confirmed", "note": "SkyLink order 917000000001 is ISSUED." } ] }
```

**5. Wallet with its transactions**
```bash
curl -s http://localhost:8080/wallets/u1
```

## Design decisions

**Project layout.** One `BookingApi` project with folders: `Api/` (controllers), `Application/` (search, booking, wallet, pricing, the `IFlightSupplier` interface), `Domain/` (entities), `Infrastructure/` (EF Core, supplier adapters, HTTP logging, the background job). The folders keep the layers apart; separate assemblies would add ceremony without adding clarity at this size. Controllers call services, and services use the `DbContext` directly (no repository layer).

**How double debits are prevented.**
- **Idempotency.** `bookings.idempotency_key` has a unique index. The booking row is inserted *first*, inside the same transaction as the debit. A second request with the same key, even one arriving at the same millisecond, blocks on that index until the first transaction finishes. Its INSERT then fails with a unique violation, and the service returns the existing booking. So "one booking, one debit" is guaranteed by PostgreSQL, not by locks in application memory, and it holds across several booking-api instances.
- **Same key, different body.** Each booking stores a SHA-256 of the request body. The same key with a different body → `409`.
- **The debit.** It is one conditional `UPDATE wallets SET balance_iqd = balance_iqd - price WHERE user_id = … AND balance_iqd >= price`. Two bookings for the same user lock the same wallet row; the second re-checks the condition against the newly committed balance and updates 0 rows. 0 rows → roll back → `402`, so no booking row and no debit remain, and the key is free again. A `CHECK (balance_iqd >= 0)` constraint is the second line of defence.
- **What happens after the debit.** The debit, its ledger row and the `Paid` status commit together. Only then is the supplier called; no transaction stays open during a supplier call. The commit and everything after it ignore the client's cancellation: a client that disconnects after paying must not stop the booking half-way.

**How the parallel search and its timeout work.**
- One task per registered supplier, all started together and awaited with `Task.WhenAll`. They share one `CancellationTokenSource` with `CancelAfter(Search:TimeoutSeconds)`, linked to the request.
- Each task catches its own errors and returns a status: `Ok`, `TimedOut` (our budget ran out) or `Failed` (anything else, including SkyLink's `errorCode ≠ "0"` on HTTP 200). A slow or broken supplier therefore only empties its own part of the answer.
- The offers that arrived are priced centrally (`decimal`, rounded up to the next 1,000 IQD), de-duplicated (airline + flight number + departure to the minute, cheaper one kept) and sorted.
- They are then saved with the supplier's own reference, which never leaves the database, and an expiry time. The save uses the request's token, not the search budget, which may already have run out.

**After payment: no refund, no retry.**
- Each adapter turns the supplier's answer into one of four outcomes:
  - `Confirmed`: ticket issued;
  - `Pending`: SkyLink `ISSUING`;
  - `Refused`: AeroHub `FAILED`, SkyLink `B050` / `B022`, so no ticket was issued;
  - `Unknown`: timeout, unreadable answer, or an unexpected error.
- `Refused` and `Unknown` both become `RequiresReview`, with a note that says what happened and whether a ticket may exist, e.g. *"AeroHub did not answer within 15 s: a ticket may have been issued; check with AeroHub."* The money stays debited, and nothing calls the supplier again (the only repeated call is AeroHub's `/book` after a `401`, see assumption 14).
- Each supplier call has its own timeout (15 s, below AeroHub's 30 s hang).
- SkyLink's `buyerOrder` is our booking id, so a repeated order could never create a second order at SkyLink (`B029`).
- `POST /bookings` returns `200` for every paid booking. The `status` field carries the outcome.

**The Pending job.**
- A `BackgroundService` with a `PeriodicTimer` (10 s) creates a fresh DI scope on every tick. The work itself is in `PendingBookingProcessor`, which the tests call directly with a fake clock.
- It does not know about SkyLink: it asks the supplier named on each Pending booking (`GetStatusAsync`).
  - ISSUED → `Confirmed` with PNR and ticket.
  - CANCELLED or another final status → `RequiresReview`.
  - Still ISSUING, or the status call failed → unchanged, tried again on the next tick, until 5 minutes after the booking became Pending; then → `RequiresReview`.
- The supplier's answer is checked before the deadline, so a ticket issued at minute 6 still ends `Confirmed`.
- Every status change, and only a change, adds a history row.
- `Booking.Status` is a concurrency token. If two booking-api instances poll the same booking, only the first change is saved.

**Adding a third supplier.** All supplier-specific code lives in `Infrastructure/Suppliers/<Supplier>/`, behind `IFlightSupplier` (`Name`, `OfferIdPrefix`, `SearchAsync`, `BookAsync`, `GetStatusAsync`). A third supplier needs:
1. a class implementing `IFlightSupplier` that maps its JSON to `SupplierOffer` and its booking answers to the four outcomes above;
2. a registration method like `AddAeroHubSupplier()`: options validated on startup, a typed `HttpClient`, and the shared `SupplierLoggingHandler` with the list of that supplier's secret JSON fields;
3. one line in `Program.cs`, plus its currency in `Pricing:RatesToIqd` if it is a new currency.

Search, booking and the Pending job find suppliers through `IEnumerable<IFlightSupplier>` and stay unchanged. Pricing, de-duplication and logging come for free.

**Secrets and logging.**
- Secrets come only from configuration: environment variables in Docker, `dotnet user-secrets` in local development. They are never in code or in committed `appsettings*.json`.
- A `DelegatingHandler` on each supplier's `HttpClient` logs every request and response (method, URL, status, elapsed ms, body). It also logs the token call, the 401 retry and the job's status calls, without the adapters knowing.
- Before writing, it replaces the declared secret fields with `***` anywhere in the JSON (AeroHub `password`, `access_token`; SkyLink `sign`).
- It never logs headers, so the bearer token cannot leak. A body that is not valid JSON is not printed at all.
- The SkyLink `partnerKey` is never sent, only its md5 sign.

**Time and money types.** Supplier prices and rates are `decimal`; IQD amounts are `long`. Flight times are local airport times without offset, as the suppliers send them (`timestamp without time zone`). Audit times are UTC (`timestamptz`) and come from `TimeProvider`.

## Assumptions

Where the task does not decide, this is what the code does:

1. **One-way trips only.** A SkyLink solution with more than one flight (a round trip) is skipped and logged.
2. **One passenger per booking.** The booking request carries one `passenger` and returns one ticket. An offer searched with `adults > 1` may be priced for every adult, so booking it returns `400` before any debit, rather than risk overcharging with no refund allowed.
3. **Idempotency keys** are unique across all users, at most 200 characters.
4. **A repeat during the supplier call.** A repeated request that arrives while the first one is still waiting for the supplier returns the booking in its current state (e.g. `Paid`). `GET /bookings/{id}` shows the final state.
5. **Booking the same offer twice.** It can be booked again with a different key; each booking is a separate purchase and debit.
6. **Status codes.** Unknown offer → `404`, expired offer → `410`; both are checked before any debit. A retry with an already-used key returns the original booking even if the offer has expired since.
7. **Wallets.** A wallet is created by the first deposit; an unknown wallet → `404`. A deposit must be a whole number of IQD from 1 to 1,000,000,000 (our limit, keeping the balance far from the 64-bit maximum). A `userId` may be at most 64 characters.
8. **Search input.** `from` / `to` are 3 letters in any case (`bgw` = `BGW`). `date` must be today or later (UTC) and `adults` between 1 and 9. Anything else → `400`. A valid search returns `200` even when every supplier timed out or failed; the `suppliers` statuses say why.
9. **Supplier counts.** `suppliers[].count` is what that supplier returned, before de-duplication, so it shows the supplier's health.
10. **De-duplication ties.** Equal prices: the first supplier in registration order wins. Seconds in the departure time are ignored.
11. **SkyLink multi-segment flights.** `airline` and `flightNumber` come from the first segment.
12. **SkyLink `GET /order/{orderNum}`** is called without auth. The task puts `auth` in the request body and a GET has no body. A sign in the query string would end up in access logs.
13. **SkyLink `B029`** at `/order` means an order already exists for this booking, which may be ticketed, so it is treated as "a ticket may exist". A failure before `/order` returned an order number is treated as "no ticket", because SkyLink only issues after `/ticketing`.
14. **The one call repeated after payment** is AeroHub's `/book` after a `401`, as section 3.1 requires ("get a new token and retry once"). A `401` means AeroHub rejected the request before processing it, so no ticket can exist from the first attempt.
15. **Booking timeouts.** 15 s per supplier (the task only says AeroHub's 30 s hang must be cut). For SkyLink the 15 s cover `/order` and `/ticketing` together.
16. **Pending answers that end the wait.** A Pending booking whose order turns `CANCELLED` (or any status other than ISSUING/ISSUED) goes to `RequiresReview` at once, without waiting 5 minutes.
17. **`GET /bookings/{id}`** also returns `pnr` and `ticketNumber` (null until Confirmed); otherwise a booking confirmed later by the job could never show its ticket.
18. **Several booking-api instances** may run the Pending job at the same time; only the first status change for a booking is saved.
19. **The mocks** keep no offer state (`offer_token` / `solutionId` on booking are not checked) and lose all orders when restarted. Unknown order numbers return `B001` (our code, not in the task). Order numbers restart at `917000000001`.

## Not finished / next steps

- **No authentication on booking-api.** `userId` in the path and body is trusted, as the task's API shape implies. Production would take the user from a token (e.g. JWT) and ignore client-supplied ids.
- **A booking can stay `Paid`.** This happens if the process dies, or the final save fails, between the debit commit and saving the supplier's answer. Nothing moves it on. Next: a sweep that marks `Paid` bookings older than the supplier timeout as `RequiresReview` ("outcome not recorded; check with the supplier").
- **No AeroHub reconciliation.** AeroHub has no booking lookup and no idempotency key, so a timed-out AeroHub booking can only be settled by hand. If AeroHub added a lookup, it would get a reconciliation job like SkyLink's Pending job.
- **Personal data in the logs.** Supplier bodies are logged as the task asks, so passenger names and birth dates appear in the logs (secrets do not). Production would mask them or send these logs to an access-controlled store.
- **Expired offers are never deleted.** Next: a cleanup job for expired offers that no booking references.
- **Duplicate polling across instances.** Each instance still calls `GET /order` for the same Pending booking (harmless reads). Next, if it mattered: one poller instance or a lease per booking.
- **Error responses.** `402` and `410` have no `type` / `title` (ASP.NET has no default for these codes); the `detail` text explains the error.
