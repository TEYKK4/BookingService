import { useEffect, useState } from "react"
import { Toaster } from "@/components/ui/sonner"
import { api, token, type Me } from "@/lib/api"
import { AuthCard } from "@/components/AuthCard"
import { BookingBoard } from "@/components/BookingBoard"
import { AdminPanel } from "@/components/AdminPanel"

export default function App() {
  const [signedIn, setSignedIn] = useState(() => token.get() !== null)
  const [me, setMe] = useState<Me | null>(null)
  const [view, setView] = useState<"book" | "admin">("book")

  // The server says who the token belongs to; the client never decodes the JWT.
  // A stale or forged token fails here and drops the user back to sign-in.
  useEffect(() => {
    if (!signedIn) {
      setMe(null)
      return
    }
    api.me().then(setMe).catch(signOut)
  }, [signedIn])

  function signOut() {
    token.clear()
    setSignedIn(false)
    setView("book")
  }

  const isAdmin = me?.role === "Admin"

  return (
    <>
      {!signedIn ? (
        <AuthCard onSignedIn={() => setSignedIn(true)} />
      ) : view === "admin" && isAdmin && me ? (
        <AdminPanel me={me} onBack={() => setView("book")} onSignOut={signOut} />
      ) : (
        <BookingBoard onSignOut={signOut} isAdmin={isAdmin} onOpenAdmin={() => setView("admin")} />
      )}
      <Toaster richColors position="top-center" />
    </>
  )
}
