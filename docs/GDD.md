# JumpRun – Game Design Document

Version 2.0 · Engine: Godot 4.6.3 (.NET) · Sprache: C# · Projektname/Assembly: `JumpRun`

Verbindliche technische Schnittstellen (Klassen, Signale, Pfade, Konstanten) stehen in `docs/CONTRACT.md` (v1) und `docs/CONTRACT_V2.md` (v2). Bei Widerspruch gilt `CONTRACT_V2`, danach `CONTRACT`.

**Neu in v2:** vier spielbare Charaktere mit animierten Armen und Beinen (Abschnitt 15), unbegrenztes Inventar, Helikopter als viertes Item, Plattformen in elf Formen inklusive drehender Scheiben, synthetisierte Musik und Soundeffekte (Abschnitt 16) sowie ein neuer Himmel mit Berg- und Insel-Hintergrund (Abschnitt 13).

## 1. Vision

Ein endloses 3D-Jump-and-Run aus der Third-Person-Perspektive. Der Spieler läuft einen prozedural erzeugten Parkour entlang, der vor ihm ständig neu entsteht und hinter ihm, sobald er außer Sicht ist, wieder verschwindet. Abwechslungsreiche Sprungpassagen fordern Timing und Präzision. Kisten am Wegesrand lassen sich zerschlagen und enthalten Items (Brücke, Trampolin, Enterhaken, Helikopter), ohne die bestimmte Abschnitte nicht zu schaffen sind. Ziel: so weit kommen wie möglich (Score = Distanz).

Stil: stilisiert/low-poly, kräftige Farben, klar lesbare Plattformkanten, prozedurale Meshes (BoxMesh, CylinderMesh, SphereMesh, PrismMesh, ArrayMesh aus `ShapeMeshFactory`), synthetisierter Sound, keine externen Assets.

## 2. Core Loop

1. Laufen und springen über die Plattformen des aktuellen Chunks.
2. Kisten zerschlagen, Items einsammeln (Pickup schwebt nach dem Zerschlagen kurz an der Stelle).
3. An Item-Pflicht-Abschnitten das passende Item auswählen und einsetzen (Brücke über die Riesenlücke, Trampolin vor der hohen Wand, Enterhaken am Ankerpunkt, Helikopter über beides).
4. Checkpoints am Anfang jedes Chunks passieren; bei einem Sturz wird der Spieler dort wieder abgesetzt.
5. Distanz steigt, Schwierigkeit steigt, Highscore wird gespeichert.

## 3. Steuerung / Input-Map

| Aktion (InputMap-Name) | Taste(n) | Funktion |
|---|---|---|
| `move_forward` | W | vorwärts (relativ zur Kamera) |
| `move_back` | S | rückwärts |
| `move_left` | A | links |
| `move_right` | D | rechts |
| `jump` | Leertaste | Sprung (mit Coyote-Time und Jump-Buffer); löst auch den Enterhaken; im Helikopter-Flug: steigen |
| `sprint` | Shift (links) | Sprinten |
| `attack` | linke Maustaste, F | Kiste vor dem Spieler schlagen |
| `item_1` | 1 | Brücke wählen und sofort einsetzen |
| `item_2` | 2 | Trampolin wählen und sofort einsetzen |
| `item_3` | 3 | Enterhaken wählen und sofort einsetzen (beim aktiven Haken: lösen) |
| `item_4` | 4 | Helikopter wählen und sofort starten (im Flug: Flug beenden, ohne Ladung zu verbrauchen) |
| `item_next` / `item_prev` | Mausrad runter / hoch | nächsten/vorigen Slot wählen |
| `use_item` | E, rechte Maustaste | gewähltes Item einsetzen |
| `pause` | Esc | Pause-Menü öffnen/schließen (Maus wird freigegeben) |

Maus: Kamera drehen (Yaw um den Spieler, Pitch begrenzt auf -60° bis +35°). Die Maus wird im Spiel eingefangen (`Input.MouseMode = Captured`) und im Pause- und Hauptmenü freigegeben.

## 4. Spielerbewegung (Feel)

