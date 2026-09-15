# Room Booking

Book a meeting room by the hour. One .NET service, one PostgreSQL database, a React front
end behind nginx, and a single `docker compose up`.

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
| `POSTGRES_USER`, `POSTGRES_PASSWORD`, `POSTGRES_DB` | any values; the database is created on first start |
| `JWT_KEY` | **at least 32 characters**, HMAC-SHA256 refuses anything shorter |
| `JWT_ISSUER`, `JWT_AUDIENCE` | any strings |

Migrations run automatically on startup, and four rooms are seeded.

### What runs where

| | URL | |
| --- | --- | --- |
| Web app | http://localhost:3000 | the only thing a user touches |
| API | http://localhost:8080 | dev only |
| API reference | http://localhost:8080/scalar/v1 | dev only |
| Database | `localhost:5432` | dev only |

Only port 3000 is published in `compose.yaml`. The rest lives in `compose.override.yaml`,
which Compose applies automatically for local development and which production would
simply not use.

### Tests

```bash
dotnet test RoomBooking.slnx
```

57 tests, about 10 seconds. Integration tests start a real PostgreSQL container through
Testcontainers, so Docker has to be running.

### Migrations

Migrations run on startup, so day to day you never touch them. When you change a model:

```bash
cd RoomBooking
dotnet ef migrations add <Name>
```

That works with nothing configured - it only builds the model. Commands that touch a
database (`database update`, `migrations list`) read `ConnectionStrings__DefaultConnection`
from the environment, the same variable the app uses:

```powershell
$env:ConnectionStrings__DefaultConnection = "Host=localhost;Port=5432;Database=<POSTGRES_DB>;Username=<POSTGRES_USER>;Password=<POSTGRES_PASSWORD>"
```

Nothing is hardcoded in the design-time factory; it reads `appsettings*.json` and the
environment exactly as the running app does.

---

## Architecture

```
Browser
   │  REST / JSON
   ▼
nginx ──► RoomBooking API ──► PostgreSQL
           auth, rooms, bookings
```

**One service, on purpose.** An earlier iteration split authentication into a separate
gRPC service with its own database — the repository name is a leftover from that. It was
merged back: for this domain one service is the right size, and the split was adding
moving parts without adding capability. The boundary it demonstrated is still visible in
the code — the token is the only thing that carries identity between `/api/auth` and the
rest — so splitting again later is a matter of moving files, not redesigning.

**Why nginx proxies `/api`.** The browser only ever talks to one origin, so CORS never
comes up — not in development (Vite's proxy) and not in production (nginx). There is no
`AddCors` anywhere in the codebase.

**Why JWT rather than a session cookie.** The API is stateless: any number of replicas
can verify a token with the shared key, no session store, no sticky sessions. The cost is
that a token cannot be revoked before it expires — see "Deliberately not done".

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
simply do not exist. The same instant can therefore be valid for one room and rejected by
another. The UI renders every time in the room's zone — the way a hotel states check-in in
the hotel's local time — and shows the viewer's own time on hover when the zones differ.

If a room's zone cannot be resolved — a typo in seed data, or a runtime image without
tzdata — the service refuses to start, with a message naming the zone. Better at deploy
time, when someone is watching, than on the first booking.

### The user id always comes from the token

Never from the request body — otherwise anyone could act as anyone by sending a
different id. `CurrentUser.IdOrNull` is the only place that reads it.

### Cancelling someone else's booking returns 404, not 403

`403 Forbidden` would confirm that the booking exists. `404` reveals nothing. Login works
the same way: an unknown user and a wrong password get the identical response, so the
API cannot be used to enumerate logins.

### Past bookings are history, not a to-do list

`/api/bookings/my` returns upcoming bookings by default, soonest first. History is
available on request:

```
/api/bookings/my               upcoming
/api/bookings/my?scope=past    past, most recent first
/api/bookings/my?scope=all     everything
```

An unrecognised `scope` is a `400` rather than a silent fallback.

### Validation is injected, not located

`CredentialsValidator` (FluentValidation) is a constructor dependency of the register
handler. An earlier version resolved it from `IServiceProvider` inside a generic gRPC
interceptor — a service locator. Constructor injection keeps every dependency visible in
the signature and lets the container verify the graph at startup.

Only registration is validated. A malformed login on `/login` simply matches nobody and
gets the same `401` as a wrong password; returning `400` there would leak the rules.

---

## API

| | | |
| --- | --- | --- |
| `POST` | `/api/auth/register` | `409` if the login is taken |
| `POST` | `/api/auth/login` | `401` for any failure, same message |
| `GET` | `/api/rooms` | anonymous |
| `GET` | `/api/rooms/{id}/availability?date=` | `date` is a day in the room's zone |
| `POST` | `/api/bookings` | token required |
| `GET` | `/api/bookings/my?scope=` | token required |
| `DELETE` | `/api/bookings/{id}` | token required, own bookings only |
| `GET` | `/api/health` | |

Errors are RFC 9457 problem details; `detail` carries the human-readable reason.

---

## Tests

```
Unit          24   validator, token generation, slot rules incl. daylight saving
Integration   33   auth API, booking API, database constraints
```

Integration tests boot the real app with `WebApplicationFactory` against real PostgreSQL
via Testcontainers. Nothing is mocked. The EF in-memory provider would have been faster
and useless here: **it does not enforce unique indexes**, so the one behaviour this
project is about would pass in tests and fail in production.

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

- **Rate limiting.** Nothing slows down password guessing on `/api/auth/login`. BCrypt
  makes each attempt expensive in CPU, which is also why this matters: it is a denial
  of service vector as much as a security one.
- **Refresh tokens.** Tokens last 60 minutes and cannot be revoked earlier.
- **A foreign key from `Bookings.UserId` to `Users`.** Users cannot be deleted, so
  nothing can orphan a booking today. It is a one-line change when that flow exists.
- **End-to-end tests.** Unit and integration are covered; driving the browser with
  Playwright was out of scope.
- **TLS.** Everything is plain HTTP inside the Compose network. A real deployment would
  terminate TLS at the edge.
- **Bookings of arbitrary length.** Fixed hourly slots are what let a single unique index
  prevent conflicts. Arbitrary ranges need overlap detection — in PostgreSQL, an
  exclusion constraint over a `tstzrange`.

---

## Layout

```
RoomBooking/            the API: auth, rooms, bookings
  Endpoints/            one file per resource, all handler logic lives here
  Contracts/            request/response records and the slot rules
  Data/                 DbContext, design-time factory, migrations
  Models/               EF entities
  Services/             JwtTokenService
  Validators/           FluentValidation rules
web/                    React + Vite + shadcn/ui, served by nginx
tests/                  xUnit, Testcontainers, WebApplicationFactory
compose.yaml            production shape
compose.override.yaml   development extras, applied automatically
```
