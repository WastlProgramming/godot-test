# JumpRun v2 – Aufgaben und Akzeptanzkriterien

Verbindliche Schnittstellen: `docs/CONTRACT_V2.md` (Abschnittsangaben §). Jede Rolle liefert am Ende einen kurzen
Bericht (geänderte Dateien, Testergebnisse, offene Punkte) und trägt Fremdänderungswünsche in
`docs/integration-notes/<rolle>.md` ein. Gemeinsame Pflicht für alle: `dotnet build` mit 0 Fehlern (für die eigenen
Dateien), keine neuen Warnungen/Fehler in Headless-Läufen, nur eigene Dateien geändert (§1).

Reihenfolge: **Phase 1 parallel** GAMEPLAY · CHARACTERS · AUDIO · VISUALS (DOCS darf parallel starten) →
**Phase 2** INTEGRATOR (danach DOCS-Feinschliff) → Abschlusstest.

---

## G – GAMEPLAY

**G1 Unbegrenztes Inventar** (§2, §3.2)
- [ ] `MaxItemCharges` entfernt, `InventoryHardCap`/`ItemSlotCount` ergänzt; `Inventory.Add` gibt für gültige Items
  immer true; `ItemAdded`-Signal; `InventoryChanged` mit 4 Argumenten.
- [ ] Test: 50× `Add(Bridge)` → `GetCount == 50`; Pickup wird bei beliebigem Bestand eingesammelt;
  `Add(None)`, `Add(x, 0)`, `Consume(None)` → false. SmokeTest-Inventarprüfung entsprechend umgestellt.

**G2 Helikopter** (§3.3, §5.3)
- [ ] `ItemType.Helicopter = 4`, `HelicopterItem.TryUse`, Taste 4 (`item_4`) wie 1–3, `use_item`-Pfad, Abbruch durch
  erneutes Drücken ohne Verbrauch.
- [ ] Flug: steigt mit gehaltener Leertaste (≤ 6 m/s), sinkt sonst langsam (1,2 m/s), horizontal 9 m/s, Deckel bei
  +14 m, endet nach 3,5 s (±1 Physik-Frame) und danach normale Schwerkraft.
- [ ] Flugende bei Respawn, Trampolin-Bounce, Enterhaken-Start; Signale `FlightStarted/FlightEnded` genau einmal je Flug.
- [ ] Rotor-Szene `scenes/objects/HelicopterRotor.tscn` + `HelicopterRotor.cs`, zur Laufzeit an `Player/HelicopterRotor`
  auf Höhe `ModelTopHeight`, nur im Flug sichtbar/drehend; fehlt die Szene → kein Fehler.
- [ ] `RequiredItems` erweitert (BigGap/TallWall + Helicopter); Drop-Tabelle 27/27/22/12/12 (LogicTests-Verteilung
  ±2 % bei 100 000 Würfen); Pickup-Material `pickup_helicopter`; Icon `art/ui/icons/helicopter.svg`.
- [ ] Physik-Tests (`--fixed-fps 60`): Helikopter überquert eine BigGap-Lücke (13,5 m) und eine TallWall (7 m) aus
  echten Layouts; Fuel läuft ab; Respawn während des Flugs beendet ihn.

**G3 Spielerzustand für das Rig** (§5.2, §5.3)
- [ ] `PlayerController : CharacterBody3D, IPlayerAnimState`, alle Mitglieder gemäß Doc-Kommentaren; `AnimMode`-Priorität
  korrekt; Zähler `AttackCount/JumpCount/LandCount` synchron zu den Signalen.
- [ ] Neue Signale `Attacked, ItemUsed(int), GrappleStarted, GrappleEnded, FlightStarted, FlightEnded, Bounced`
  mit exakter Arität; v1-Signale unverändert.
- [ ] Zugriffe auf `Visual/Body`/`Visual/Visor` bleiben null-sicher (CHARACTERS entfernt sie).

