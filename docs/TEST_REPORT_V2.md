# JumpRun v2 – Testbericht (Tester: Bugs & Sicherheit)

Stand: 2026-10-18 · Godot 4.6.3 .NET (headless, `--fixed-fps 60`) · `dotnet build`: 0 Warnungen, 0 Fehler.
Umfang: die v2-Änderungen aus `docs/CONTRACT_V2.md` (Helikopter, unbegrenztes Inventar, Plattformformen,
Charakterauswahl, Rig-Animation, Audio, Performance, Save-Validierung).

## 1. Ausgeführte Tests

| Test | Ergebnis |
|---|---|
| Alle bestehenden `tests/*.tscn` (Audio, Character, Flow, Gameplay, Helicopter, ShapeCollision, Smoke, TesterEdgeCase, TesterGeometry, TesterItemSolvability, TesterLongRun, TesterSecurity, Visuals) | alle Exit 0 |
| **neu** `tests/TesterV2Test.tscn` (`$G --headless --fixed-fps 60 --path . res://tests/TesterV2Test.tscn`) | `TESTER_V2_DONE pass=25 known=7 fail=0`, Exit 0, keine Engine-Fehler/Leaks im Log |

`TesterV2Test` meldet die unten gelisteten Befunde als `KNOWN V2-xx` (der Gesamtlauf bleibt grün). Ist ein Befund
behoben, erscheint `PASS … (V2-xx behoben)`. Mit `-- --strict` zählen bekannte Befunde als FAIL (Exit 1).

Ohne Befund geprüft:
- **Inventar:** `Add(int.MaxValue)` ×2 sättigt bei 999 999, kein Überlauf. `None`/`99`/`0`/`-5` werden abgelehnt.
  `Consume` bei 0 → false. `SelectNext` läuft im Kreis 4 → 1 → 4. `ItemAdded` kommt bei jedem erfolgreichen `Add`.
- **HUD:** „x999999“ in allen 4 Slots. Die Slot-Leiste braucht 471 px (die Bar hat 540 px), es wird nichts abgeschnitten.
- **Save-Felder:** `character` mit doppeltem Schlüssel, NUL-Byte, Großbuchstaben, Leerzeichen, Homoglyph (ı),
  Array/Objekt/Zahl und > 4 KB fällt jeweils auf `superhero` zurück. Ein gültiges `knight` wird als `knight`
  akzeptiert. Lautstärken mit ±1e309, als String, `null`, bool, Array, −0, > 1 oder als Denormal-Zahl werden auf
  endliche Werte in 0..1 bzw. den Default gesetzt. `SerializeSettings` schreibt nie eine ungültige Id/NaN.
- **Audio:** 400 × `PlaySfx` (2D, 3D, unbekannte Namen) plus 200 × `SetLoop`-Wechsel. Danach existiert nur 1 Player
  mehr (der Loop-Player wird einmal angelegt). Der 2D-Pool bleibt auf ≤ 12 begrenzt, unbekannte Namen werden
  einmal gewarnt.
- **Helikopter:** Origin-Shift mitten im Flug ohne Geschwindigkeitssprung. Die Rig-Pivots bleiben über 40 Flüge
  endlich (auch beim Stillstand). Nach dem Flug ist der Pfad ab dem Checkpoint lückenlos.
- **Sturztod auf gültiger Plattform** (Checkpoints per Flug übersprungen): nicht reproduzierbar. In 400 Seeds liegt
  keine Plattform der nächsten 25 Chunks mehr als 13,4 m unter dem letzten Checkpoint (Grenze 20 m).
- Bereits durch Entwicklertests abgedeckt und unauffällig: Charakterauswahl/Fallback/Persistenz (`CharacterTest`),
  Formkollision (`ShapeCollisionTest`), Geometrie über 18 000 Chunks (`TesterGeometryTest`) und Flugphysik,
  Tank sowie Flugende durch Respawn/Bounce/Grapple (`HelicopterTest`).

## 2. Befunde

