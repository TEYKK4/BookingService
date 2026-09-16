export type Room = { id: number; name: string; capacity: number; timeZoneId: string }
export type Slot = { slotStart: string; isTaken: boolean; isMine: boolean }
export type Booking = {
  id: number
  roomId: number
  roomName: string
  timeZoneId: string
  slotStart: string
  createdAt: string
}

const TOKEN_KEY = "roombooking.token"

export const token = {
  get: () => localStorage.getItem(TOKEN_KEY),
  set: (value: string) => localStorage.setItem(TOKEN_KEY, value),
  clear: () => localStorage.removeItem(TOKEN_KEY),
}

/** The API answers errors with RFC 9457 problem details; `detail` is the human part. */
class ApiError extends Error {}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const jwt = token.get()

  const response = await fetch(`/api${path}`, {
    ...init,
    headers: {
      "Content-Type": "application/json",
      ...(jwt ? { Authorization: `Bearer ${jwt}` } : {}),
      ...init.headers,
    },
  })

  if (response.status === 401) {
    token.clear()
    throw new ApiError("Your session has expired. Please sign in again.")
  }

  if (!response.ok) {
    const problem = await response.json().catch(() => null)
    throw new ApiError(problem?.detail ?? problem?.title ?? `Request failed (${response.status})`)
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T)
}

export const api = {
  register: (login: string, password: string) =>
    request<{ token: string }>("/auth/register", {
      method: "POST",
      body: JSON.stringify({ login, password }),
    }),

  login: (login: string, password: string) =>
    request<{ token: string }>("/auth/login", {
      method: "POST",
      body: JSON.stringify({ login, password }),
    }),

  rooms: () => request<Room[]>("/rooms"),

  availability: (roomId: number, date: string) =>
    request<Slot[]>(`/rooms/${roomId}/availability?date=${date}`),

  book: (roomId: number, slotStart: string) =>
    request<Booking>("/bookings", {
      method: "POST",
      body: JSON.stringify({ roomId, slotStart }),
    }),

  myBookings: (scope: "upcoming" | "past" = "upcoming") =>
    request<Booking[]>(`/bookings/my?scope=${scope}`),

  cancel: (bookingId: number) => request<void>(`/bookings/${bookingId}`, { method: "DELETE" }),
}

/**
 * The API sends UTC instants. Working hours belong to the room's own zone, so
 * every instant is rendered in that zone - never in UTC and never in the
 * viewer's zone, the same way a hotel states check-in in the hotel's local time.
 */
const formatter = (timeZone: string, options: Intl.DateTimeFormatOptions) =>
  new Intl.DateTimeFormat("en-GB", { timeZone, ...options })

export const inZone = {
  hour: (iso: string, timeZone: string) =>
    formatter(timeZone, { hour: "2-digit", minute: "2-digit", hour12: false }).format(new Date(iso)),

  /** YYYY-MM-DD, the shape <input type="date"> and the API both expect. */
  day: (iso: string | Date, timeZone: string) =>
    new Intl.DateTimeFormat("en-CA", { timeZone }).format(new Date(iso)),

  full: (iso: string, timeZone: string) =>
    `${inZone.day(iso, timeZone)} ${inZone.hour(iso, timeZone)}`,

  /** Short zone label, e.g. "CEST". */
  label: (iso: string | Date, timeZone: string) =>
    formatter(timeZone, { timeZoneName: "short" }).formatToParts(new Date(iso))
      .find((part) => part.type === "timeZoneName")?.value ?? timeZone,
}

export const viewerZone = Intl.DateTimeFormat().resolvedOptions().timeZone

/** "America/New_York" → "New York": the city is what a person reads, the id is what the API speaks. */
export const zoneCity = (timeZone: string) =>
  (timeZone.split("/").pop() ?? timeZone).replace(/_/g, " ")

export const todayIn = (timeZone: string) => inZone.day(new Date(), timeZone)