- Laufen 7 m/s, Sprint 11 m/s, Beschleunigung am Boden hoch (snappy), in der Luft reduziert (Air Control 40 %).
- Schwerkraft 24 m/s², Sprunggeschwindigkeit 10,5 m/s → Sprunghöhe ca. 2,3 m, Flugzeit ca. 0,875 s.
- Maximale Sprungweite im Sprint ca. 9 m (horizontal, gleiche Höhe). Generator nutzt als sichere Obergrenze 6,0 m Lücke (siehe CONTRACT `MaxSafeGap`).
- Variable Sprunghöhe: Loslassen der Sprungtaste während des Aufstiegs halbiert die vertikale Geschwindigkeit.
- Coyote-Time 0,12 s, Jump-Buffer 0,12 s.
- Der Spieler dreht sich in Laufrichtung; die Kamera folgt mit SpringArm3D (Kollision mit der Welt, kein Durchclippen).

## 5. Welt: Chunks und Endlos-Generierung

### 5.1 Aufbau

- Die Welt besteht aus Chunks entlang der -Z-Achse (Godot-Vorwärtsrichtung). Nominelle Chunk-Länge: 40 m.
- Jeder Chunk hat einen Einstiegspunkt (`EntryPoint`) und einen Ausstiegspunkt (`ExitPoint`). Der nächste Chunk beginnt exakt am `ExitPoint` des vorherigen. Seitlicher Versatz (X) bleibt in ±12 m um die Mittellinie, Höhe (Y) wandert um maximal ±3 m pro Chunk und bleibt im Bereich -10 bis +40 m.
- Jeder Chunk beginnt mit einem Checkpoint-Pad (4 × 4 m, Flaggen-Mesh). Betreten aktiviert den Checkpoint.
- Generierung ist deterministisch aus Seed + Chunk-Index (`new Random(seed * 7919 + index)`), damit Fehler reproduzierbar sind.

### 5.2 Spawn-/Despawn-Regeln

- **Spawnen:** Es existieren immer mindestens `ChunksAhead = 6` Chunks vor dem Chunk, auf dem der Spieler steht. Pro Physik-Frame wird höchstens ein neuer Chunk gebaut (verhindert Ruckler).
- **Despawnen** eines Chunks hinter dem Spieler nur, wenn **alle** Bedingungen erfüllt sind:
  1. Chunk-Index < aktueller Spieler-Chunk-Index (er liegt hinter dem Spieler),
  2. der `ExitPoint` des Chunks liegt mehr als `DespawnDistance = 30 m` hinter dem Spieler (entlang -Z gemessen),
  3. das Chunk-AABB liegt vollständig außerhalb des Kamera-Frustums (Ebenen-Test, nicht nur Eckpunkte),
  4. es ist **nicht** der Chunk, auf dem der Spieler steht, und **nicht** der Chunk mit dem aktiven Checkpoint.
- **Hartes Limit:** Mehr als `MaxAliveChunks = 12` Chunks → der älteste Chunk wird auch ohne Frustum-Bedingung entfernt (Bedingungen 1 und 4 gelten weiterhin).
- Entfernen per `QueueFree()`. Alle Kinder (Kisten, Pickups, Anker, bewegliche Plattformen) hängen am Chunk und verschwinden mit. Platzierte Items (Brücke, Trampolin) hängen unter `WorldRoot/Placeables` und haben eine eigene Lebensdauer.

### 5.3 Floating Origin

Bei großen Distanzen verliert `float` Präzision (Zittern ab einigen km). Sobald `|Player.Z| > 1000 m`, verschiebt `WorldGenerator` alle Kinder von `WorldRoot`, den Spieler und die Kamera um denselben Offset zurück Richtung Ursprung (nur Z-Anteil, ganzzahlige Meter). Der Offset wird in `GameManager.OriginOffsetZ` aufsummiert, damit die Distanz korrekt weiterzählt. Signal `OriginShifted(Vector3 offset)` informiert alle Interessenten (z. B. aktive Enterhaken-Verbindung, Respawn-Position).

## 6. Sprungmuster-Katalog (`PatternType`)

Jeder Chunk besteht aus Checkpoint-Pad + einem Muster. Parameter skalieren mit dem Schwierigkeitsfaktor `d` (0..1, siehe Abschnitt 7).

