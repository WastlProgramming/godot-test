# JumpRun – Testbericht (Tester: Bugs & Sicherheit)

Stand: 2026-10-18 · Godot 4.6.3 .NET (headless) · Build `dotnet build JumpRun.csproj`: 0 Warnungen, 0 Fehler.

## 1. Ausgeführte Tests

| Test | Befehl (aus Projektroot, `G=.tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64`) | Ergebnis |
|---|---|---|
| Smoke (Entwickler) | `$G --headless --path . res://tests/SmokeTest.tscn` | PASS (inkl. LogicTests) |
| Gameplay (Entwickler) | `$G --headless --path . res://tests/GameplayTest.tscn` | PASS |
| Flow (Entwickler) | `$G --headless --path . res://tests/FlowTest.tscn` | PASS |
| **Langzeitlauf** (neu) | `$G --headless --fixed-fps 60 --path . res://tests/TesterLongRunTest.tscn` | PASS – 300 s simuliert, 10,8 km, 290 Chunks, 10 Origin-Shifts |
| **Grenzfälle** (neu) | `$G --headless --fixed-fps 60 --path . res://tests/TesterEdgeCaseTest.tscn` | 9 PASS / 5 FAIL (siehe Befunde) |
| **Savefile-Sicherheit** (neu) | `$G --headless --path . res://tests/TesterSecurityTest.tscn` | 15 PASS / 2 FAIL (Low) |
| **Geometrie-Lösbarkeit** (neu) | `$G --headless --path . res://tests/TesterGeometryTest.tscn` | 18 000 Chunks / 60 Seeds: nur Befund F-08 |
| **Item-Lösbarkeit mit Physik** (neu) | `$G --headless --fixed-fps 60 --path . res://tests/TesterItemSolvabilityTest.tscn` | 36/36 PASS (BigGap Brücke + Enterhaken, TallWall Trampolin, d = 0,1 / 0,6 / 1,0) |

Hinweis: `--fixed-fps 60` lässt die Simulation entkoppelt von der Echtzeit laufen (300 s Spielzeit in ~3 s). Der Sicherheitstest sichert vorhandene `user://`-Dateien und stellt sie wieder her. Alle Testläufe ohne Engine-Fehler/Warnungen im Log (Ausnahme F-10).

## 2. Befunde

| ID | Schwere | Ort | Kurzbeschreibung |
|---|---|---|---|
| F-01 | **high** | `scripts/World/WorldGenerator.cs:142` (+ `:173`) | Chunks zwischen aktivem Checkpoint und Spieler werden despawnt → Loch im Pfad, Run nach Respawn nicht mehr lösbar |
| F-02 | medium | `scripts/Items/Trampoline.cs:38-44`, `scenes/objects/Trampoline.tscn` (BounceArea r = 1,15), `scripts/Items/TrampolineItem.cs:22` | Trampolin katapultiert den Spieler sofort beim Platzieren bzw. bei seitlicher Berührung |
| F-03 | low | `scripts/Player/PlayerController.cs:224`, `scripts/Items/GrappleHook.cs:14-19` | `use_item` löst einen laufenden Enterhaken nicht, wenn 0 Ladungen übrig sind; „Enterhaken gelöst“ wird als Fehlerhinweis gemeldet |
| F-04 | low | `scripts/World/WorldGenerator.cs:197-205`, `scripts/World/Chunk.cs:60-68`, `scripts/Items/Crate.cs:54` | Punkte-Farming: Restock nach absichtlichem Sturz erzeugt beliebig oft neue Versorgungskisten (+25 je Kiste) |
| F-05 | low | `scripts/Game/SaveSystem.cs:194` | Savefile mit UTF-8-BOM wird verworfen → Highscore auf 0 zurückgesetzt |
| F-06 | low | `scripts/Game/SaveSystem.cs:168-171` (`TryGetInt`) | `"version": 1.9` wird als Version 1 akzeptiert |
| F-07 | low | `scripts/Items/TrampolineItem.cs:34`, `scripts/Items/Placeables.cs:37` | Trampolin auf beweglicher Plattform bleibt im Raum stehen (schwebt) – nur Code-Review, nicht per Test reproduziert |
| F-08 | low | `scripts/World/ChunkBuilder.cs:252` | BigGap-Lücke bis 16,0 m = `BridgeLength` → keine Toleranz für die Brücke |
| F-09 | low | `PlayerController.cs:183-184`, `GrappleHook.cs:52-54`, `TrampolineItem.cs:25-27`, `WorldGenerator.cs:135` | RefCounted-Allokationen pro Frame (Query-Parameter, Godot-Arrays) → Objektzahl wächst bis zum .NET-GC (~11 Objekte/s) |
| F-10 | low | `scripts/World/MaterialLibrary.cs:53`, statische Shapes `PlayerController.cs:44`, `TrampolineItem.cs:11` | Gelegentlich „RID allocations … leaked at exit“ beim Beenden (statische Ressourcen-Caches) |
| F-11 | low | `scripts/World/WorldGenerator.cs:151-153` | Rückwärtslaufen mit nach vorne gerichteter Kamera führt in bereits entfernte Chunks (Sturz) |

