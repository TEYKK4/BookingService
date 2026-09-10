export type Room = { id: number; name: string; capacity: number }
export type Slot = { slotStart: string; isTaken: boolean; isMine: boolean }
export type Booking = {
  id: number
  roomId: number
  roomName: string
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

  myBookings: () => request<Booking[]>("/bookings/my"),

  cancel: (bookingId: number) => request<void>(`/bookings/${bookingId}`, { method: "DELETE" }),
}

/** Slots are UTC whole hours - show them as such so they match what the API validates. */
export const formatUtc = {
  hour: (iso: string) => `${new Date(iso).getUTCHours().toString().padStart(2, "0")}:00`,
  date: (iso: string) => new Date(iso).toISOString().slice(0, 10),
  full: (iso: string) => `${formatUtc.date(iso)} ${formatUtc.hour(iso)} UTC`,
}

export const today = () => new Date().toISOString().slice(0, 10)
