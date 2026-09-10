import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import { api, formatUtc, today, type Booking, type Room, type Slot } from "@/lib/api"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select"

export function BookingBoard({ onSignOut }: { onSignOut: () => void }) {
  const [rooms, setRooms] = useState<Room[]>([])
  const [roomId, setRoomId] = useState<number | null>(null)
  const [date, setDate] = useState(today())
  const [slots, setSlots] = useState<Slot[]>([])
  const [bookings, setBookings] = useState<Booking[]>([])
  const [busySlot, setBusySlot] = useState<string | null>(null)

  const fail = (error: unknown) => toast.error((error as Error).message)

  const loadBookings = useCallback(async () => {
    try {
      setBookings(await api.myBookings())
    } catch (error) {
      fail(error)
    }
  }, [])

  const loadSlots = useCallback(async () => {
    if (roomId === null) return
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
    loadBookings()
  }, [loadBookings])

  useEffect(() => { loadSlots() }, [loadSlots])

  async function book(slot: Slot) {
    if (roomId === null) return
    setBusySlot(slot.slotStart)

    try {
      await api.book(roomId, slot.slotStart)
      toast.success(`Booked ${formatUtc.full(slot.slotStart)}`)
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

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-4xl flex-col gap-6 p-4 md:p-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Room Booking</h1>
          <p className="text-sm text-muted-foreground">
            Hourly slots, {formatUtc.hour("1970-01-01T08:00:00Z")}–{formatUtc.hour("1970-01-01T20:00:00Z")} UTC
          </p>
        </div>
        <Button variant="outline" onClick={onSignOut}>Sign out</Button>
      </header>

      <Card>
        <CardHeader>
          <CardTitle>Find a slot</CardTitle>
          <CardDescription>Pick a room and a day, then click a free hour.</CardDescription>
        </CardHeader>

        <CardContent className="flex flex-col gap-4">
          <div className="flex flex-wrap gap-4">
            <div className="flex min-w-48 flex-col gap-2">
              <Label>Room</Label>
              <Select
                value={roomId?.toString() ?? ""}
                onValueChange={(value) => setRoomId(Number(value))}
              >
                <SelectTrigger><SelectValue placeholder="Choose a room" /></SelectTrigger>
                <SelectContent>
                  {rooms.map((room) => (
                    <SelectItem key={room.id} value={room.id.toString()}>
                      {room.name} · {room.capacity} people
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
                value={date}
                min={today()}
                onChange={(e) => setDate(e.target.value)}
              />
            </div>
          </div>

          <div className="grid grid-cols-3 gap-2 sm:grid-cols-4 md:grid-cols-6">
            {slots.map((slot) => (
              <Button
                key={slot.slotStart}
                variant={slot.isMine ? "default" : slot.isTaken ? "secondary" : "outline"}
                disabled={slot.isTaken || busySlot === slot.slotStart}
                onClick={() => book(slot)}
                title={slot.isMine ? "Yours" : slot.isTaken ? "Taken" : "Free"}
              >
                {formatUtc.hour(slot.slotStart)}
              </Button>
            ))}
          </div>

          {slots.length === 0 && (
            <p className="text-sm text-muted-foreground">No slots to show for this day.</p>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            My bookings <Badge variant="secondary">{bookings.length}</Badge>
          </CardTitle>
        </CardHeader>

        <CardContent className="flex flex-col gap-2">
          {bookings.length === 0 && (
            <p className="text-sm text-muted-foreground">Nothing booked yet.</p>
          )}

          {bookings.map((booking) => (
            <div
              key={booking.id}
              className="flex items-center justify-between rounded-md border px-3 py-2"
            >
              <div>
                <p className="font-medium">{booking.roomName}</p>
                <p className="text-sm text-muted-foreground">{formatUtc.full(booking.slotStart)}</p>
              </div>
              <Button variant="ghost" size="sm" onClick={() => cancel(booking)}>Cancel</Button>
            </div>
          ))}
        </CardContent>
      </Card>
    </main>
  )
}