**G4 Plattform-Formen** (§4)
- [ ] `PlatformShape` (11 Werte, Reihenfolge exakt), `PlatformSpec`-Felder/-Properties, `ChunkLayout.WalkableGap`,
  `ChunkLayout.SupportsPoint`, `ShapeMeshFactory` (flache Normalen, UV/UV2-Vertrag), `RotatingPlatform`,
  `MaterialLibrary.GetShapeVariant`, neue `MaterialNames`-Konstanten für alle Dateien aus §7.2.
- [ ] Kollision passt zur Darstellung: neuer Test `tests/ShapeCollisionTest.tscn` setzt den Spieler über jede Form
  (inkl. Ramp, Cross, drehende Disc) → nach ≤ 2 s `IsOnFloor()` und Fuß-Y = `TopY` ±0,1 m (Ramp: Oberfläche an der
  XZ-Position ±0,1 m); Spieler bleibt auf einer drehenden Disc 5 s lang oben.
- [ ] Neue Muster `Pillars, SpinningDiscs, ShapeMix` (am Enum-Ende) + Formvariation der bestehenden Muster; `Start`,
  `NarrowBeams`, Pad, Landing, `BigGap`, `TallWall` bleiben Boxen in v1-Reihenfolge.
- [ ] In 20 Chunks eines Laufs kommen mindestens 4 verschiedene Nicht-Box-Formen vor (Test über 50 Seeds × 100 Chunks:
  jede `PlatformShape` erscheint mindestens einmal).
- [ ] `TesterGeometryTest` nutzt `WalkableGap`/`EntryTopY`/`SupportsPoint` und meldet über 18 000 Chunks **0 Issues**;
  `InnerRadius ≥ 1,2 m`, Rampen ≤ 25°, `|SpinSpeed| ≤ 0,9`.
- [ ] Floating Origin: `IsOnMovingPlatform` erkennt auch `RotatingPlatform`; `TesterLongRunTest` (300 s) besteht.
- [ ] Performance: Unit-Mesh-/Shape-Cache, keine Mesh-Erzeugung pro Plattform-Instanz außer gecachten Größen;
  Chunk-Bau bleibt ≤ 1 pro Physik-Frame ohne spürbaren Ruckler (Build eines Chunks < 4 ms im LongRun-Log).

**G5 HUD** (§8)
- [ ] 4 Slots, „x12“-Format ohne Limit, Tankbalken nur im Flug, aktualisierter Steuerungshinweis.

**G6 Tests gesamt**
- [ ] Alle `tests/*.tscn` (alt + neu) mit Exit 0, inkl. `TesterItemSolvabilityTest`, `TesterEdgeCaseTest`,
  `TesterSecurityTest`.

---

## C – CHARACTERS

**C1 Daten + Modelle** (§5.4)
- [ ] `CharacterDefinition`, `CharacterCatalog` (4 Einträge in Reihenfolge bottle, trashcan, superhero, knight;
  Default `superhero`), `CharacterModelFactory`.
- [ ] Jeder Charakter klar erkennbar: Flasche (Hals + Deckel + Etikett), Mülleimer (Rillen + Deckel-Kopf), Superheld
  (Umhang + Maske + Emblem), Ritter (Helm mit Visier/Federbusch + Schwert + Schild); alle mit Armen und Beinen.
- [ ] Limits je Charakter eingehalten (≤ 24 MeshInstance3D, ≤ 4000 Dreiecke, ≤ 6 Materialien); Höhe 1,6–2,0 m;
  Blick nach −Z, Füße bei y = 0.

**C2 Rig + Animation** (§5.5)
- [ ] `CharacterRig` mit Knotenhierarchie `Hips/Body/{Head,ArmL,ArmR}`, `Hips/{LegL,LegR}` exakt benannt.
- [ ] Laufzyklus skaliert mit Tempo (Stillstand = Idle ohne Fußrutschen-Optik), Sprung-, Fall-, Flug-, Enterhaken-,
  Schlag- und Landepose; weiche Übergänge; Umhang/Deckel/Schwert reagieren.