| PatternType | Beschreibung | Parameter (d=0 → d=1) |
|---|---|---|
| `Start` | Nur Chunk 0: großer, flacher Startbereich (12 × 30 m) mit 3 Tutorial-Kisten (die dritte bei (0, 0.5, −26) enthält einen Helikopter) | – |
| `GapJump` | 4–6 Plattformen in Linie mit Lücken | Lücke 2,5 → 5,5 m; Plattformlänge 5 → 2,5 m |
| `Stairs` | Treppe auf- oder abwärts aus Blöcken | Stufenhöhe 1,0 → 1,8 m; Stufenabstand 1,5 → 3,0 m |
| `ZigZag` | Plattformen abwechselnd links/rechts versetzt | Seitversatz 3 → 6 m; Lücke 2 → 4 m |
| `MovingPlatforms` | 2–4 bewegliche Plattformen (seitlich oder vertikal, Ping-Pong) | Geschwindigkeit 1,5 → 3,5 m/s; Plattform 4 → 2,5 m |
| `NarrowBeams` | Schmale Balken, teils mit kleinen Lücken | Breite 1,0 → 0,5 m; Länge 8 → 5 m |
| `HeightJump` | Plattformen mit Höhenversatz nach oben | Höhenstufe 1,2 → 2,0 m (unter Sprunghöhe 2,3 m) |
| `BigGap` | **Item-Pflicht:** Lücke 12–16 m, darüber in der Mitte ein Enterhaken-Ankerpunkt (6–8 m über Plattformniveau) | lösbar mit **Brücke ODER Enterhaken** |
| `TallWall` | **Item-Pflicht:** Wand 5,5–7 m hoch, 3 m tief, über die gesamte Breite | lösbar mit **Trampolin** (zusätzlich Anker oberhalb, falls d > 0,5: alternativ Enterhaken) |
| `Pillars` (v2) | 5–8 Säulen (Zylinder/Sechseck/Achteck, 3–8 m hoch) mit Höhenversatz | Durchmesser 3,2 → 2,6 m |
| `SpinningDiscs` (v2) | 3–5 drehende Scheiben/Sechsecke/Dreiecke, erst ab Chunk 4 | 0,4 → 0,9 rad/s, Drehrichtung zufällig |
| `ShapeMix` (v2) | 4–7 gemischte Formen inkl. Rampe (≤ 25°) und Kreuz | – |

BigGap ist in v2 auch mit dem **Helikopter** lösbar, TallWall ebenfalls. Die garantierten Versorgungskisten bleiben wie in v1 (Brücke/Enterhaken bzw. Trampolin).

### 6.0 Plattform-Formen (v2)

`PlatformShape`: Box, Square, Diamond, Cylinder, Disc, Triangle, Pentagon, Hexagon, Octagon, Ramp, Cross. Jede Form hat eine exakt passende Kollision (Box-, Zylinder- oder konvexe Shapes, nie Trimesh). Runde und eckige Formen haben einen Innenradius von mindestens 1,2 m; Rampen steigen höchstens 25°. Drehende Plattformen (`RotatingPlatform`) drehen höchstens 0,9 rad/s und tragen den Spieler mit.

Formvariation bestehender Muster: GapJump (Box/Square/Disc/Hexagon/Octagon/Triangle), Stairs (Box/Square/Cylinder/Ramp, Rampen nur aufwärts), ZigZag (Box/Disc/Hexagon/Diamond/Square), HeightJump (Box/Cylinder/Hexagon/Octagon), MovingPlatforms (Box/Disc/Hexagon). Start, NarrowBeams, BigGap, TallWall, Checkpoint-Pad und Landeplattform bleiben Boxen.

Allgemeine Regeln:
- Alle normalen Lücken ≤ `MaxSafeGap` (6,0 m), Höhenstufen ≤ `MaxSafeStep` (2,0 m). Nur Item-Pflicht-Abschnitte überschreiten das.
- Zwei Item-Pflicht-Chunks folgen nie direkt aufeinander.
- Die ersten 3 Chunks (Index 0–2) enthalten nie Item-Pflicht-Abschnitte.
- Gleiches Muster höchstens 2× in Folge.

### 6.1 Lösbarkeitsgarantie (wichtig)

Ein Item-Pflicht-Chunk ist so aufgebaut: `Checkpoint-Pad → Versorgungsplattform (Supply) mit 2 Kisten → Hindernis → Landeplattform → ExitPoint`.

- Mindestens 2 Kisten auf der Versorgungsplattform haben einen **garantierten Drop** (`Crate.ForcedDrop`) des benötigten Items (BigGap: Brücke oder Enterhaken, abwechselnd; TallWall: Trampolin).
- Zusätzlich enthält der vorherige Chunk mindestens 1 Kiste mit dem benötigten Item als garantiertem Drop.
- **Respawn-Absicherung:** Stürzt der Spieler im Item-Pflicht-Chunk und besitzt keines der passenden Items, ruft `WorldGenerator` `Chunk.RestockSupply()` auf: zerstörte Versorgungskisten werden mit garantiertem Drop neu erzeugt. Damit ist jeder Run immer lösbar.
- Der Checkpoint liegt vor der Versorgungsplattform.