| ID | Schwere | Ort | Kurzbeschreibung |
|---|---|---|---|
| V2-01 | medium | `scripts/Player/PlayerController.cs:291`, `:318`, `:385` | Verkettete Helikopterflüge ohne globale Höhengrenze → Spieler verlässt die Welt nach oben (562 m) |
| V2-02 | medium | `scripts/World/WorldGenerator.cs:140`, `:157`, `:184` | Beim Dauerflug über Checkpoints wachsen die lebenden Chunks unbegrenzt (38 statt ≤ 12, 1269 Nodes) |
| V2-03 | low | `scenes/objects/Checkpoint.tscn:23` (Triggerhöhe), `scripts/World/Checkpoint.cs` | Flug über Pads aktiviert keine Checkpoints → Respawn bis zu > 1 km zurück |
| V2-04 | low | `scripts/Items/TrampolineItem.cs:43` | Trampolin auf `RotatingPlatform` erlaubt → schwebt 34 % der Zeit ohne Boden |
| V2-05 | low | `scripts/Player/PlayerController.cs:397-401` | `use_item` mit gewähltem Helikopter im Enterhaken löst nur den Haken, startet keinen Flug |
| V2-06 | low | `scripts/Audio/AudioManager.cs:78`, `:85`, `:92` | `AudioManager.MusicVolume = NaN` setzt die Bus-Lautstärke auf NaN dB |
| V2-07 | low | `scripts/Audio/AudioManager.cs:122`, `:264` | Helikopter-Loop läuft im Pause-Menü weiter (Flug eingefroren) |
| V2-08 | info | `scripts/Game/GameManager.cs:131` | Debug-Override `--character=` wird beim nächsten `SaveAll` dauerhaft gespeichert |

### V2-01 (medium) – Unbegrenzte Flughöhe durch verkettete Flüge
- **Ort:** `PlayerController.StartFlight` setzt `_flightStartY = GlobalPosition.Y` (Z. 291). Die Decke ist
  `_flightStartY + HelicopterMaxRise` (Z. 318) und gilt damit nur relativ zum jeweiligen Flugstart. Taste 4 beendet
  den Flug (Z. 385), ein zweiter Druck startet sofort einen neuen.
- **Repro:** 40 Ladungen (Inventar ist unbegrenzt, 12 % Dropchance), `jump` halten und je Flug zweimal Taste 4
  drücken (`TesterV2Test` → `HeliChainAltitudeBounded`).
- **Erwartet:** Die Höhe bleibt in Weltnähe, z. B. eine globale Decke `MaxWorldY + HelicopterMaxRise`
  bzw. `Max(startY, letzte Bodenhöhe) + MaxRise`.
- **Tatsächlich:** 562 m über dem Start (MaxWorldY = 40). Der Spieler fliegt über Nebel und Backdrop hinaus.
  Der Backdrop folgt in Y nur gedämpft (±18 m Lag). Der Rücksturz dauert bis zu 7 s.
- **Fix-Vorschlag (GAMEPLAY):** `ceiling = Mathf.Min(_flightStartY, lastGroundY) + MaxRise`, wobei `lastGroundY` beim
  letzten `IsOnFloor` gemerkt wird. Alternativ ein Cooldown/Verbot für einen Neustart in der Luft direkt nach einem Flug.

### V2-02 (medium) – Unbegrenztes Chunk-Wachstum beim Dauerflug
- **Ort:** `WorldGenerator.Despawn`/`ProtectedFromIndex` (Z. 140/184) schützt alle Chunks ab dem aktiven Checkpoint.
  Das harte Limit (Z. 157) entfernt nur ungeschützte Chunks.
- **Repro:** wie V2-01. Mit `DebugAutoRun` 40 Flüge über 1170 m; der aktive Checkpoint bleibt 0 (siehe V2-03).
- **Erwartet:** `AliveChunkCount ≤ MaxAliveChunks (12)`, Node-Anzahl ≈ 400 (wie im LongRun).
- **Tatsächlich:** max. 38 lebende Chunks, 1269 Nodes (332 am Start). Das wächst linear mit der Flugstrecke
  (≈ 1 Chunk pro Ladung). Das kostet mehr Physik-Broadphase, Speicher und `_PhysicsProcess` aller Plattformen und
  Kisten. Bei mehreren hundert Ladungen wird es zum Performance-Problem (Ziel: 60 fps, iGPU).
- **Fix-Vorschlag:** Den Checkpoint beim Überfliegen nachziehen (V2-03), oder den Schutz auf
  `[checkpoint, checkpoint+1] ∪ [current-1, …]` begrenzen und beim Respawn fehlende Chunks deterministisch neu
  bauen. `Plan(index)` ist deterministisch (Seed + Index), braucht aber `entry`/`prevPattern`; diese pro Index
  in einer kleinen Tabelle merken.

### V2-03 (low) – Checkpoints werden überflogen
- **Ort:** Der Checkpoint-Trigger ist nur ≈ 3 m hoch (`Checkpoint.tscn`). Im Flug liegt man meist darüber.
- **Repro:** `HeliChainSkipsCheckpoints`: 31 Chunks überflogen, aktiver Checkpoint bleibt 0.
- **Erwartet:** Ein Checkpoint wird aktiviert, sobald der Spieler den Pad-Chunk passiert (unabhängig von der Höhe),
  oder der Trigger ist hoch genug (≥ HelicopterMaxRise + Pfad-Variation).
