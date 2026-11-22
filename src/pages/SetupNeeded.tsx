export default function SetupNeeded() {
  return (
    <main className="mx-auto max-w-lg px-4 py-12">
      <h1 className="text-2xl font-bold">Almost there</h1>
      <p className="mt-3 text-stone-700">
        The app isn't connected to its database yet. Create a <code>.env.local</code> file next to{" "}
        <code>package.json</code> with your Supabase project's URL and anon key, then restart <code>npm run dev</code>:
      </p>
      <pre className="mt-4 overflow-x-auto rounded-xl bg-stone-900 p-4 text-sm text-stone-100">
        {`VITE_SUPABASE_URL=https://YOUR-PROJECT.supabase.co
VITE_SUPABASE_ANON_KEY=your-anon-public-key`}
      </pre>
      <p className="mt-4 text-stone-700">The README walks through the full setup step by step.</p>
    </main>
  );
}