## 7. Schwierigkeitskurve

- `d = clamp(distance / 1500 m, 0, 1)`.
- Mit steigendem `d`: Lücken und Höhen größer (bis zur Safe-Grenze), Plattformen kleiner, bewegliche Plattformen schneller, Balken schmaler.
- Wahrscheinlichkeit eines Item-Pflicht-Chunks: 0,15 → 0,35.
- Gewichtung der Muster verschiebt sich von GapJump/Stairs (einfach) zu ZigZag/MovingPlatforms/NarrowBeams (schwer).

## 8. Items

Inventar: 4 Slots (Brücke, Trampolin, Enterhaken, Helikopter), **ohne Obergrenze** (v2; nur ein Überlaufschutz bei 999 999). Pickups werden immer eingesammelt. Ein Slot ist ausgewählt (HUD hebt ihn hervor). `use_item` verbraucht 1 Ladung, wenn der Einsatz gültig ist; ungültiger Einsatz verbraucht nichts und zeigt einen Hinweis im HUD.

| Item | Einsatz | Details |
|---|---|---|
| **Brücke** (`Bridge`) | Nur am Boden. Baut eine 2 m breite, 0,3 m dicke Planke in horizontaler Blickrichtung der Kamera, beginnend an der Fußposition, Länge 16 m. | Lebensdauer 20 s, danach blinkt sie 2 s und verschwindet. Kollisionsschicht `placeable`. |
| **Trampolin** (`Trampoline`) | Nur am Boden. Wird 1,5 m vor dem Spieler auf den Boden gesetzt (Raycast nach unten muss Boden treffen). | Betreten von oben → vertikaler Impuls 22 m/s (≈ 10 m Höhe). Lebensdauer 30 s. |
| **Helikopter** (`Helicopter`, v2) | Am Boden oder in der Luft (Taste 4 oder `use_item`). Erneutes Drücken beendet den Flug ohne Verbrauch. | Flugdauer 3,5 s, horizontal 9 m/s, Leertaste halten = steigen mit 6 m/s, sonst sinken mit 1,2 m/s, maximal 14 m über der Starthöhe. Endet auch bei Respawn, Trampolin-Sprung oder Enterhaken-Start. Rotor über dem Kopf, Tank-Anzeige im HUD. |
| **Enterhaken** (`GrappleHook`) | Am Boden oder in der Luft. Zielt auf den nächsten `grapple_anchor` innerhalb von 28 m im Blickkegel (25°) der Kamera mit freier Sichtlinie. | Zieht den Spieler mit 22 m/s zum Anker; löst bei < 1,5 m Abstand oder per `jump` (behält Schwung). Seil wird als dünner Zylinder dargestellt. Ein anvisierbarer Anker wird hervorgehoben. |

Maximal 3 platzierte Objekte (Brücken + Trampoline) gleichzeitig; beim vierten wird das älteste entfernt.

## 9. Kisten und Drop-Tabelle

- Kisten (1 × 1 × 1 m) stehen auf Plattformen, 0–3 pro normalem Chunk, Versorgungskisten in Item-Chunks.
- Ein Schlag (`attack`, Reichweite 2,2 m vor dem Spieler, Cooldown 0,35 s) zerstört eine Kiste (1 HP).
- Beim Zerschlagen: Partikel-Effekt, +25 Punkte Bonus, ggf. Pickup.
- Drop-Tabelle für Kisten ohne `ForcedDrop`:

| Ergebnis | Gewicht |
|---|---|
| Brücke | 27 |
| Trampolin | 27 |
| Enterhaken | 22 |
| Helikopter | 12 |
| nichts (nur Bonuspunkte) | 12 |

- Pickup: schwebt, rotiert, wird beim Berühren immer eingesammelt (unbegrenztes Inventar).

## 10. Punkte und Highscore

- Score = zurückgelegte maximale Distanz in Metern (ganzzahlig, inkl. `OriginOffsetZ`) + Kistenbonus.
- Highscore und beste Distanz werden in `user://highscore.json` gespeichert:
  ```json
  { "version": 1, "highscore": 1234, "best_distance": 1100 }
  ```
