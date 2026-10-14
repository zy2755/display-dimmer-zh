# Display Dimmer — Chinese (Simplified) Localization Patch (Unofficial)

A community-made localization toolkit that translates the UI of
[Display Dimmer](https://apps.microsoft.com/detail/9nbwhfcln6cm) — a Windows brightness
control utility for external monitors — into Simplified Chinese. **877 translated strings** — 253 from BAML UI resources and 624
string literals in code — plus 39 method-scoped rules for strings too short to replace
globally (`"."`, `" ("`).
Covers the main window, settings pages, tray menu, schedules, app rules, hotkeys,
the Pro dialog, and release notes.

> **This repository ships tools, not software.**
> It contains **no binaries of Display Dimmer**. You install the official app from the
> Microsoft Store yourself; this toolkit reads the files already on your machine,
> replaces the UI strings locally, and the output never leaves your computer.
> See [NOTICE.md](NOTICE.md).

## Quick start

Requirements: Windows 10/11, Display Dimmer installed from the Microsoft Store,
and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (only needed to
build the small tool in `tools/`).

```powershell
# 1. Copy the official files out of the Store install (run as Administrator)
.\fetch_original.ps1

# 2. Apply the patch in place
.\build.ps1 -InPlace
```

Then launch `app\Display Dimmer.exe`.

Why a "portable" copy? Store apps are signed MSIX packages — swapping a DLL inside one
breaks signature validation and the app refuses to start. So the whole program folder is
copied out and run outside the Store loader.

## Caveats

- Built against **Display Dimmer 2.2.12**. Newer versions will need new strings translated.
- **Pro license**: the portable copy runs outside the Store environment, so Store
  entitlement **may not validate**. Pro users should keep the Store version around to compare.
- Auto-update will overwrite the patched DLL; re-run the build afterwards.
- The portable `exe` has no embedded icon (MSIX keeps icons in the package manifest).
  `scripts\gen_ico.py` and `scripts\embed_icon.py` can add one back.

## How it works

WPF apps keep UI text in two unrelated places, and patching only one leaves a
half-translated mess.

1. **BAML resources** (`localize`) — compiled XAML, stored in `g.resources`.
   Instead of hand-parsing this fragile binary format, the tool drives WPF's own
   `BamlLocalizer` pipeline (`BamlResourceDeserializer` / `BamlResourceSerializer`),
   so the full structure — deferred content, connection IDs, type converters — is
   preserved and only strings are swapped.
2. **String literals in IL** (`usx`) — status text and dynamically composed sentences
   live in the `#US` metadata stream, rewritten with Mono.Cecil.

The string dictionary is applied in three passes: whole sentences first, then
concatenation fragments (e.g. `"Connected to: "` with leading/trailing spaces), then UI
classes missed by the first pass. Later passes win.

A few strings are too short and too widely reused to replace globally — `"."`, `" ("`,
`" at "`. `dict/us_scope.tsv` restricts those to methods whose name matches a given
substring, keeping diagnostics and CLI output intact.

## Contributing

New translations are the most welcome contribution. Dictionary files are plain
tab-separated `source<TAB>translation` lines in `dict/`.

## License

- Toolkit source and translations: **MIT** ([LICENSE](LICENSE))
- Display Dimmer itself: all rights belong to its author, **Vantari Works**

**Unofficial community project. Not affiliated with Vantari Works.**
If you find the app useful, please support the developer by purchasing Pro on the
Microsoft Store.