### F-01 (high) – Despawn reißt Loch zwischen Checkpoint und Spieler
- **Ort:** `WorldGenerator.Despawn`, Zeile 142: `if (c.Index >= current || c.Index == checkpoint) continue;` – geschützt sind nur der Spieler-Chunk, Chunks davor und **genau** der Checkpoint-Chunk. Gleiche Lücke im Hartlimit `OldestRemovableIndex` (Zeile 173).
- **Reproduktion:** `TesterEdgeCaseTest` → `CheckpointSkipHole`: Spieler überfliegt zwei Checkpoint-Pads in Folge (im Spiel z. B. per Trampolin – 10 m Sprunghöhe, Trigger nur 3 m hoch – oder Enterhaken), aktiver Checkpoint bleibt Chunk 10, Spieler erreicht Chunk 12. Chunk 11 liegt > 30 m hinten und außerhalb des Frustums → wird entfernt. Danach Sturz → Respawn auf Chunk 10.
- **Erwartet:** Alle Chunks von `ActiveCheckpointChunk` bis `CurrentChunkIndex` bleiben erhalten; nach Respawn ist der Pfad lückenlos.
- **Tatsächlich:** `FAIL CheckpointSkipHole: Chunk 11 fehlt (checkpoint=10, current=12)` und `FAIL PathContinuousAfterRespawn`. Der Spieler steht nach dem Respawn vor einer 40-m-Lücke; der Run ist ohne Neustart nicht fortsetzbar (Soft-Lock).
- **Fix-Vorschlag:** Bedingung auf `c.Index >= Math.Min(current, checkpoint)` ändern (alles ab dem Checkpoint schützen), analog in `OldestRemovableIndex`.

### F-02 (medium) – Trampolin löst ohne Betreten von oben aus
- **Ort:** Trampolin wird 1,5 m vor dem Spieler gesetzt (`TrampolineItem.cs:22`), die `BounceArea` hat Radius 1,15 m ab Bodenhöhe (Kapselradius 0,4 → Spieler ragt bereits hinein). `Trampoline.OnBodyEntered` prüft nur `Velocity.Y <= 1`.
- **Reproduktion:** `TesterEdgeCaseTest` → `TrampolineNoSideTrigger`: Spieler steht auf dem Boden, Trampolin auswählen, `use_item`.
- **Erwartet (GDD 8):** Impuls nur beim Betreten von oben.
- **Tatsächlich:** Spieler steigt sofort 9,47 m auf, ohne das Trampolin zu berühren. Gleiches gilt beim seitlichen Vorbeilaufen.
- **Fix-Vorschlag:** BounceArea flach über der Padfläche (z. B. Radius 0,8, Höhe 0,3, y ≈ 0,45) und zusätzlich prüfen, dass die Spielerfüße über der Padoberkante liegen.

### F-03 (low) – Enterhaken mit 0 Ladungen nicht per `use_item` lösbar
- **Ort:** `PlayerController.UseSelectedItem` (Zeile 224) bricht mit „Keine Ladung mehr“ ab, bevor `GrappleHook.TryUse` (Zeile 14-19, Lösen) erreicht wird. Zusätzlich meldet `TryUse` das Lösen als `ItemUseFailed("Enterhaken gelöst")`.
- **Reproduktion:** `TesterEdgeCaseTest` → `UseItemWhileGrappling(0 Ladungen)`: letzte Ladung verbrauchen (Haken aktiv), erneut `use_item`.
- **Erwartet:** Haken wird gelöst (wie bei verbleibenden Ladungen), kein Fehlerhinweis.
- **Tatsächlich:** `released=False reason='Keine Ladung mehr'`; Lösen nur per `jump` oder Zeitlimit 3 s.