- Sicherheit/Robustheit beim Laden: Datei max. 4 KB, Parsen mit `System.Text.Json` in try/catch, unbekannte Felder ignorieren, Werte auf 0..10 000 000 begrenzen, falsche `version` oder kaputtes JSON → Defaults (0) und Warnung im Log, niemals Absturz. Speichern atomar (temporäre Datei schreiben, dann umbenennen). Nur feste Pfade unter `user://`, keine Pfade aus Benutzereingaben.
- Einstellungen (Mausempfindlichkeit 0,05–1,0, Y-Invertierung, v2: Charakter-Id, Musik- und Effektlautstärke 0..1) analog in `user://settings.json`. Unbekannte Charakter-Ids fallen auf `superhero` zurück, Lautstärken werden auf 0..1 begrenzt.
- Gespeichert wird bei: Rückkehr ins Hauptmenü, Beenden, Pause-Menü „Neustart“, und alle 30 s bei neuem Highscore.

## 11. Sturz, Respawn, Checkpoints

- Fällt der Spieler mehr als `FallDeathDepth = 20 m` unter die Y-Position des aktiven Checkpoints, wird er auf den Checkpoint zurückgesetzt (Geschwindigkeit 0, Enterhaken gelöst, Kamera hinter dem Spieler ausgerichtet).
- Kurzer Bildschirm-Flash im HUD, Sturzzähler +1. Kein Game Over (endloser Lauf); der Score bleibt.
- Checkpoint-Flagge wechselt beim Aktivieren das Material (`checkpoint_flag_active`).

## 12. Menüs und Ablauf

- **Hauptmenü** (`MainMenu.tscn`, Startszene): Titel, Highscore-Anzeige, optionales Seed-Feld (nur Ziffern, max. 9 Zeichen; leer = zufällig), Buttons „Start“, „Charakter: <Name>“ und „Beenden“.
- **Charakterauswahl** (`CharacterSelect.tscn`, v2): drehende 3D-Vorschau mit Demo-Animation, A/← und D/→ wechseln, Enter wählt und speichert, Esc zurück ohne Speichern; Maus und Kartenreihe gehen ebenso.
- **Spiel** (`Main.tscn`): Welt, Spieler, HUD, Pause-Menü.
- **Pause-Menü** (Esc): „Weiter“, „Neustart“, „Hauptmenü“, „Beenden“, Slider Mausempfindlichkeit, Musik und Effekte (v2, 0..1, Schritt 0,05; wirken sofort und werden gespeichert). `GetTree().Paused = true`, die Musik läuft weiter.
- **HUD:** Distanz, Highscore, 4 Item-Slots mit Anzahl (ohne Limit, z. B. „x1234“) und Tastenhinweis 1–4, Tank-Balken nur während des Helikopter-Flugs, Fadenkreuz, Hinweistext (z. B. „Kein Anker in Reichweite“), Respawn-Flash.

## 13. Optik und Atmosphäre (Designer)

- Himmel (v2: eigener Shader `art/shaders/sky.gdshader` mit Farbverlauf, Sonnenscheibe mit Halo und ruhenden Wolken), Nebel 80–200 m, der weit entfernte Chunks weich ausblendet (unterstützt das „Entstehen“ vorne). Nebel-, Himmels- und Hintergrund-Horizontfarbe sind identisch (`#BFDDF5`).
- Hintergrund (v2, `scenes/Backdrop.tscn` als `Main/Backdrop`): drei Ringe Low-Poly-Berge, schwebende Inseln, ein Wolkenmeer unter der Strecke und Schwebeteilchen nahe der Kamera. Unbeleuchtet, eigener Dunst, folgt der Kamera, unabhängig vom Floating Origin. Budget: 7 Draw Calls, ca. 43 000 Dreiecke, keine Schatten.
- Nicht-Box-Plattformen nutzen `platform_shape.gdshader` (Kantenbreite in Pixeln konstant, runde Mäntel ohne Nähte). Helikopter-Signaturfarbe Violett `#9B6BFF`.
- Renderer: alles läuft in Compatibility (OpenGL 3) und Forward+; keine Screen-/Depth-Texturen, CPUParticles, nur die Sonne wirft Schatten.
- Jedes Muster hat eine eigene Plattformfarbe, damit der Spieler Abschnitte sofort erkennt. Item-Pflicht-Abschnitte deutlich warnend (z. B. Orange/Rot-Akzente).
- Interaktive Objekte klar unterscheidbar: Kisten (Holz-Braun), Anker (leuchtend, Emission), Trampolin (Federfarbe), Brücke (halbtransparent-leuchtend).
- Lesbarkeit vor Detail; Performance-Ziel 60 FPS auf Mittelklasse-Hardware.

