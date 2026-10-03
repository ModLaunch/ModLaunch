# Adding a language to ModLaunch

ModLaunch picks its language from Windows (Settings → Appearance → Language lets you change it).
Built-in languages: Русский, English, Español, Deutsch, Français, Português (Brasil), 简体中文, Українська.
Anything that is not translated is shown in English.

## Add your own language (no programming)

1. Open Settings → Appearance → Language → **Open translations folder**.
2. Put a file there named after the language code, for example `it.json`, `ja.json`, `pl.json`, `ar.json`.
3. Inside, write `"key": "text"` pairs, copying the keys from `desktop/ModLaunch/Assets/strings*.json` (the `"en"` part):

```json
{
  "nav.menu": "Home in your language",
  "mod.install": "Install in your language"
}
```

4. Restart ModLaunch. The language appears in the list with the percentage that is translated.

Rules:
- Keep `{placeholders}` exactly as they are (`{n}`, `{game}`, `{mod}` …). A built-in self-test checks this.
- Plural keys come in three forms: `.one`, `.few`, `.many`. For languages without plurals (Chinese, Japanese…) write the same text three times. Plural rules: Russian/Ukrainian/Belarusian, Polish, Czech/Slovak and English-like languages are built in.
- `num.k` / `num.m` set how thousands and millions are shortened (`{n}K`, `{n} тыс.`).
- Right-to-left languages (`ar`, `he`, `fa`, `ur`) flip the layout automatically.

## Ship a language inside the app

Put `strings-<code>-<part>.json` into `desktop/ModLaunch/Assets/` using the shape `{ "xx": { "key": "text" } }`
and run `ModLaunch.exe --selftest` — it fails if a translation loses or invents a `{placeholder}`.
