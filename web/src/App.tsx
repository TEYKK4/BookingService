import { useState } from "react"
import { Toaster } from "@/components/ui/sonner"
import { token } from "@/lib/api"
import { AuthCard } from "@/components/AuthCard"
import { BookingBoard } from "@/components/BookingBoard"

export default function App() {
  const [signedIn, setSignedIn] = useState(() => token.get() !== null)

  function signOut() {
    token.clear()
    setSignedIn(false)
  }

  return (
    <>
      {signedIn
        ? <BookingBoard onSignOut={signOut} />
        : <AuthCard onSignedIn={() => setSignedIn(true)} />}
      <Toaster richColors position="top-center" />
    </>
  )
}
