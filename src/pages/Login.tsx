import { useState, type FormEvent } from "react";
import { supabase } from "../lib/supabase";
import { returnUrl } from "../lib/auth";
import { Button, Card, ErrorBox, Field, Input } from "../components/ui";

export default function Login() {
  const [email, setEmail] = useState("");
  const [sent, setSent] = useState(false);
  const [busy, setBusy] = useState<"google" | "email" | null>(null);
  const [error, setError] = useState<unknown>(null);
  const joining = window.location.pathname.startsWith("/join/");

  async function google() {
    setBusy("google");
    setError(null);
    const { error } = await supabase.auth.signInWithOAuth({ provider: "google", options: { redirectTo: returnUrl() } });
    if (error) {
      setError(error);
      setBusy(null);
    }
  }

  async function magicLink(e: FormEvent) {
    e.preventDefault();
    setBusy("email");
    setError(null);
    const { error } = await supabase.auth.signInWithOtp({
      email: email.trim(),
      options: { emailRedirectTo: returnUrl() },
    });
    setBusy(null);
    if (error) setError(error);
    else setSent(true);
  }

  return (
    <main className="mx-auto flex min-h-screen max-w-sm flex-col justify-center px-4 py-10">
      <div className="mb-8 text-center">
        <img src="/icon.svg" alt="" className="mx-auto mb-4 h-16 w-16" />
        <h1 className="text-2xl font-bold">Family Lists</h1>
        <p className="mt-2 text-stone-600">
          {joining
            ? "Sign in to ask to join the group you were invited to."
            : "Your monthly groceries, to-dos and spending, shared with the people you live with."}
        </p>
      </div>

      <Card className="space-y-4">
        {sent ? (
          <div className="space-y-3 text-center">
            <p className="text-lg font-semibold">Check your email</p>
            <p className="text-stone-600">
              We sent a sign-in link to <strong>{email}</strong>. Open it on this device to continue.
            </p>
            <Button variant="ghost" onClick={() => setSent(false)}>
              Use a different email
            </Button>
          </div>
        ) : (
          <>
            <Button variant="secondary" className="w-full" onClick={google} busy={busy === "google"}>
              <svg width="18" height="18" viewBox="0 0 48 48" aria-hidden>
                <path fill="#FFC107" d="M43.6 20.5H42V20H24v8h11.3C33.7 32.7 29.2 36 24 36c-6.6 0-12-5.4-12-12s5.4-12 12-12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 12.9 4 4 12.9 4 24s8.9 20 20 20 20-8.9 20-20c0-1.3-.1-2.4-.4-3.5z" />
                <path fill="#FF3D00" d="m6.3 14.7 6.6 4.8C14.7 15.1 19 12 24 12c3.1 0 5.8 1.2 7.9 3.1l5.7-5.7C34 6.1 29.3 4 24 4 16.3 4 9.7 8.3 6.3 14.7z" />
                <path fill="#4CAF50" d="M24 44c5.2 0 9.9-2 13.4-5.2l-6.2-5.2C29.2 35.1 26.7 36 24 36c-5.2 0-9.6-3.3-11.3-8l-6.5 5C9.5 39.6 16.2 44 24 44z" />
                <path fill="#1976D2" d="M43.6 20.5H42V20H24v8h11.3c-.8 2.2-2.2 4.2-4.1 5.6l6.2 5.2C37 39.2 44 34 44 24c0-1.3-.1-2.4-.4-3.5z" />
              </svg>
              Continue with Google
            </Button>

            <div className="flex items-center gap-3 text-xs text-stone-400">
              <span className="h-px flex-1 bg-stone-200" /> or <span className="h-px flex-1 bg-stone-200" />
            </div>

            <form onSubmit={magicLink} className="space-y-3">
              <Field label="Email" hint="We'll email you a link that signs you in. No password needed.">
                <Input
                  type="email"
                  required
                  autoComplete="email"
                  value={email}
                  onChange={(e) => setEmail(e.target.value)}
                  placeholder="you@example.com"
                />
              </Field>
              <Button type="submit" className="w-full" busy={busy === "email"}>
                Email me a sign-in link
              </Button>
            </form>
          </>
        )}
        <ErrorBox error={error} />
      </Card>
    </main>
  );
}
