import { useState } from "react"
import { toast } from "sonner"
import { api, token } from "@/lib/api"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { Label } from "@/components/ui/label"
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs"

export function AuthCard({ onSignedIn }: { onSignedIn: () => void }) {
  const [mode, setMode] = useState<"login" | "register">("login")
  const [login, setLogin] = useState("")
  const [password, setPassword] = useState("")
  const [busy, setBusy] = useState(false)

  async function submit(event: React.FormEvent) {
    event.preventDefault()
    setBusy(true)

    try {
      const result = mode === "login"
        ? await api.login(login, password)
        : await api.register(login, password)

      token.set(result.token)
      toast.success(mode === "login" ? "Signed in" : "Account created")
      onSignedIn()
    } catch (error) {
      toast.error((error as Error).message)
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="flex min-h-svh items-center justify-center bg-muted/40 p-4">
      <Card className="w-full max-w-sm">
        <CardHeader>
          <CardTitle>Room Booking</CardTitle>
          <CardDescription>Book a meeting room by the hour</CardDescription>
        </CardHeader>

        <CardContent>
          <Tabs value={mode} onValueChange={(value) => setMode(value as typeof mode)}>
            <TabsList className="mb-4 grid w-full grid-cols-2">
              <TabsTrigger value="login">Sign in</TabsTrigger>
              <TabsTrigger value="register">Register</TabsTrigger>
            </TabsList>

            <TabsContent value={mode}>
              <form onSubmit={submit} className="flex flex-col gap-4">
                <div className="flex flex-col gap-2">
                  <Label htmlFor="login">Login</Label>
                  <Input
                    id="login"
                    value={login}
                    onChange={(e) => setLogin(e.target.value)}
                    placeholder="at least 3 characters"
                    autoComplete="username"
                    required
                  />
                </div>

                <div className="flex flex-col gap-2">
                  <Label htmlFor="password">Password</Label>
                  <Input
                    id="password"
                    type="password"
                    value={password}
                    onChange={(e) => setPassword(e.target.value)}
                    placeholder="at least 6 characters"
                    autoComplete={mode === "login" ? "current-password" : "new-password"}
                    required
                  />
                </div>

                <Button type="submit" className="w-full" disabled={busy}>
                  {busy ? "Please wait..." : mode === "login" ? "Sign in" : "Create account"}
                </Button>
              </form>
            </TabsContent>
          </Tabs>
        </CardContent>
      </Card>
    </main>
  )
}