- [ ] Funktioniert ohne `IPlayerAnimState` (Preview) und mit; setzt `ModelTopHeight`.
- [ ] Keine Allokationen in `_Process`; keine Fehler headless.

**C3 Player.tscn** (§5.1)
- [ ] `Visual/Rig` statt Body/Visor; alle anderen Knoten unverändert; `Camera3D.far = 1500`.

**C4 Auswahl-Screen + Menü** (§5.6)
- [ ] `CharacterSelect.tscn/.cs` mit allen Unique Names, drehender 3D-Vorschau (eigene World3D), Tastatur und Maus,
  Bestätigen speichert, Zurück verwirft.
- [ ] `MainMenu`: `%CharacterButton` zeigt aktuellen Charakter; bestehende Knoten/Funktionen unverändert
  (`FlowTest` besteht).

**C5 Persistenz** (§5.6)
- [ ] `GameManager.SelectedCharacter/MusicVolume/SfxVolume`, `CharacterChanged`, `CharacterSelectScenePath`,
  `item_4` in `EnsureInputMap`.
- [ ] `settings.json` enthält `character`, `music_volume`, `sfx_volume`; ungültige Werte (falscher Typ, unbekannte Id,
  `"../../x"`, 10 000 Zeichen, NaN, −5, 7) → Defaults/Clamp ohne Absturz. `TesterSecurityTest` besteht.

**C6 Test** `tests/CharacterTest.tscn`
- [ ] Baut alle 4 Charaktere, prüft Hierarchie-Namen, Limits, `ModelHeight`; simuliert Rig mit Fake-`IPlayerAnimState`
  (Run/Jump/Fly/Grapple) ohne Exceptions; Catalog-Fallback; Settings-Roundtrip. Ausgabe `CHARACTER_TEST_PASS`.

---

## A – AUDIO

**A1 Generator** (§6.1)
- [ ] `python3 tools/gen_audio.py` läuft mit Stdlib in < 60 s, deterministisch (zweiter Lauf → identische Bytes).
- [ ] `music_main.wav` 60–90 s, angenehm (Melodie + Bass + Drums/Percussion, Akkordfolge, keine Dauer-Rauschflächen),
  nahtloser Loop (Sample-Differenz am Loop-Punkt klein, kein DC-Offset), Peak ≤ −1 dBFS.
- [ ] Alle SFX aus §6.1 vorhanden, kurz, klickfrei (Attack/Release-Hüllkurven), `helicopter_loop` nahtlos.
- [ ] `art/audio/` gesamt ≤ 6 MB.

**A2 AudioManager + Busse** (§6.2, §6.3)
- [ ] `default_bus_layout.tres` mit Master/Music/SFX.
- [ ] API exakt wie §6.3; Pools mit Voice-Stealing; Lautstärke linear → dB; Loops zur Laufzeit; Button-Klicks
  automatisch; unbekannte Namen nur einmal gewarnt.
- [ ] Headless (Dummy-Audio) ohne Fehler.

**A3 AudioEventBridge** (§6.4)
- [ ] Verbindet per String-Signalnamen mit Prüfung `HasSignal`; fehlt ein Signal (Gameplay noch nicht fertig) →
  kein Fehler; Mapping-Tabelle vollständig; Trennung in `_ExitTree`; Drosselung `land`, Startphase-Unterdrückung.

**A4 Test** `tests/AudioTest.tscn`
- [ ] Instanziert `AudioManager` (falls kein Autoload) und Bridge mit einem Dummy-Knoten, der die Signale aus §5.3
  deklariert; feuert alle Signale; prüft: alle Streams ladbar, Loops gesetzt, Volumes 0/0.5/1 → erwartete dB,
  keine Exceptions. Ausgabe `AUDIO_TEST_PASS`.

