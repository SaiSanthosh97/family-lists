# One-time setup for building the Android app on GitHub.
#
#   powershell -ExecutionPolicy Bypass -File scripts\setup-github.ps1
#
# 1. Saves your Supabase Project URL and public (anon / publishable) key as
#    GitHub Actions variables. These two are public by design: they ship inside the app.
# 2. Creates an app signing key (first run only) in android-signing\ (git-ignored),
#    and saves it as GitHub Actions secrets so every APK has the same signature
#    and installs as an update over the previous one.
# 3. Starts an APK build.
#
# Keep android-signing\ backed up (e.g. in your password manager). If it is lost,
# create a new one by deleting the folder and running this again; then uninstall
# the app once on each phone before installing the new APK. Your lists are safe:
# they live in Supabase, not on the phone.

$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

$repo = gh repo view --json nameWithOwner -q .nameWithOwner
if (-not $repo) { throw "Run 'gh auth login' first, from inside the project folder." }
Write-Host "Setting up $repo" -ForegroundColor Green

# --- 1. Supabase settings -------------------------------------------------
Write-Host ""
Write-Host "From Supabase: Project Settings -> API (or API Keys)."
$url = (Read-Host "Project URL (https://xxxx.supabase.co)").Trim().TrimEnd("/")
if ($url -notmatch '^https://[a-z0-9-]+\.supabase\.co$') { throw "That doesn't look like a Supabase project URL." }
$key = (Read-Host "anon public / Publishable key").Trim()
if ($key -match '^sb_secret_' -or $key -match 'service_role') { throw "That's the SECRET key. Use the anon / publishable key instead." }
if ($key.Length -lt 20) { throw "That key looks too short." }

gh variable set VITE_SUPABASE_URL --repo $repo --body $url
gh variable set VITE_SUPABASE_ANON_KEY --repo $repo --body $key

# --- 2. Signing key -------------------------------------------------------
$dir = "android-signing"
$keystore = Join-Path $dir "family-lists.keystore"
$pwFile = Join-Path $dir "password.txt"

if (-not (Test-Path $keystore)) {
  $keytool = $null
  if ($env:JAVA_HOME) { $keytool = Join-Path $env:JAVA_HOME "bin\keytool.exe" }
  if (-not $keytool -or -not (Test-Path $keytool)) { $keytool = (Get-Command keytool -ErrorAction SilentlyContinue).Source }
  if (-not $keytool) { throw "keytool not found. Install a JDK (17 or newer) and try again." }

  New-Item -ItemType Directory -Force $dir | Out-Null
  $bytes = New-Object byte[] 24
  [Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
  $pw = [Convert]::ToBase64String($bytes).Replace("+", "A").Replace("/", "B").Replace("=", "")
  & $keytool -genkeypair -keystore $keystore -alias family-lists -keyalg RSA -keysize 2048 -validity 10000 `
    -storepass $pw -keypass $pw -dname "CN=Family Lists" 2>$null | Out-Null
  if (-not (Test-Path $keystore)) { throw "Creating the signing key failed." }
  [IO.File]::WriteAllText((Resolve-Path $dir).Path + "\password.txt", $pw)
  Write-Host "Created a new signing key in $dir\ - back this folder up." -ForegroundColor Yellow
}

$pw = [IO.File]::ReadAllText((Resolve-Path $pwFile).Path)
$b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes((Resolve-Path $keystore).Path))
gh secret set ANDROID_KEYSTORE_BASE64 --repo $repo --body $b64
gh secret set ANDROID_KEYSTORE_PASSWORD --repo $repo --body $pw
Remove-Variable pw, b64

# --- 3. Build -------------------------------------------------------------
gh workflow run android.yml --repo $repo --ref main
Write-Host ""
Write-Host "Done. The APK build takes about 5 minutes." -ForegroundColor Green
Write-Host "Then on your phone open https://github.com/$repo/releases and download the .apk."