- **Tatsächlich:** Endet der Flug über einer Lücke, respawnt man am letzten berührten Pad, im Test 1,1 km zurück
  (GameManager zählt das als einen Sturz). Lösbar bleibt alles, die Chunks sind ja geschützt (V2-02).
- **Fix-Vorschlag:** In `WorldGenerator.UpdatePlayerChunk` bei `IsFlying` und einem Chunkwechsel
  `SetCheckpoint(chunk.CheckpointPosition, idx)` für Nicht-Item-Chunks aufrufen. Alternativ die
  `CollisionShape` des Triggers auf ≈ 30 m Höhe vergrößern.

### V2-04 (low) – Trampolin auf drehender Plattform
- **Ort:** `TrampolineItem.TryUse` Z. 43 lehnt nur `MovingPlatform` ab (Fix F-07), nicht `RotatingPlatform`.
- **Repro:** `TrampolineOnRotatingPlatform`: Spieler steht auf einem drehenden Dreieck (Size 6 m, 0,9 rad/s) und setzt
  das Trampolin 2 m vor der Mitte. Danach wird 7 s lang per Ray nach unten geprüft.
- **Erwartet:** „Nicht auf beweglichen Plattformen“ wie bei `MovingPlatform`.
- **Tatsächlich:** Das Trampolin wird gesetzt und steht in 144 von 420 Frames ohne Boden in der Luft. Die
  Dreiecksfläche dreht unter dem statischen Trampolin hindurch.
- **Fix (GAMEPLAY):** `is MovingPlatform or RotatingPlatform`. `BridgeItem` braucht das nicht, die Brücke ist
  ohnehin freistehend.

### V2-05 (low) – `use_item` + Helikopter während des Enterhakens
- **Ort:** `PlayerController.UseSelectedItem` Z. 397–401: Ist ein Enterhaken aktiv, wird er immer nur gelöst
  (Fix F-03). CONTRACT_V2 §3.3 verlangt: „Läuft gerade ein Enterhaken, wird er mit StopGrapple(true) beendet und
  der Flug startet“. Aktiviert wird per Taste 4 **oder** `use_item` bei gewähltem Helikopter.
- **Repro:** `UseItemHeliDuringGrapple`: Haken läuft, Mausrad wählt den Helikopter, dann `use_item`.
- **Erwartet:** Haken endet, Flug startet (wie bei Taste 4, die korrekt funktioniert).
- **Tatsächlich:** `grappling=False flying=False`, der Flug startet nicht.
- **Fix:** In `UseSelectedItem` vor dem Grapple-Zweig
  `if (IsGrappling && Inventory.Selected == ItemType.Helicopter && Inventory.GetCount(ItemType.Helicopter) > 0) { UseItem(ItemType.Helicopter); return; }`.

### V2-06 (low) – NaN-Lautstärke am AudioManager
- **Ort:** Die Setter `MasterVolume`/`MusicVolume`/`SfxVolume` (Z. 78/85/92) nutzen `Mathf.Clamp`. NaN bleibt dabei
  NaN, und `LinearToBusDb(NaN)` ergibt NaN dB.
- **Repro:** `AudioManager.Instance.MusicVolume = float.NaN` → `get=NaN`, `AudioServer.GetBusVolumeDb(Music)=NaN`.
- **Erwartet:** Nicht-endliche Werte werden ignoriert oder auf den alten Wert gesetzt (wie `SaveSystem.ClampVolume`).
- **Tatsächlich:** Bus-dB ist NaN; je nach Audiotreiber stummer oder gestörter Bus. Aktuell ist das kein
  Spieler-Pfad (Slider und Settings liefern nur endliche Werte), aber die API ist öffentlich.
- **Fix (AUDIO):** `if (!float.IsFinite(value)) return;` in allen drei Settern.

### V2-07 (low) – Helikopter-Loop im Pause-Menü
- **Ort:** `AudioManager` läuft mit `ProcessMode = Always` (Z. 122). Der Loop-Player aus `SetLoop` (Z. 264) erbt das
  und spielt bei `GetTree().Paused` weiter.
