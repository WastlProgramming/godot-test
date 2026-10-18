# Mitwirken

## Arbeitsweise

Das Projekt wird in **Rollen** entwickelt, die parallel im selben Verzeichnis arbeiten. Verbindlich sind die
Verträge in `docs/CONTRACT.md` und `docs/CONTRACT_V2.md` (Schnittstellen, Datei-Besitz, Namen) sowie die Aufgaben in
`docs/TASKS_V2.md`.

| Rolle | Bereich |
|---|---|
| GAMEPLAY | `PlayerController`, Items, Welt-Generierung, HUD, Gameplay-Tests |
| CHARACTERS | Charaktere, `Player.tscn`, Hauptmenü, Charakterauswahl |
| AUDIO | `tools/gen_audio.py`, `art/audio/`, `scripts/Audio/`, Bus-Layout |
| VISUALS | Shader, Materialien, Umgebung, FX, Backdrop |
| DOCS | `website/` (diese Dokumentation) |
| INTEGRATOR | `project.godot`, `Main.tscn`, Pause-Menü, README, Git |

Wer eine Änderung in einer fremden Datei braucht, trägt sie in `docs/integration-notes/<rolle>.md` ein – mit Datei,
exakter Änderung und Begründung.

## Regeln für Code

- C#, Namespaces nach Ordner, `public partial class`, Dateiname = Klassenname.
- Spielwerte nur in `scripts/Game/GameConstants.cs`.
- Keine externen Assets: Meshes prozedural, Audio synthetisch, Icons als handgeschriebenes SVG.
- Renderer-Regeln beachten (siehe [Grafik](../technik/grafik)): alles muss in Forward+ **und** Compatibility laufen.
- Keine Allokationen pro Frame in heißen Pfaden.
- Kein Code darf headless Fehler werfen. Neue Funktionen bekommen einen Test unter `tests/`.
- Neue Enum-Werte (`PatternType`, `PlatformShape`, `ItemType`) nur **am Ende** anhängen – die Reihenfolge ist
  verbindlich und beeinflusst den Determinismus.

## Build und Test

```bash
dotnet build
$G --headless --path . --import
for t in tests/*.tscn; do $G --headless --fixed-fps 60 --path . "res://$t" > /dev/null 2>&1; echo "$t -> exit $?"; done
```

Details unter [Tests ausführen](../technik/tests).

## Audio neu generieren

```bash
python3 tools/gen_audio.py
python3 tools/verify_audio.py --determinism
$G --headless --path . --import
```

Lautstärken einzelner Sounds passt du in `Sfx.TrimDb` an, nicht in den WAV-Dateien. Mehr unter [Audio](../technik/audio).

## Dokumentation bauen

Die Website liegt in `website/` (nicht in `docs/` – dort liegen die Projekt-Dokumente). Die leere Datei
`website/.gdignore` sorgt dafür, dass Godot `node_modules` nicht importiert.

```bash
cd website
npm ci                # bzw. npm install
npm run docs:dev      # lokale Vorschau mit Hot Reload
npm run docs:build    # statische Seite nach website/.vitepress/dist
npm run docs:preview  # gebaute Seite lokal ansehen
```

Für GitHub Pages unter einem Unterpfad setzt du die Basis per Umgebungsvariable:

```bash
DOCS_BASE=/<repo>/ npm run docs:build
```

## Git-Workflow

- Branch `main`; neue Arbeit auf einem Feature-Branch, z. B. `feature/neues-item`.
- Nicht eingecheckt werden: `.godot/`, `.tools/`, `bin/`, `obj/`, `website/node_modules/`,
  `website/.vitepress/dist/`, `website/.vitepress/cache/`.
- `*.uid`- und `*.import`-Dateien **werden** eingecheckt, ebenso die erzeugten `.wav`-Dateien und
  `website/package-lock.json`.
- Vor jedem Commit: `dotnet build` ohne Fehler, Tests grün, `npm run docs:build` grün.

Remote einrichten und pushen:

```bash
git remote add origin <url>
git push -u origin main
```
