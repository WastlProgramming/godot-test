# JumpRun – Aufgabenliste pro Rolle

Grundlage: `docs/GDD.md` (Was) und `docs/CONTRACT.md` (Wie, verbindlich). Status: `[ ]` offen, `[x]` erledigt.

## Phase 1 – Projektmanager (PM)

- [x] PM-1 `docs/GDD.md` schreiben. *Akzeptanz:* Core Loop, Steuerung, Muster-Katalog, Spawn/Despawn, Items, Drops, Score/Highscore, Respawn, Floating Origin beschrieben.
- [x] PM-2 `docs/CONTRACT.md` schreiben. *Akzeptanz:* alle Klassen, Signale, Gruppen, Layer, Konstanten, Designer-Pfade, Zuständigkeiten eindeutig.
- [x] PM-3 `docs/TASKS.md` schreiben.
- [ ] PM-4 Nach Integration: Abgleich Ergebnis ↔ Vertrag, offene Punkte an Entwickler/Designer zurückgeben.

## Phase 2 – Entwickler (DEV) · besitzt `scripts/**`, `project.godot`, `*.csproj`, `*.sln`, `NuGet.config`, `scenes/*.tscn`, `scenes/objects/**`, `tests/**`

- [ ] DEV-1 Projektgerüst: `project.godot` (Name, Features 4.6/C#, Main-Scene, Autoload `GameManager`, InputMap aller Aktionen aus GDD 3, Layer-Namen aus CONTRACT 4), `JumpRun.csproj`, `JumpRun.sln`, `NuGet.config`.
  *Akzeptanz:* `dotnet build` fehlerfrei; `godot --headless --path . --build-solutions --quit` ohne Fehler.
- [ ] DEV-2 `GameConstants`, `GameManager`, `SaveSystem`.
  *Akzeptanz:* Highscore/Settings werden geladen/gespeichert; kaputtes, leeres, zu großes (> 4 KB) oder manipuliertes JSON (negative/riesige Werte, falsche Typen, falsche Version) führt zu Defaults ohne Exception; Schreiben atomar.
- [ ] DEV-3 `PlayerController` + `ThirdPersonCamera` + `Player.tscn`.
  *Akzeptanz:* Werte aus CONTRACT 3; Coyote-Time, Jump-Buffer, variable Sprunghöhe; Kamera kollidiert nicht durch Wände; Pitch begrenzt; Maus-Capture; Fallcheck → Respawn am aktiven Checkpoint.
- [ ] DEV-4 `WorldGenerator`, `ChunkBuilder`, `Chunk`, `PatternType`, `MovingPlatform`, `Checkpoint`, `FrustumUtil`, `MaterialLibrary`.
  *Akzeptanz:* alle 9 Muster implementiert; Parameter skalieren mit `d`; Regeln GDD 6 (keine Item-Chunks in 0–2, nie zwei in Folge, max. 2× gleiches Muster); Spawn/Despawn exakt nach GDD 5.2 (inkl. Frustum-Ebenen-Test, Schutz von Spieler- und Checkpoint-Chunk, hartes Limit); max. 1 Chunk-Build pro Frame; deterministisch pro Seed; Floating Origin bei 1000 m inkl. Kamera, Placeables, Respawn-Position, aktiver Enterhaken.
- [ ] DEV-5 Items: `ItemType`, `Inventory`, `Crate`, `Pickup`, `BridgeItem`/`Bridge`, `TrampolineItem`/`Trampoline`, `GrappleHook`, `GrappleAnchor` + Szenen.
  *Akzeptanz:* Drop-Tabelle GDD 9; `ForcedDrop` und `RestockSupply()` sichern Lösbarkeit; max. 5 Ladungen; max. 3 Placeables (ältestes wird entfernt); Lebensdauer mit Blinken; Enterhaken mit Reichweite, Kegel, Sichtlinie, Highlight, Lösen per Sprung; ungültige Einsätze verbrauchen nichts und senden `ItemUseFailed`.
- [ ] DEV-6 UI-Skripte `HUD.cs`, `MainMenu.cs`, `PauseMenu.cs` gegen Knotennamen aus CONTRACT 6.5; `Main.tscn` gemäß CONTRACT 8.
  *Akzeptanz:* HUD zeigt Distanz, Score, Rekord, Slots, Auswahl, Hinweise, Respawn-Flash; Pause pausiert Baum und gibt Maus frei; Seed-Feld validiert (nur Ziffern, ≤ 9).
- [ ] DEV-7 Tests: `tests/SmokeTest.tscn/.cs`, `tests/LogicTests.cs` (CONTRACT 9).
  *Akzeptanz:* Headless-Lauf gibt `SMOKE_TEST_PASS` aus und Exit-Code 0; Logiktests decken SaveSystem, ChoosePattern-Regeln, Lösbarkeit, Drop-Verteilung ab.
- [ ] DEV-8 Integration mit Designer-Ressourcen: `--import` headless, alle Vertragspfade laden ohne Fallback-Warnung, Build + Smoke-Test grün.

## Phase 2 – Designer (DESIGN) · besitzt `art/**`, `scenes/ui/**`, `icon.svg`

- [ ] DES-1 Farbpalette und Stil festlegen (kurz als Kommentarblock in `art/README.md`, keine Pflicht). Jedes Muster eindeutig unterscheidbar; Item-Pflicht-Abschnitte warnend.
- [ ] DES-2 Alle Materialien aus CONTRACT 7.1 als gültige `.tres` (format=3).
  *Akzeptanz:* jede Datei existiert unter exaktem Pfad, lädt headless fehlerfrei; Triplanar oder Weltkoordinaten-Shader, sieht auf skalierten Boxen gut aus; Anker/Bridge/Pickups mit Emission gut sichtbar.
- [ ] DES-3 Optionale Shader `art/shaders/*.gdshader` (z. B. Plattform mit Kantenhervorhebung, Pickup-Glow).
  *Akzeptanz:* kompilieren headless ohne Fehler.
- [ ] DES-4 `art/env/world_environment.tres` und `art/env/Lighting.tscn` (CONTRACT 7.3).
  *Akzeptanz:* Himmel, Nebel ~200 m, Schatten; Szene lädt ohne Fehler.
- [ ] DES-5 `art/ui/theme.tres` mit Typvariationen `ItemSlot`, `ItemSlotSelected`, `HudLabel`, `TitleLabel`; SVG-Icons in `art/ui/icons/`.
- [ ] DES-6 `scenes/ui/HUD.tscn`, `MainMenu.tscn`, `PauseMenu.tscn` mit **exakt** den Unique-Name-Knoten aus CONTRACT 6.5, Skript-ext_resource auf `res://scripts/UI/*.cs`, Theme an der Wurzel, Anker/Layouts für verschiedene Auflösungen (Full-Rect-Wurzel), PauseMenu `process_mode = 3`, `visible = false`.
  *Akzeptanz:* Szenen öffnen headless ohne Parse-Fehler; jeder geforderte Knoten vorhanden (`unique_name_in_owner = true`).
- [ ] DES-7 `art/fx/crate_break.tscn` (CPUParticles3D, one_shot), optional `item_pickup.tscn`.

## Phase 3 – Integration (DEV)

- [ ] INT-1 `dotnet build` fehlerfrei, keine Warnungen aus eigenem Code mit Schweregrad „Fehler“.
- [ ] INT-2 `godot --headless --path . --import` und `--build-solutions --quit` fehlerfrei.
- [ ] INT-3 Smoke-Test grün; Log frei von `ERROR`/`SCRIPT ERROR`.

## Phase 4 – Spieletester (TEST) · schreibt `docs/TEST_REPORT.md`

- [ ] TEST-1 Bug-Hunt Gameplay: Fall ins Nichts, Despawn unter dem Spieler bzw. am aktiven Checkpoint, Respawn in Item-Chunk ohne Item (Restock), Item-Spam (Placeable-Limit, Ladungen), Enterhaken auf despawnten Anker, Kiste schlagen während Pause, Moving-Platform-Mitnahme, Floating-Origin-Sprung (Score, Placeables, Seil).
- [ ] TEST-2 Ressourcen/Leaks: Knotenanzahl über langen Lauf stabil (`Performance.ObjectNodeCount`), keine verwaisten Pickups/Placeables, Signale getrennt.
- [ ] TEST-3 Sicherheit: Savefile-Parsing mit manipulierten Dateien (riesig, binär, tiefe Verschachtelung, NaN, negative Zahlen), Pfad-Handling nur `user://`, Seed-Feld-Eingaben (Buchstaben, Overflow, Unicode), keine Reflection/OS.Execute/Netzwerk.
- [ ] TEST-4 Lösbarkeit: über viele Seeds/Chunks prüfen, dass jeder Item-Chunk lösbar ist und normale Lücken ≤ `MaxSafeGap`.
  *Akzeptanz Phase 4:* Report mit Schweregrad (Kritisch/Hoch/Mittel/Niedrig), Reproduktionsschritten und Dateiverweis je Finding.

## Phase 4 – Reviewer (REVIEW, kontextunabhängig) · schreibt `docs/REVIEW.md`

- [ ] REV-1 Erhält nur Projektpfad + Original-Anforderung des Nutzers (nicht GDD/Vertrag/Verlauf). Prüft: Ist es ein 3D-Jump-and-Run in Godot/C#? Wird die Welt vorne nachgeneriert und hinten außer Sicht entfernt? Verschiedene Sprünge? Items Brücke/Trampolin/Enterhaken aus zerstörbaren Kisten? Third-Person?
- [ ] REV-2 Code-Qualität, Wartbarkeit, Godot-Best-Practices, offensichtliche Fehler.
  *Akzeptanz:* Findings mit Schweregrad und Datei/Zeile; klares Fazit „Anforderung erfüllt / teilweise / nicht“.

## Phase 5 – Fix-Runde (DEV, ggf. DESIGN)

- [ ] FIX-1 Alle Findings „Kritisch“ und „Hoch“ aus TEST_REPORT und REVIEW beheben.
- [ ] FIX-2 Erneut Build + Import + Smoke-Test grün; Findings im jeweiligen Report als behoben markieren.

## Definition of Done

1. `dotnet build` und Godot-Headless-Build fehlerfrei.
2. Smoke-Test: `SMOKE_TEST_PASS`, Chunk-Anzahl stabil (Spawn vorne / Despawn hinten), keine Errors.
3. Alle drei Items im Spiel erhältlich und einsetzbar; jeder Item-Chunk lösbar.
4. Highscore wird robust gespeichert/geladen.
5. Keine offenen kritischen/hohen Findings.