---

## V – VISUALS

**V1 Himmel + Atmosphäre** (§7.1, §7.3)
- [ ] `sky.gdshader` (Verlauf, Sonne + Halo, weiche Wolken) in `world_environment.tres`; Nebel so abgestimmt, dass
  neue Chunks weiterhin ausgeblendet erscheinen (Ende ≤ 220 m) und der Horizont nahtlos in den Himmel übergeht.
- [ ] Licht: wärmere Sonne, angenehme Ambient-Farbe, optional schattenloses Fülllicht.

**V2 Backdrop** (§7.1)
- [ ] `scenes/Backdrop.tscn` + `Backdrop.cs`: ferne Berg-/Insel-Silhouetten in Schichten mit Dunst, schwebende Inseln,
  Wolkenbänke unter der Strecke, leichte Partikel; folgt der Kamera; stetig über Floating-Origin-Shifts
  (Test: zwei Frames vor/nach einem Shift → kein sichtbarer Sprung, d. h. Elementpositionen relativ zur Kamera
  ändern sich < 0,5 m).
- [ ] Budget: ≤ 16 Draw Calls, ≤ 60 000 Dreiecke, kein Schattenwurf, keine Texturdateien.

**V3 Materialien** (§7.2)
- [ ] Alle Dateien aus §7.2 vorhanden; `platform_shape.gdshader` mit pixelkonstanter Kante über UV2, Deck-/Seitenfarbe,
  Welt-Rauschen, Kachelung auf Deckflächen, funktioniert auf allen 11 Formen.
- [ ] Helikopter-Materialien (Pickup, Rotor, Nabe) passend zur Signaturfarbe.
- [ ] `art/STYLE.md` um neue Farben/Shader ergänzt.

**V4 Renderer-Kompatibilität**
- [ ] Alle Shader kompilieren unter Compatibility und Forward+ (kein `hint_screen_texture`, kein `hint_depth_texture`,
  keine Forward+-only-Built-ins). Prüfung, falls Display vorhanden: `./run-linux.sh --quit-after 600` ohne
  `SHADER ERROR` in der Ausgabe; sonst Begründung im Bericht.
- [ ] Headless `--import` ohne Fehler; optional `tests/VisualsTest.tscn` (lädt Backdrop + Environment, prüft
  Material-Pfade, simuliert Origin-Shift) mit `VISUALS_TEST_PASS`.

---

## D – DOCS (VitePress)

- [ ] `website/` gemäß §9 inkl. `.gdignore`, exakte VitePress-Version, `package-lock.json`.
- [ ] Alle Seiten aus §9 mit echtem Inhalt (deutsch), Steuerung inkl. Taste 4, alle Charaktere, alle Plattformformen,
  Items mit unbegrenztem Inventar, Audio-Neuerzeugung, Teststrategie, Renderer-Regeln.
- [ ] `npm ci && npm run docs:build` erfolgreich, keine toten Links; Godot `--import` ignoriert `website/`.

---

## I – INTEGRATOR

- [ ] §10 Punkte 1–6 vollständig; alle Integration-Notes erledigt oder begründet abgelehnt.
- [ ] Spielablauf manuell/automatisch: Menü → Charakter wählen → Start → Rig animiert, Musik läuft, SFX bei
  Sprung/Kiste/Pickup/Items, Taste 4 fliegt mit Tankbalken, neue Formen sichtbar, Hintergrund sichtbar,
  Pause-Slider für Musik/SFX wirken und werden gespeichert.
- [ ] Alle Tests grün, `dotnet build` 0 Fehler, Docs-Build grün.
- [ ] `.gitignore`/`.gitattributes`, `git init -b main`, Initial-Commit; `git status` sauber; Repo-Größe ohne `.tools`
  und `.godot` < 30 MB. Kein Remote, kein Push.
