# Family Lists

A shared list app for a household. You install it on your phone's home screen.

- **The monthly grocery list copies itself forward.** Each month starts as a copy of last month's list. You review it together: untick what you don't need, add what's new, then press **Finalize**.
- **Shopping mode.** Tap items as they go in the cart. Both phones see the ticks live.
- **Bill upload.** Take a photo of the receipt (or pick a PDF) and Claude reads every line and its price. Products you bought that weren't on the list are added to it, so next month's list includes them.
- **Spending tracking is optional.** You get a per-month total, a per-product breakdown, month-over-month bars, and the price history of any product. No bill? Type the total in, or type prices on individual items.
- **Groups.** Invite people with a link or a 6-letter code, and an existing member accepts them. You can be in several groups, and groups can never see each other's lists (enforced by the database, not just the app).
- **To-do lists.** Plain checklists, shared inside the same group.

## How it's built

| Part | What |
|---|---|
| App | React + TypeScript + Vite + Tailwind, as an installable PWA (`src/`) |
| Database, logins, photo storage, live sync | [Supabase](https://supabase.com) free tier (`supabase/migrations/`) |
| Bill reading | Supabase Edge Function `parse-bill` → Claude vision API (`supabase/functions/parse-bill/`) |

Privacy rules live in the database as row-level security. Every table, and the bill-photo bucket, is readable only by members of the group it belongs to. `npm run test:db` proves this against the real schema.

---

## One-time setup (about 20 minutes)

You need [Node.js](https://nodejs.org) 20 or newer. Then, in this folder:

```bash
npm install
```

### 1. Create the Supabase project

1. Sign up at <https://supabase.com> and create a **New project** (the free plan is fine). Pick a region near you and save the database password somewhere safe.
2. Open **Project Settings → API**. Copy the **Project URL** and the **anon public** key.
3. Create a file called `.env.local` in this folder:

   ```
   VITE_SUPABASE_URL=https://xxxxxxxx.supabase.co
   VITE_SUPABASE_ANON_KEY=eyJhbGciOi...
   ```

### 2. Create the tables and deploy the bill reader

```bash
npx supabase login
```

```bash
npx supabase link --project-ref xxxxxxxx
```

(`xxxxxxxx` is the part before `.supabase.co` in your project URL.)

```bash
npm run db:push
```

```bash
npx supabase secrets set ANTHROPIC_API_KEY=sk-ant-...
```

```bash
npm run fn:deploy
```

You can get an Anthropic API key at <https://console.anthropic.com>. Bills are read with `claude-opus-5-5` and usually cost a few cents each. To use a different model, run `npx supabase secrets set BILL_MODEL=claude-sonnet-5-5`, for example. If a request is declined, the function automatically retries on Anthropic's recommended fallback model.

### 3. Turn on sign-in

**Email magic link:** this works out of the box. Supabase's built-in email sender only allows a few emails per hour, which is fine for a family. For more, add your own SMTP under **Authentication → Emails → SMTP Settings**.

**Google sign-in** (free):

1. Go to <https://console.cloud.google.com>, create a project, then open **APIs & Services → OAuth consent screen**. Choose **External**, fill in the app name and your email, and add yourself and your family as **test users** (or publish the app).
2. Open **APIs & Services → Credentials → Create credentials → OAuth client ID** and choose **Web application**.
   - Under **Authorized redirect URIs**, add `https://xxxxxxxx.supabase.co/auth/v1/callback`.
3. Copy the Client ID and Client secret into Supabase under **Authentication → Sign In / Providers → Google**, then enable it.

**Redirect URLs:** in Supabase, open **Authentication → URL Configuration**:

- Set **Site URL** to where the app lives (for example `https://family-lists.vercel.app`).
- Under **Redirect URLs**, add `http://localhost:5173/**` and `https://your-app-domain/**`.

### 4. Run it

```bash
npm run dev
```

Open <http://localhost:5173>. To try it on your phone while it runs on your computer, use `npm run dev -- --host` and open the "Network" address it prints. That address also has to be added to the Redirect URLs above.

### 5. Put it online and install it on your phones

The app is a static site, so any free static host works. With [Vercel](https://vercel.com):

1. Push this folder to a GitHub repository.
2. In Vercel, choose **Import Project** and add the two `VITE_SUPABASE_*` variables under **Environment Variables**.
   - Build command: `npm run build`. Output directory: `dist`.
3. Deep links such as `/join/ABC234` already work because `vercel.json` is included. On another host, add a rule that rewrites every path to `/index.html`.
4. Add the deployed URL to Supabase's redirect URLs (step 3).

### Or: the Android app (APK) built on GitHub

Every push to `main` builds an APK with GitHub Actions (`.github/workflows/android.yml`) and publishes it on the repo's **Releases** page.

1. Finish steps 1–2 above (the Supabase project and tables).
2. In Supabase, open **Authentication → Emails → Templates** and paste in the sign-in code templates, because the app signs in with a 6-digit code from the email:
   - **Magic Link:** subject `Your Family Lists sign-in code`, body from `supabase/templates/magic_link.html`.
   - **Confirm signup:** same subject, body from `supabase/templates/confirmation.html`.
3. On your computer, in this folder, run this once. It asks for the Project URL and public key, creates the app's signing key and starts a build:

   ```
   powershell -ExecutionPolicy Bypass -File scripts\setup-github.ps1
   ```

4. After about 5 minutes, on your phone, open the repo's **Releases** page and download the newest `.apk`. Opening it installs or updates the app. The first time, Android asks you to allow installs from your browser.

In the app, sign in with the emailed code. Google sign-in only appears on the website, because Google blocks its sign-in page inside apps. To invite someone, tap **Share code** in group settings; they type the code under **Join a group**.

Back up the `android-signing` folder that the script creates, for example in your password manager. Updates must be signed with the same key. If it's lost, run the script again; then each phone needs to uninstall the app once before installing the new one. Your lists are safe either way, because they're stored in Supabase.

To install the website version on a phone instead, open the site and then:

- **Android / Chrome:** tap ⋮ → **Install app**.
- **iPhone / Safari:** tap Share → **Add to Home Screen**.

---

## Using it

1. **Create a group** (for example "Home") and pick your currency.
2. **Invite your partner.** Open ⚙ → **Share link**. They sign in, tap **Ask to join**, and you tap **Accept**.
3. **Create a monthly list** called "Groceries" and add what you usually buy. Typing `3 onions` or `onions x3` sets the quantity.
4. **Finalize** the list once you've both reviewed it. Then tick items off in the shop.
5. **Upload the bill** when you're back.
   - Review what was read. Each line is either matched to a list item, added as new, or skipped (bags, deposits).
   - Tap **Add to [month]** to confirm.
   - Prices land on the items, and new products join the list.
6. **Next month,** open the list and tap **Start [Month] list**.
   - Everything carries over, including last month's extras.
   - Items you skipped last month come back marked "skipped last month".
   - **Remove for good** on an item stops it from carrying over.
7. **Spending** (the 📊 icon on a monthly list) shows totals by month, a per-product breakdown and each product's price history.

---

## Development

| Command | What it does |
|---|---|
| `npm run dev` | Start the dev server |
| `npm test` | Unit tests: month rollover, bill-line matching, money maths |
| `npm run test:db` | Runs the real migration in an in-process Postgres and checks the privacy rules between groups, joining, leaving and bill confirmation |
| `npm run typecheck` | TypeScript check |
| `npm run build` | Production build to `dist/` |
| `npm run build:android` | Build for the Android app and copy it into `android/` (open that folder in Android Studio to run it on a phone or emulator) |

**Pretending it's next month.** In dev mode, add `?today=2026-12-01` to any URL. The app then behaves as if it's that date for the rest of the browser tab.

**Testing without the hosted project.** If you have Docker Desktop, `npx supabase start` runs the whole backend locally. It prints a local URL and anon key for `.env.local`, plus the address of a local mail viewer where magic-link emails arrive. For bill reading there, put `ANTHROPIC_API_KEY=...` in `supabase/functions/.env`, then run `npx supabase functions serve`.

To try two people at once on one computer, open the app at `http://localhost:5173` for one and at `http://anyname.localhost:5173` for the other. Each address keeps its own login. `npx supabase stop` shuts the local backend down and keeps its data.

### Project layout

```
src/
  api/         data access + React Query hooks (groups, lists, bills, spending)
  components/  shared UI (buttons, sheets, item rows, editor)
  lib/         rollover, bill matching, money, period, image resizing, auth
  pages/       screens: Login, Home, JoinGroup, GroupHome, GroupSettings,
               ListPage (to-do), MonthView (monthly), BillReview, Spending
supabase/
  migrations/  tables, row-level security, RPCs, storage policies, realtime
  functions/parse-bill/  reads a bill photo/PDF with Claude
  tests/rls.test.ts      privacy tests against the real schema
```