- **Repro:** `AudioHeliLoopPausedWithGame`: Flug starten, `PauseMenu.Toggle()` → `IsLoopActive("helicopter_loop") == true`.
- **Erwartet:** Gameplay-Loops pausieren mit dem Spiel; nur die Musik läuft weiter (§6.3).
- **Tatsächlich:** Das Rotorgeräusch dröhnt im Pause-Menü weiter, obwohl der Flug eingefroren ist.
- **Fix (AUDIO):** Loop-Player mit `ProcessMode = Pausable` anlegen (`StreamPaused` folgt der Pause), oder
  `AudioEventBridge` auf `NotificationPaused`/`Unpaused` reagieren lassen.

### V2-08 (info) – Debug-Charakter wird persistiert
- **Ort:** `GameManager.ApplyCommandLineOverrides` Z. 131 setzt `_selectedCharacter` direkt; `SaveAll` schreibt das Feld.
- **Repro (Code-Review):** Start mit `-- --character=knight`, im Pause-Menü die Sensitivität ändern (Settings dirty)
  → `settings.json` enthält `"character": "knight"`.
- **Erwartet laut Doc-Kommentar:** „ohne die Settings zu ändern“.
- **Fix (CHARACTERS):** Override getrennt halten (`_characterOverride`) und in `SaveAll` den geladenen Wert schreiben.

## 3. Hinweise ohne Befund
- **Helikopter + Item-Pflichtstrecken:** BigGap/TallWall per Flug zu überqueren ist laut Entwicklertest
  sauber. `RestockSupply` unterbleibt bei vorhandener Helikopter-Ladung (gültige Alternative). Die
  Versorgungskisten garantieren weiterhin Brücke bzw. Trampolin.
- **Performance:** Formplattformen teilen sich gecachte Meshes je (Form, Material), Draw Calls bleiben ≈ 1 pro
  Plattform wie in v1. Headless lassen sich Draw Calls nicht messen. Die Node-Anzahl im Normalbetrieb ist
  unverändert (LongRun max. 432). Problematisch ist nur der Dauerflug (V2-02).
- **Rig:** `CharacterRig._Process` klemmt `dt`, prüft `IsFinite` für alle Eingaben und den Grapple-Quaternion. Beim
  Origin-Shift gibt es keine Weltkoordinaten-Abhängigkeit außer dem Grapple-Ziel, das mitverschoben wird.
- **Audio-Hook:** `AudioManager.OnNodeAdded` läuft für jeden neuen Node (auch Chunk-Plattformen). Das ist pro Node
  ein kleiner Managed-Callback, im Profil unkritisch. Bei Bedarf nur Nodes unter `CanvasLayer`/`Control` prüfen.

## 4. Fix-Status (Fix-Runde v2)

Nachtest: `dotnet build` 0 Warnungen / 0 Fehler, `--import` sauber, alle 14 `tests/*.tscn` Exit 0,
`TesterV2Test`: `TESTER_V2_DONE pass=34 known=0 fail=0` (auch mit `-- --strict`), `npm run docs:build` ok.

| ID | Status | Umsetzung |
|---|---|---|
| V2-01 | behoben | `PlayerController.StartFlight`: Decke = `min(Y, letzte Bodenhöhe + HelicopterAirStartAllowance (4 m)) + HelicopterMaxRise`. `_lastGroundY` wird bei jedem Bodenkontakt und beim Respawn gesetzt. Nachtest: max. 18 m statt 562 m über Start. |
| V2-02 | behoben | Folge von V2-03: der geschützte Bereich wandert mit. Nachtest: max. 9 lebende Chunks, 410 Nodes. |
| V2-03 | behoben | `WorldGenerator.UpdatePlayerChunk`: Im Flug wird beim Eintritt in einen neueren Chunk dessen Checkpoint über `Checkpoint.Activate` gesetzt (inkl. Fahne). Auch Item-Chunks, da der Respawn dort fehlende Items per Restock nachliefert. Nachtest: Checkpoint 0 → 31. |
| V2-04 | behoben | `TrampolineItem.TryUse` lehnt `MovingPlatform or RotatingPlatform` ab. |
| V2-05 | behoben | `PlayerController.UseSelectedItem`: Ist der Helikopter gewählt und vorhanden, startet `use_item` während des Hakens den Flug (Haken endet mit Momentum). |
| V2-06 | behoben | `AudioManager.Master/Music/SfxVolume` ignorieren nicht-endliche Werte, ebenso die Übernahme aus dem GameManager. |
| V2-07 | behoben | Loop-Player mit `ProcessMode = Pausable` (pausiert mit dem Baum). `IsLoopActive` meldet pausierte Loops als inaktiv. |
| V2-08 | behoben | `GameManager`: getrenntes Feld `_persistedCharacter`. `--character=` ändert nur die aktive Wahl, `SaveAll` schreibt den gespeicherten bzw. im Menü gewählten Wert. |
