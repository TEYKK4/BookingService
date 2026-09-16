import { useCallback, useEffect, useState } from "react"
import { toast } from "sonner"
import {
  api, inZone, zoneCity,
  type AdminBooking, type AdminRoom, type AdminUser, type Me, type SaveRoom, type Scope,
} from "@/lib/api"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select"
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"

type Props = {
  me: Me
  onBack: () => void
  onSignOut: () => void
}

const fail = (error: unknown) => toast.error((error as Error).message)

/** Offered as suggestions only; the server validates whatever is typed. */
const COMMON_ZONES = [
  "Europe/Warsaw", "Europe/London", "Europe/Berlin", "Europe/Kyiv",
  "America/New_York", "America/Los_Angeles", "Asia/Tokyo", "Etc/UTC",
]

export function AdminPanel({ me, onBack, onSignOut }: Props) {
  return (
    <main className="mx-auto flex min-h-svh w-full max-w-5xl flex-col gap-6 p-4 md:p-8">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-2xl font-semibold">Admin</h1>
          <p className="text-sm text-muted-foreground">Signed in as {me.login}</p>
        </div>
        <div className="flex gap-2">
          <Button variant="secondary" onClick={onBack}>Back to booking</Button>
          <Button variant="outline" onClick={onSignOut}>Sign out</Button>
        </div>
      </header>

      <Tabs defaultValue="rooms">
        <TabsList>
          <TabsTrigger value="rooms">Rooms</TabsTrigger>
          <TabsTrigger value="bookings">Bookings</TabsTrigger>
          <TabsTrigger value="users">Users</TabsTrigger>
        </TabsList>
        <TabsContent value="rooms"><RoomsTab /></TabsContent>
        <TabsContent value="bookings"><BookingsTab /></TabsContent>
        <TabsContent value="users"><UsersTab me={me} /></TabsContent>
      </Tabs>
    </main>
  )
}

// ---------------------------------------------------------------- rooms

const EMPTY_FORM: SaveRoom = { name: "", capacity: 4, timeZoneId: "Europe/Warsaw" }

