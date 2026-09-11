# Room Booking

Book a meeting room by the hour. Two .NET services, a PostgreSQL database each, a React
front end, and one `docker compose up`.

The interesting part is not the CRUD — it is what happens when two people click the same
slot at the same moment, and what "08:00" means for a room in another country.

---

## Running it

Requires Docker. Nothing else — no local .NET or Node needed to run the app.

```bash
cp .env.example .env      # then fill in the values, see below
docker compose up -d --build
```

Open **http://localhost:3000**, register an account, and book a slot.

`.env` needs:

| Variable | Notes |
| --- | --- |
| `POSTGRES_USER`, `POSTGRES_PASSWORD` | shared by both databases |
| `AUTH_DB_NAME`, `BOOKING_DB_NAME` | two separate databases — see below |
| `JWT_KEY` | **at least 32 characters**, HMAC-SHA256 refuses anything shorter |
| `JWT_ISSUER`, `JWT_AUDIENCE` | any string, must match on both services |

Migrations run automatically on startup, and four rooms are seeded.

### What runs where

| | URL | |
| --- | --- | --- |
| Web app | http://localhost:3000 | the only thing a user touches |
| REST API | http://localhost:8081 | dev only |
| API reference | http://localhost:8081/scalar/v1 | dev only |
| Auth gRPC | `localhost:8080` | dev only, for `grpcurl` |
| Databases | `5432` auth, `5433` booking | dev only |

Only port 3000 is published in `compose.yaml`. Everything else lives in
`compose.override.yaml`, which Compose applies automatically for local development and
which production would simply not use.

### Tests

```bash
dotnet test RoomBooking.slnx
```

53 tests, about 10 seconds. Integration tests start real PostgreSQL containers through
Testcontainers, so Docker has to be running.

---

## Architecture

```
Browser
   │  REST / JSON
   ▼
nginx ──► BookingService ──gRPC──► AuthGrpcService
              │                         │
              ▼                         ▼
          booking-db                 auth-db
```

**Why gRPC for one hop and REST for the other.** gRPC is built for services talking to
each other: a typed contract, generated clients, no hand-written HTTP plumbing. A browser
cannot speak it directly, so the public API is REST. Auth is internal and never exposed.

