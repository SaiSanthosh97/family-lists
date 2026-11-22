# Releasing an Update

Phones running Family Lists find new versions in **Settings → Check for updates**, and automatically once a day. They read the latest release of **github.com/SaiSanthosh97/family-lists**, download the `.apk`, check it against GitHub's SHA-256, and hand it to Android's installer.

## Steps for each new version

1. **Bump the version** in `src/Apps/FamilyLists.App/FamilyLists.App.csproj`:
   - `ApplicationDisplayVersion` is the version people see, e.g. `0.1.0` → `0.2.0`. It **must match the release tag** (`v0.2.0`), because the app compares the two.
   - `ApplicationVersion` is a whole number that must go **up by one** every release. Android refuses to "update" to a lower or equal number.
2. **Record the change** in `docs/CHANGES.md`, plus a technical doc if it's a feature.
3. **Test and build:**
   ```sh
   dotnet test tests/Core.Sync.Tests        # and the other test projects
   dotnet publish src/Apps/FamilyLists.App -f net10.0-android -c Release
   ```
   The release build is signed automatically with the key in `%USERPROFILE%\.familylists\` (see below).
4. **Commit and push**, then **publish the release** with the APK attached. The file name must end in `.apk`.
   ```sh
   copy src\Apps\FamilyLists.App\bin\Release\net10.0-android\publish\io.github.saisanthosh97.familylists-Signed.apk family-lists-0.2.0.apk
   gh release create v0.2.0 family-lists-0.2.0.apk --title "Family Lists 0.2.0" --notes "What's new…"
   ```

Phones offer the update within a day, or straight away through "Check for updates".

## The signing key: back it up

`%USERPROFILE%\.familylists\release.keystore` and `signing.props` (which holds its password) sign every release.
- **Android only installs an update signed with the same key** as the installed app. If this key is lost, no installed phone can be updated again. Everyone would have to uninstall, reinstall, and rejoin their group, and the group's other phones send everything back.
- Keep a copy of that folder somewhere safe and private, such as an encrypted USB stick or a password manager. **Never commit it.** `.gitignore` excludes `*.keystore`, `*.jks` and `signing.props`.

`signing.props` looks like this:

```xml
<Project>
  <PropertyGroup Condition="'$(Configuration)' == 'Release'">
    <AndroidKeyStore>true</AndroidKeyStore>
    <AndroidSigningKeyStore>C:\Users\you\.familylists\release.keystore</AndroidSigningKeyStore>
    <AndroidSigningKeyAlias>familylists</AndroidSigningKeyAlias>
    <AndroidSigningStorePass>…</AndroidSigningStorePass>
    <AndroidSigningKeyPass>…</AndroidSigningKeyPass>
  </PropertyGroup>
</Project>
```

## Notes
- The repository must stay **public**, because the app reads releases without logging in.
- There is no CI workflow yet. Pushing a file under `.github/workflows/` needs the GitHub CLI's `workflow` permission, which you grant once with `gh auth refresh -s workflow`.