function RoomsTab() {
  const [rooms, setRooms] = useState<AdminRoom[]>([])
  const [form, setForm] = useState<SaveRoom>(EMPTY_FORM)
  const [editingId, setEditingId] = useState<number | null>(null)
  const [busy, setBusy] = useState(false)

  const load = useCallback(() => api.admin.rooms().then(setRooms).catch(fail), [])
  useEffect(() => { load() }, [load])

  function startEdit(room: AdminRoom) {
    setEditingId(room.id)
    setForm({ name: room.name, capacity: room.capacity, timeZoneId: room.timeZoneId })
  }

  function reset() {
    setEditingId(null)
    setForm(EMPTY_FORM)
  }

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setBusy(true)
    try {
      if (editingId === null) {
        await api.admin.createRoom(form)
        toast.success(`Room "${form.name}" created`)
      } else {
        await api.admin.updateRoom(editingId, form)
        toast.success(`Room "${form.name}" updated`)
      }
      reset()
      await load()
    } catch (error) {
      fail(error)
    } finally {
      setBusy(false)
    }
  }

  async function toggleActive(room: AdminRoom) {
    try {
      await api.admin.setRoomActive(room.id, !room.isActive)
      toast.success(`Room "${room.name}" ${room.isActive ? "deactivated" : "activated"}`)
      await load()
    } catch (error) {
      fail(error)
    }
  }

  return (
    <div className="flex flex-col gap-4">
      <Card>
        <CardHeader>
          <CardTitle>{editingId === null ? "New room" : `Edit room #${editingId}`}</CardTitle>
          <CardDescription>
            Working hours are 08:00–19:00 in the room&rsquo;s own time zone. The zone cannot
            change while the room has upcoming bookings.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={submit} className="flex flex-wrap items-end gap-4">
            <div className="flex min-w-48 flex-1 flex-col gap-2">
              <Label htmlFor="room-name">Name</Label>
              <Input
                id="room-name"
                value={form.name}
                onChange={(e) => setForm({ ...form, name: e.target.value })}
                required
                maxLength={100}
              />
            </div>
            <div className="flex w-28 flex-col gap-2">
              <Label htmlFor="room-capacity">Capacity</Label>
              <Input
                id="room-capacity"
                type="number"
                min={1}
                max={1000}
                value={form.capacity}
                onChange={(e) => setForm({ ...form, capacity: Number(e.target.value) })}
                required
              />
            </div>
            <div className="flex min-w-56 flex-col gap-2">
              <Label htmlFor="room-zone">Time zone (IANA)</Label>
              <Input
                id="room-zone"
                list="common-zones"
                value={form.timeZoneId}
                onChange={(e) => setForm({ ...form, timeZoneId: e.target.value })}
                required
              />
              <datalist id="common-zones">
                {COMMON_ZONES.map((zone) => <option key={zone} value={zone} />)}
              </datalist>
            </div>
            <div className="flex gap-2">
              <Button type="submit" disabled={busy}>
                {editingId === null ? "Add room" : "Save"}
              </Button>
              {editingId !== null && (
                <Button type="button" variant="ghost" onClick={reset}>Cancel</Button>
              )}
            </div>
          </form>
        </CardContent>
      </Card>

      <Card>
        <CardContent className="pt-6">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Capacity</TableHead>
                <TableHead>Time zone</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Actions</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rooms.map((room) => (
                <TableRow key={room.id} className={room.isActive ? "" : "text-muted-foreground"}>
                  <TableCell className="font-medium">{room.name}</TableCell>
                  <TableCell>{room.capacity}</TableCell>
                  <TableCell title={room.timeZoneId}>{zoneCity(room.timeZoneId)}</TableCell>
                  <TableCell>
                    <Badge variant={room.isActive ? "default" : "secondary"}>
                      {room.isActive ? "Active" : "Inactive"}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right">
                    <Button variant="ghost" size="sm" onClick={() => startEdit(room)}>Edit</Button>
                    <Button variant="ghost" size="sm" onClick={() => toggleActive(room)}>
                      {room.isActive ? "Deactivate" : "Activate"}
                    </Button>
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardContent>
      </Card>
    </div>
  )
}

// ------------------------------------------------------------- bookings

function BookingsTab() {
  const [rooms, setRooms] = useState<AdminRoom[]>([])
  const [roomId, setRoomId] = useState<number | undefined>(undefined)
  const [scope, setScope] = useState<Scope>("upcoming")
  const [bookings, setBookings] = useState<AdminBooking[]>([])

  useEffect(() => { api.admin.rooms().then(setRooms).catch(fail) }, [])

  const load = useCallback(
    () => api.admin.bookings(scope, roomId).then(setBookings).catch(fail),
    [scope, roomId])
  useEffect(() => { load() }, [load])

  async function cancel(booking: AdminBooking) {
    try {
      await api.admin.cancelBooking(booking.id)
      toast.success(`Cancelled ${booking.userLogin}'s booking in ${booking.roomName}`)
      await load()
    } catch (error) {
      fail(error)
    }
  }

  const roomItems = [
    { value: "all", label: "All rooms" },
    ...rooms.map((r) => ({ value: r.id.toString(), label: `${r.name} · ${zoneCity(r.timeZoneId)}` })),
  ]

  return (
    <Card>
      <CardHeader>
        <CardTitle>Bookings</CardTitle>
        <CardDescription>Every user&rsquo;s bookings. Times are shown in each room&rsquo;s zone.</CardDescription>
      </CardHeader>
      <CardContent className="flex flex-col gap-4">
        <div className="flex flex-wrap items-end gap-4">
          <div className="flex min-w-56 flex-col gap-2">
            <Label>Room</Label>
            <Select
              items={roomItems}
              value={roomId?.toString() ?? "all"}
              onValueChange={(value) => setRoomId(value === "all" ? undefined : Number(value))}
            >
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                {roomItems.map((item) => (
                  <SelectItem key={item.value} value={item.value}>{item.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
          <div className="flex gap-1">
            {(["upcoming", "past", "all"] as Scope[]).map((s) => (
              <Button
                key={s}
                size="sm"
                variant={scope === s ? "default" : "outline"}
                onClick={() => setScope(s)}
              >
                {s}
              </Button>
            ))}
          </div>
        </div>

        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>When</TableHead>
              <TableHead>Room</TableHead>
              <TableHead>User</TableHead>
              <TableHead>Booked</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {bookings.length === 0 && (
              <TableRow>
                <TableCell colSpan={5} className="text-muted-foreground">Nothing here.</TableCell>
              </TableRow>
            )}
            {bookings.map((b) => (
              <TableRow key={b.id}>
                <TableCell className="font-medium">
                  {inZone.full(b.slotStart, b.timeZoneId)}
                  <span className="ml-1 text-xs text-muted-foreground">{zoneCity(b.timeZoneId)}</span>
                </TableCell>
                <TableCell>{b.roomName}</TableCell>
                <TableCell>{b.userLogin}</TableCell>
                <TableCell className="text-muted-foreground">{inZone.day(b.createdAt, b.timeZoneId)}</TableCell>
                <TableCell className="text-right">
                  {scope !== "past" && (
                    <Button variant="ghost" size="sm" onClick={() => cancel(b)}>Cancel</Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}

// ---------------------------------------------------------------- users

function UsersTab({ me }: { me: Me }) {
  const [users, setUsers] = useState<AdminUser[]>([])

  const load = useCallback(() => api.admin.users().then(setUsers).catch(fail), [])
  useEffect(() => { load() }, [load])

  async function setRole(user: AdminUser, role: AdminUser["role"]) {
    try {
      await api.admin.setRole(user.id, role)
      toast.success(`${user.login} is now ${role === "Admin" ? "an admin" : "a regular user"}`)
      await load()
    } catch (error) {
      fail(error)
    }
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>Users</CardTitle>
        <CardDescription>
          A role change takes effect when that user next signs in. You cannot remove your own admin role.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>ID</TableHead>
              <TableHead>Login</TableHead>
              <TableHead>Role</TableHead>
              <TableHead className="text-right">Actions</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {users.map((u) => (
              <TableRow key={u.id}>
                <TableCell className="text-muted-foreground">{u.id}</TableCell>
                <TableCell className="font-medium">
                  {u.login}{u.id === me.id && <span className="ml-1 text-xs text-muted-foreground">(you)</span>}
                </TableCell>
                <TableCell>
                  <Badge variant={u.role === "Admin" ? "default" : "secondary"}>{u.role}</Badge>
                </TableCell>
                <TableCell className="text-right">
                  {u.role === "Admin" ? (
                    <Button variant="ghost" size="sm" disabled={u.id === me.id} onClick={() => setRole(u, "User")}>
                      Remove admin
                    </Button>
                  ) : (
                    <Button variant="ghost" size="sm" onClick={() => setRole(u, "Admin")}>
                      Make admin
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  )
}