**Why nginx proxies `/api`.** The browser only ever talks to one origin, so CORS never
comes up — not in development (Vite's proxy) and not in production (nginx). No
`AddCors` anywhere in the codebase.

**Why one repository.** Repository layout is source control, not architecture. The
services already deploy independently and own their data, which is what makes them
services. Splitting the repo would mean versioning `auth.proto` as a package, or
copy-pasting it and watching the copies drift.

**Why `RoomBooking.Contracts`.** The `.proto` used to be compiled into both services —
one as server, one as client — which produced two different `Credentials` types with the
same name. Anything referencing both projects failed to compile. The contract now lives
in one library with `GrpcServices="Both"`.

---

## Design decisions

### Double booking is prevented by the database, not by the code

`Bookings` has a unique index on `(RoomId, SlotStart)`. The handler also checks whether
the slot is taken before inserting, but **that check guarantees nothing on its own**:

```
Request A: "is 14:00 free?" → yes
Request B: "is 14:00 free?" → yes      ← A has not inserted yet
Request A: INSERT                      ✓
Request B: INSERT                      ✓ ← two bookings
```

The gap is between two round trips to the database, so no amount of C# closes it. Only
the database sees both inserts. The two pieces do different jobs: the check produces a
friendly `409` on the normal path, and the unique index is what is actually true when
requests interleave. `Users.Login` works the same way.

The filtered `catch` matters too — only `PostgresErrorCodes.UniqueViolation` turns into
"already booked". Any other database failure stays a real error instead of being
disguised.

### Working hours belong to the room's time zone, not to UTC

Each room carries an IANA zone (`Europe/Warsaw`, `Europe/London`, `America/New_York`).
Working hours are 08:00–19:00 **in that zone**; instants are stored and compared in UTC.

Hardcoding the range in UTC would be wrong, because a zone's offset is not constant:

```
Warsaw, local 08:00  →  07:00Z in January   (UTC+1)
                     →  06:00Z in July      (UTC+2)
```

A fixed UTC range would silently shift the room's opening time by an hour twice a year.
Slot generation also skips local hours swallowed by a spring-forward transition, which
simply do not exist.

The same instant can therefore be valid for one room and rejected by another:

```
2026-09-15T06:00:00Z
  Warsaw room   → 201   (08:00 there)
  New York room → 400   (02:00 there)
```

The UI renders every time in the room's zone — the way a hotel states check-in in the
hotel's local time — and shows the viewer's own time on hover when the zones differ.

### BookingService verifies tokens itself

A JWT is self-contained: the signature proves it. Calling AuthService on every request
would add a network hop and make Auth a single point of failure for every booking, in
exchange for nothing. Auth is contacted only to *issue* a token.

The user id always comes from the token's claims, never from the request body —
otherwise anyone could act as anyone by sending a different id.

### Separate databases

Auth owns `Users`; Booking owns `Rooms` and `Bookings`. Booking stores the user id from
the token with no foreign key across the boundary. Two services sharing one database is
the usual way "microservices" turn into a distributed monolith.

### Cancelling someone else's booking returns 404, not 403

`403 Forbidden` would confirm that the booking exists. `404` reveals nothing.

### Past bookings are history, not a to-do list

`/api/bookings/my` returns upcoming bookings by default, soonest first. History is
available on request:

```
/api/bookings/my               upcoming
/api/bookings/my?scope=past    past, most recent first
/api/bookings/my?scope=all     everything
```

An unrecognised `scope` is a `400` rather than a silent fallback.

---

## API

| | | |
| --- | --- | --- |
| `POST` | `/api/auth/register` | forwarded to AuthService over gRPC |
| `POST` | `/api/auth/login` | forwarded to AuthService over gRPC |
| `GET` | `/api/rooms` | anonymous |
| `GET` | `/api/rooms/{id}/availability?date=` | `date` is a day in the room's zone |
| `POST` | `/api/bookings` | token required |
| `GET` | `/api/bookings/my?scope=` | token required |
| `DELETE` | `/api/bookings/{id}` | token required, own bookings only |
| `GET` | `/api/health` | |

gRPC statuses from AuthService are mapped to HTTP rather than surfacing as `500`:
`Unauthenticated`→401, `AlreadyExists`→409, `InvalidArgument`→400, `Unavailable`→503.

---

## Tests

```
Unit          13   validator, token generation, slot rules incl. daylight saving
Integration   40   auth service, booking API, database constraints, the gRPC seam
```

Integration tests run against real PostgreSQL via Testcontainers. The EF in-memory
provider would have been faster and useless here: **it does not enforce unique indexes**,
so the one behaviour this project is about would pass in tests and fail in production.

Two tests deserve a mention:

- `Only_one_of_many_simultaneous_bookings_wins` fires eight concurrent requests and
  expects exactly one `201`. It asserts the *outcome*, which holds whether the check or
  the index caught the duplicate — a genuine race is almost impossible to force from a
  test.
- `The_database_refuses_two_bookings_for_one_room_and_slot` goes around the API and
  inserts directly, so it proves the constraint itself exists. That is the one that
  would fail if somebody dropped the index.

---

## Deliberately not done

- **End-to-end tests.** Unit and integration are covered; driving the browser with
  Playwright was out of scope for the time available.
- **Refresh tokens.** Tokens last 60 minutes, after which you sign in again.
- **Rate limiting.** Nothing slows down password guessing on `/api/auth/login`.
- **TLS.** Everything is plain HTTP inside the Compose network. A real deployment would
  terminate TLS at the edge.
- **Kubernetes.** Compose is the target here; production would be one deployment per
  service.
- **Bookings of arbitrary length.** Fixed hourly slots are what let a single unique index
  prevent conflicts. Arbitrary ranges need overlap detection — in PostgreSQL, an
  exclusion constraint over a `tstzrange`.

---

## Layout

```
AuthGrpcService/        gRPC service — registration, login, token issuing
BookingService/         REST API — rooms, availability, bookings
RoomBooking.Contracts/  the shared .proto and its generated code
web/                    React + Vite + shadcn/ui, served by nginx
tests/                  xUnit, Testcontainers, WebApplicationFactory
compose.yaml            production shape
compose.override.yaml   development extras, applied automatically
```