### F-04 (low) – Kisten-Bonus-Farming über Restock
- **Ort:** `WorldGenerator.OnPlayerRespawned` → `Chunk.RestockSupply` ohne Begrenzung; `Crate.Hit` vergibt jedes Mal `CrateBonusScore`.
- **Reproduktion:** `TesterEdgeCaseTest` → `CrateBonusFarming`: im Item-Chunk Versorgungskisten zerschlagen, Pickups nicht einsammeln, absichtlich abstürzen; 3 Zyklen.
- **Erwartet:** Bonus höchstens einmal je Kiste (hier 50).
- **Tatsächlich:** +150 nach 3 Zyklen, unbegrenzt wiederholbar → Highscore manipulierbar (nur lokal).
- **Fix-Vorschlag:** Restock-Kisten ohne Bonus oder Restock nur, wenn keine passenden Pickups mehr im Chunk liegen.

### F-05 (low) – Savefile mit UTF-8-BOM wird verworfen
- **Ort:** `SaveSystem.ReadLimited` dekodiert mit `UTF8Encoding(false, true).GetString` – das BOM bleibt als `﻿` erhalten, `JsonDocument.Parse` scheitert.
- **Reproduktion:** `TesterSecurityTest` → `Utf8BomAccepted`: `EF BB BF {"version":1,"highscore":321,...}` nach `user://highscore.json`.
- **Erwartet:** Highscore 321 (oder dokumentiertes Verwerfen).
- **Tatsächlich:** Defaults (0); beim nächsten Speichern wird der Rekord überschrieben. Kein Sicherheitsproblem, nur Datenverlust nach manueller Bearbeitung.

### F-06 (low) – Gleitkomma-Version wird akzeptiert
- **Reproduktion:** `TesterSecurityTest` → `FractionalVersionRejected`: `{"version":1.9,"highscore":42}`.
- **Erwartet:** falsche Version → Defaults (GDD 10). **Tatsächlich:** `highscore=42` (Version wird abgerundet).
- **Fix-Vorschlag:** Für `version` nur `TryGetInt32` (ganzzahlig) zulassen.

### F-07 (low, nur Code-Review) – Trampolin auf beweglicher Plattform
- Trampolin wird unter `WorldRoot/Placeables` in Weltkoordinaten gesetzt; der Raycast (`TrampolineItem.cs:25-34`) trifft auch `MovingPlatform`. Die Plattform fährt weg, das Trampolin schwebt. **Erwartet:** Platzieren auf beweglichen Plattformen ablehnen oder Trampolin an die Plattform hängen.

### F-08 (low) – BigGap ohne Toleranz zur Brückenlänge
- `gap = Lerp(12, 14, d) + Range(0, 2)` → bis 16,0 m = `BridgeLength`. Da der Spieler nie exakt an der Kante steht, fehlen bis ~0,6 m (Geometrietest: 561 von 18 000 Chunks > 15,6 m). Im Physiktest (Abstand 0,6 m zur Kante, Lücke 15,98 m) läuft der Spieler trotzdem hinüber – die Kapsel überwindet den Spalt. Kein Blocker, aber knapp. **Vorschlag:** max. Lücke 15 m.

### F-09 (low) – Allokationen pro Frame
- `IsEdgeAhead` (nur DebugAutoRun), `GrappleHook.FindTarget` (alle 0,1 s je Anker), `WorldGenerator.Despawn` (`GetFrustum` jeden Frame), `TrampolineItem`/`TryAttack` erzeugen `PhysicsRayQueryParameters3D`/`Godot.Collections.Array` neu.
- **Langzeitlauf:** Ohne erzwungenen GC wächst `ObjectCount` 2360 → 5319 in 270 s; mit `GC.Collect()` bleibt er bei ~2000 (Nodes konstant 275–333, Orphans 0). **Kein echtes Leck**, aber GC-Druck entgegen CONTRACT 10 („keine Allokationen pro Frame“). **Vorschlag:** Query-Objekte als Felder cachen.

