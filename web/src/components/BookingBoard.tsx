import { useCallback, useEffect, useMemo, useState } from "react"
import { toast } from "sonner"
import {
  api, inZone, todayIn, viewerZone, zoneCity,
  type Booking, type Room, type Slot,
} from "@/lib/api"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select"

type Props = {
  onSignOut: () => void
  isAdmin: boolean
  onOpenAdmin: () => void
}

export function BookingBoard({ onSignOut, isAdmin, onOpenAdmin }: Props) {
  const [rooms, setRooms] = useState<Room[]>([])
  const [roomId, setRoomId] = useState<number | null>(null)
  const [date, setDate] = useState<string | null>(null)
  const [slots, setSlots] = useState<Slot[]>([])
  const [busySlot, setBusySlot] = useState<string | null>(null)
  const [scope, setScope] = useState<"upcoming" | "past">("upcoming")

  // Two lists on purpose: the grid always needs upcoming bookings to know which
  // slot is cancellable, while the card below shows whichever scope is selected.
  const [upcoming, setUpcoming] = useState<Booking[]>([])
  const [listed, setListed] = useState<Booking[]>([])

  const room = useMemo(() => rooms.find((r) => r.id === roomId) ?? null, [rooms, roomId])
  const zone = room?.timeZoneId ?? viewerZone

  // Base UI's SelectValue shows the raw value unless the Select knows the labels.
  const roomItems = useMemo(
    () => rooms.map((r) => ({
      value: r.id.toString(),
      label: `${r.name} · ${r.capacity} people · ${zoneCity(r.timeZoneId)}`,
    })),
    [rooms])

  const fail = (error: unknown) => toast.error((error as Error).message)

  const loadBookings = useCallback(async () => {
    try {
      const mine = await api.myBookings("upcoming")
      setUpcoming(mine)
      setListed(scope === "past" ? await api.myBookings("past") : mine)
    } catch (error) {
      fail(error)
    }
  }, [scope])

  const loadSlots = useCallback(async () => {
    if (roomId === null || date === null) return
    try {
      setSlots(await api.availability(roomId, date))
    } catch (error) {
      fail(error)
    }
  }, [roomId, date])

  useEffect(() => {
    api.rooms()
      .then((loaded) => {
        setRooms(loaded)
        setRoomId((current) => current ?? loaded[0]?.id ?? null)
      })
      .catch(fail)
  }, [])

  useEffect(() => { loadBookings() }, [loadBookings])

  // The calendar day is a day in the room's zone, so it follows the room.
  useEffect(() => {
    if (room) setDate((current) => current ?? todayIn(room.timeZoneId))
  }, [room])

  useEffect(() => { loadSlots() }, [loadSlots])

  async function book(slot: Slot) {
    if (roomId === null) return
    setBusySlot(slot.slotStart)

    try {
      await api.book(roomId, slot.slotStart)
      toast.success(`Booked ${inZone.full(slot.slotStart, zone)}`)
      await Promise.all([loadSlots(), loadBookings()])
    } catch (error) {
      fail(error)
      await loadSlots()
    } finally {
      setBusySlot(null)
    }
  }

  async function cancel(booking: Booking) {
    try {
      await api.cancel(booking.id)
      toast.success("Booking cancelled")
      await Promise.all([loadSlots(), loadBookings()])
    } catch (error) {
      fail(error)
    }
  }

  /** The API refuses past slots, so they are not shown at all. */
  const isPast = (slot: Slot) => new Date(slot.slotStart).getTime() < Date.now()
  const openSlots = slots.filter((slot) => !isPast(slot))

  const zoneLabel = room && date ? inZone.label(`${date}T12:00:00Z`, room.timeZoneId) : ""
  const showsAnotherZone = room !== null && room.timeZoneId !== viewerZone

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-4xl flex-col gap-6 p-4 md:p-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Room Booking</h1>
          <p className="text-sm text-muted-foreground">
            Hourly slots, 08:00&ndash;19:00 in each room&rsquo;s own time zone
          </p>
        </div>
        <div className="flex gap-2">
          {isAdmin && <Button variant="secondary" onClick={onOpenAdmin}>Admin</Button>}
          <Button variant="outline" onClick={onSignOut}>Sign out</Button>
        </div>
      </header>

      <Card>
        <CardHeader>
          <CardTitle>Find a slot</CardTitle>
          <CardDescription>
            {room
              ? <>Times shown in <strong>{zoneCity(room.timeZoneId)} time</strong>{zoneLabel && ` (${zoneLabel})`}</>
              : "Pick a room and a day, then click a free hour."}
          </CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-wrap gap-4">
            <div className="flex min-w-72 flex-col gap-2">
              <Label>Room</Label>
              <Select
                items={roomItems}
                value={roomId?.toString() ?? ""}
                onValueChange={(value) => {
                  const next = rooms.find((r) => r.id === Number(value))
                  setRoomId(Number(value))
                  if (next) setDate(todayIn(next.timeZoneId))
                }}
              >
                <SelectTrigger><SelectValue placeholder="Choose a room" /></SelectTrigger>
                <SelectContent>
                  {rooms.map((option) => (
                    <SelectItem key={option.id} value={option.id.toString()}>
                      {option.name} · {option.capacity} people · {zoneCity(option.timeZoneId)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="date">Date</Label>
              <Input
                id="date"
                type="date"
                value={date ?? ""}
                min={room ? todayIn(room.timeZoneId) : undefined}
                onChange={(e) => setDate(e.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-6">
            {openSlots.map((slot) => {
              const state = slot.isMine ? "yours" : slot.isTaken ? "taken" : "free"
              const mine = upcoming.find(
                (b) => b.roomId === roomId
                  && Date.parse(b.slotStart) === Date.parse(slot.slotStart))

              const yourTime = showsAnotherZone
                ? ` (${inZone.hour(slot.slotStart, viewerZone)} your time)`
                : ""

              return (
                <Button
                  key={slot.slotStart}
                  variant={state === "yours" ? "default" : state === "free" ? "outline" : "secondary"}
                  disabled={state === "taken" || busySlot === slot.slotStart}
                  onClick={() => (mine ? cancel(mine) : book(slot))}
                  title={
                    (state === "yours" ? "Booked by you - click to cancel"
                      : state === "taken" ? "Booked by someone else"
                        : "Free - click to book") + yourTime
                  }
                >
                  {inZone.hour(slot.slotStart, zone)}
                </Button>
              )
            })}
          </div>

          {openSlots.length === 0 && (
            <p className="text-sm text-muted-foreground">
              {slots.length === 0
                ? "No slots to show for this day."
                : "Every hour for this day has already passed."}
            </p>
          )}

          {openSlots.length > 0 && (
            <div className="flex flex-wrap items-center gap-4 text-xs text-muted-foreground">
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded-sm border" /> Free
              </span>
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded-sm bg-secondary opacity-50" /> Taken
              </span>
              <span className="flex items-center gap-1.5">
                <span className="size-3 rounded-sm bg-primary" /> Yours
              </span>
              {showsAnotherZone && (
                <span>Your zone is {zoneCity(viewerZone)} &mdash; hover a slot to see it</span>
              )}
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex flex-wrap items-center gap-2">
            {scope === "upcoming" ? "Upcoming" : "Past"} bookings
            <Badge variant="secondary">{listed.length}</Badge>
            <Button
              variant="link"
              size="sm"
              className="ml-auto"
              onClick={() => setScope(scope === "upcoming" ? "past" : "upcoming")}
            >
              {scope === "upcoming" ? "Show past" : "Show upcoming"}
            </Button>
          </CardTitle>
        </CardHeader>

        <CardContent className="flex flex-col gap-2">
          {listed.length === 0 && (
            <p className="text-sm text-muted-foreground">
              {scope === "upcoming" ? "Nothing booked yet." : "No past bookings."}
            </p>
          )}

          {listed.map((booking) => (
            <div
              key={booking.id}
              className="flex items-center justify-between rounded-md border px-3 py-2"
            >
              <div>
                <p className="font-medium">{booking.roomName}</p>
                <p className="text-sm text-muted-foreground">
                  {inZone.full(booking.slotStart, booking.timeZoneId)} · {zoneCity(booking.timeZoneId)}
                </p>
              </div>
              {scope === "upcoming" && (
                <Button variant="ghost" size="sm" onClick={() => cancel(booking)}>Cancel</Button>
              )}
            </div>
          ))}
        </CardContent>
      </Card>
    </main>
  )
}