## 14. Nicht-Ziele

Kein Multiplayer, keine Gegner, keine heruntergeladenen Assets (Audio wird synthetisiert), keine Controller-Unterstützung (InputMap erlaubt späteres Ergänzen), kein Online-Highscore.

## 15. Charaktere (v2)

Vier Charaktere, alle prozedural (`scripts/Characters/`), mit Armen und Beinen, Füße bei y = 0, Blick nach −Z, je höchstens 24 Meshes, 4000 Dreiecke und 6 Materialien. Die Wahl ändert nur die Optik, nicht die Physik.

| Id | Name | Beschreibung | Farben |
|---|---|---|---|
| `bottle` | Flasche | Flinke PET-Flasche mit Schraubdeckel | Aqua `#8FE6DE`, Etikett Koralle `#FF7AA2`, Gold `#E8A317` |
| `trashcan` | Mülleimer | Tonne, die beim Springen den Deckel lüftet | Grün `#45A866`, Deckel `#2C7A47`, Banane Gold |
| `superhero` | Superheld (Standard) | Maske, Umhang im Wind, fliegt mit dem Arm voraus | Anzug `#3D5AFE`, Gold, Maske Navy |
| `knight` | Ritter | Helm mit Federbusch, Schild links, Schwert rechts | Stahl `#C9D1DC`, Wappenrock `#2F6FA8`, Augen `#7FE6FF` |

Animation (`CharacterRig`, liest `IPlayerAnimState` des Spielers): Idle, Laufen (Schrittzyklus folgt der echten Strecke, keine rutschenden Füße), Springen, Fallen, Fliegen (Helikopter), Enterhaken, Schlag (Arm/Schwert) und Landungs-Stauchung; weiche Überblendung zwischen den Zuständen, keine Allokationen pro Frame. Der Rotor sitzt auf `ModelTopHeight` des gewählten Charakters.

Debug: `-- --character=<id>` auf der Kommandozeile wählt einen Charakter für diesen Start, ohne die Settings zu ändern.

## 16. Audio (v2)

- Alle Klänge erzeugt `tools/gen_audio.py` (nur Python-Stdlib, deterministisch) nach `art/audio/`: Musik `music_main` (C-Dur-Chiptune, 105 BPM, 32 Takte, ca. 73 s, nahtloser Loop) und 17 Effekte. Prüfung: `python3 tools/verify_audio.py --determinism`.
- Busse `Master` → `Music`, `SFX` (`default_bus_layout.tres`, Limiter auf Master).
- `AudioManager` (Autoload nach `GameManager`) spielt die Musik szenenübergreifend (auch im Pause-Menü), verwaltet 12 + 8 Effekt-Stimmen und Loops und klickt bei jedem Button. Lautstärken kommen beim Start aus den Settings.
- `Main/AudioEventBridge` verbindet die Spieler-Signale mit Klängen:

| Ereignis | Sound |
|---|---|
| Springen / Landen | `jump` / `land` (max. 1× pro 0,15 s) |
| Schlag / Kiste zerstört | `swing` / `crate_break` (positional) |
| Item eingesammelt | `pickup` |
| Brücke / Trampolin / Enterhaken / Helikopter eingesetzt | `bridge_place` / `trampoline_place` / `grapple_shoot` / `helicopter_start` |
| Enterhaken greift | `grapple_attach` |
| Helikopter-Flug | `helicopter_loop` (Dauerschleife bis Flugende) |
| Trampolin-Sprung | `trampoline_bounce` |
| Respawn / Checkpoint | `respawn` / `checkpoint` (nicht in den ersten 0,5 s, nicht Chunk 0) |
| Item-Einsatz ungültig | `item_fail` |
| Button-Klick / -Hover | `ui_click` / `ui_hover` |

- Headless (Tests/CI) startet keine Musik und spielt keine Effekte (`AudioManager.PlaybackEnabled = false`), damit beim Beenden keine Playback-Objekte übrig bleiben; `AudioTest` schaltet die Wiedergabe gezielt ein.