### F-10 (low) – „RID allocations leaked at exit“
- Einmal beobachtet beim Beenden des Langzeitlaufs: `ERROR: 2 RID allocations of type '...DummyMaterial' / '...DummyShader' were leaked at exit.` Ursache: statischer `MaterialLibrary`-Cache und statische Shape-Ressourcen werden erst nach dem RenderingServer freigegeben (abhängig vom GC-Zeitpunkt). Nur Log-Rauschen. **Vorschlag:** Caches beim `NotificationWMCloseRequest`/`_ExitTree` des GameManagers leeren.

### F-11 (low) – Rückwärts in entfernte Chunks
- Despawn prüft das Frustum der Kamera, nicht die Laufrichtung. Läuft der Spieler mit `S` rückwärts (Kamera nach vorne), betritt er nach > 30 m einen bereits entfernten Bereich und stürzt. Laut GDD vertretbar, sollte aber bewusst entschieden sein.

## 3. Geprüft ohne Befund

- **Langzeitlauf (300 s, 10,8 km):** Chunks stets 7–8 (≤ 12), Spieler- und Checkpoint-Chunk existieren immer, Nodes stabil (275–333), keine Orphans, keine Exceptions.
- **Floating Origin / Float-Präzision:** 10 Verschiebungen, |Z| bleibt < 1000 m, Distanz 10 766 m vs. erwartet 10 767 m (Abweichung < 1 m), `OriginOffsetZ` ganzzahlig (10 003).
- **Origin-Shift auf beweglicher Plattform:** Spieler bleibt auf der Plattform (Abweichung 0,0 m) – kein Katapult durch die Teleportation der AnimatableBody.
- **Items mit 0 Ladungen:** alle 3 Typen → „Keine Ladung mehr“, nichts platziert, nichts verbraucht.
- **Item-Spam (12× `use_item` in 24 Frames, 5 Brücken):** 5 verbraucht = 5 erzeugt, nie mehr als 3 Placeables gleichzeitig.
- **Enterhaken ohne Anker:** keine Ladung verbraucht, Hinweis „Kein Anker in Reichweite“.
- **Pause während Enterhaken:** Position während 60 Pause-Frames unverändert, Haken bleibt aktiv und erreicht danach den Anker.
- **Anker während Enterhaken gelöscht / Respawn während Enterhaken:** sauber gelöst, Seil unsichtbar, keine Exceptions.
- **Sturz/Respawn + Restock:** Versorgungskisten werden nach Sturz ohne passende Items wiederhergestellt.
- **Lösbarkeit:** Musterregeln (keine Item-Chunks 0–2, nie zwei in Folge, max. 2× gleiches Muster), Vorgänger-Chunk hat immer das benötigte Item, ≥ 2 Versorgungskisten, alle normalen Sprünge (inkl. Seitversatz, bewegliche Plattformen im günstigsten Moment) ballistisch erreichbar, alle Kisten stehen auf Plattformen; physikalisch: BigGap per Brücke und Enterhaken, TallWall per Trampolin bei d = 0,1/0,6/1,0 gelöst.
- **Savefiles:** > 4 KB / 1 MB, ungültiges UTF-8, tiefe Verschachtelung, negative/riesige/`1e999`-Werte, Duplikat-Schlüssel, falsche Typen, leere Datei, Verzeichnis statt Datei → Defaults ohne Absturz; atomares Schreiben erhält das Original, wenn `.tmp` nicht schreibbar ist; Werte werden beim Schreiben geklemmt; keine `.tmp`-Reste.
- **Laufzeit-Eingaben:** `MouseSensitivity = NaN/1000` geklemmt; `ReportPlayerZ(NaN/±∞)` und `AddOriginOffset(∞)` ignoriert, Score ≤ 10 000 000; Seed-Feld nur ASCII-Ziffern, max. 9 Stellen.
- **Code-Audit:** Keine Reflection-Typerzeugung, kein `OS.Execute`, kein Netzwerk, kein `unsafe`/P/Invoke. Alle `ResourceLoader.Load`-Pfade sind Konstanten (`MaterialLibrary.Get` nur mit `MaterialNames`-Konstanten); Dateizugriff nur über `IsAllowedPath` (Highscore/Settings + `.tmp`). JSON-Parsing in try/catch, `MaxDepth = 8`.

## 4. Neue Testdateien (nur `tests/**`)

