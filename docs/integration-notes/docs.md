# Integration-Notes DOCS

- `website/` angelegt (VitePress 1.6.3 exakt, `package-lock.json`, `.gdignore`, `config.mts` mit `lang: 'de-DE'`,
  Nav + Sidebar, lokale Suche, `base` über `DOCS_BASE`). Build: `cd website && npm ci && npm run docs:build` → grün,
  keine toten Links.
- `.gitignore`: benötigt `website/node_modules/`, `website/.vitepress/dist/`, `website/.vitepress/cache/` – bereits
  vorhanden, keine Änderung nötig. `website/package-lock.json` einchecken.
- `npm install` meldet Audit-Hinweise (transitive Dev-Abhängigkeiten esbuild/vite der VitePress-1.6.3-Kette, betrifft
  nur den lokalen Dev-Server). Kein Handlungsbedarf für den statischen Build.