`tests/TesterLongRunTest.cs/.tscn`, `tests/TesterEdgeCaseTest.cs/.tscn`, `tests/TesterSecurityTest.cs/.tscn`, `tests/TesterGeometryTest.cs/.tscn`, `tests/TesterItemSolvabilityTest.cs/.tscn`. Der Grenzfall- und der Sicherheitstest beenden sich bewusst mit Exit-Code 0 und geben `PASS`/`FAIL` je Fall aus, damit bekannte Befunde den Build nicht blockieren; nach den Fixes können sie auf Exit 1 bei FAIL umgestellt werden.

## 5. Fix-Status (Entwickler, Fix-Runde)

| ID | Status | Umsetzung |
|---|---|---|
| F-01 | fixed | `WorldGenerator.ProtectedFromIndex()` = min(Checkpoint-Chunk, Spieler-Chunk). Despawn und Hartlimit (`OldestRemovableIndex`) schützen alles ab dem Checkpoint. `CheckpointSkipHole`/`PathContinuousAfterRespawn` PASS |
| F-02 | fixed | `Trampoline` löst nur über `ShouldBounce` aus: Füße auf Pad-Höhe (−0,3…0,9 m), horizontal ≤ 0,85 m von der Mitte, `Velocity.Y ≤ 1`, 0,3 s Cooldown. Die große BounceArea ist entfernt. Platzierung 1,5 m vor dem Spieler liegt damit außerhalb. `TrampolineNoSideTrigger` PASS |
| F-03 | fixed | `PlayerController.UseSelectedItem` löst einen aktiven Enterhaken vor der Ladungsprüfung (auch bei 0 Ladungen) ohne Fehlerhinweis. Der Lösen-Zweig in `GrappleHook.TryUse` ist entfernt |
| F-04 | fixed | Nachgelieferte Versorgungskisten (`Chunk.RestockSupply`) haben `Crate.GivesBonus = false`. `CrateBonusFarming` PASS |
| F-05 | fixed | `SaveSystem` überspringt ein UTF-8-BOM vor dem Parsen. `Utf8BomAccepted` PASS |
| F-06 | fixed | `version` wird nur als echte Ganzzahl akzeptiert. `FractionalVersionRejected` PASS |
| F-07 | fixed | `TrampolineItem` lehnt das Platzieren auf `MovingPlatform` ab („Nicht auf beweglichen Plattformen“) |
| F-08 | fixed | BigGap = 10–13,5 m, gedeckelt auf `BigGapMax = BridgeLength − 2,5`. Die Brücke rastet bis 20° Abweichung auf die Laufrichtung ein. Geometrietest: 0 Befunde |
| F-09 | fixed | Ray-Query-Parameter und Exclude-Array pro Spieler gecacht (`PlayerController.RayQuery`). Trampolin-Shape-Query gecacht. Anker-Registry statt `GetNodesInGroup`. `GetFrustum` nur noch alle 10 Physik-Frames |
| F-10 | fixed | `GameManager._ExitTree` leert `MaterialLibrary`, den Szenen- und Mesh-Cache von `ChunkBuilder` und die statischen Ressourcen von `TrampolineItem`. Die Spieler-Shape ist pro Instanz. In den Testläufen dieser Runde keine „leaked“-Meldungen |
| F-11 | declined | Bewusste Designentscheidung: Despawn folgt dem Sichtfeld der Kamera und dem Abstand, nicht der Laufrichtung (Vorgabe „hinten entfernen, wenn außer Sicht“). Wer mehr als 30 m rückwärts läuft, stürzt und respawnt am immer geschützten Checkpoint (F-01), es entsteht also kein Soft-Lock. Im Code dokumentiert (`WorldGenerator.Despawn`) |

Zusätzlich: Grenzfall- und Sicherheitstest beenden sich bei einem FAIL mit Exit 1. Seit dieser Runde gilt das auch für Geometrie- und Item-Lösbarkeitstest.

Verifikation nach der Fix-Runde: `dotnet build` 0 Warnungen / 0 Fehler, `--import` ohne Fehler. Alle 8 Testszenen enden mit Exit 0: Smoke, Gameplay, Flow und LongRun PASS, Edge 16/0, Security 17/0, Geometry 18 000 Chunks mit 0 Befunden, ItemSolve 36/0.
