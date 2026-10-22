# JumpRun – endloses 3D-Jump-and-Run (Godot 4.6 .NET / C#)

Ein prozedurales 3D-Jump-and-Run: Vor dir wird die Parkour-Strecke laufend neu generiert, hinter dir werden Abschnitte entfernt, sobald sie außer Sicht und weit genug entfernt sind. Die Strecke mischt verschiedene Sprungmuster (Lücken, Treppen, Zickzack, bewegliche Plattformen, Balken, Höhenstufen) mit Hindernissen, die du nur mit Items schaffst. Diese Items findest du in Kisten, die du zerschlägst:

- **Brücke** – überspannt große Lücken (BigGap, 10–13,5 m, Brücke 16 m).
- **Trampolin** – katapultiert dich über hohe Wände (TallWall).
- **Enterhaken** – zieht dich zu einem Anker im Blickfeld (funktioniert bei beiden Hindernissen).
- **Helikopter** – 3,5 s fliegen: Leertaste halten zum Steigen, sonst sinkst du langsam (funktioniert bei beiden Hindernissen).

Neu in v2:

- **Vier Charaktere** mit animierten Armen und Beinen: Flasche, Mülleimer, Superheld (Standard) und Ritter. Auswahl im Hauptmenü über „Charakter: <Name>“ (A/D oder Pfeiltasten wechseln, Enter wählt, Esc zurück). Die Wahl wird gespeichert.
- **Unbegrenztes Inventar:** Du kannst von jedem Item beliebig viele mitnehmen.
- **Plattformen in elf Formen:** Quadrat, Raute, Zylinder, Scheibe, Dreieck, Fünf-, Sechs- und Achteck, Rampe, Kreuz und Box, dazu die neuen Muster Säulen, drehende Scheiben und Formen-Mix.
- **Musik und Soundeffekte**, komplett per Python synthetisiert. Musik- und Effektlautstärke stellst du im Pause-Menü ein.
- **Neue Grafik:** eigener Himmel mit Sonne und Wolken, Bergringe, schwebende Inseln und ein Wolkenmeer im Hintergrund.

Alle Meshes sind prozedural (BoxMesh, CylinderMesh, ArrayMesh …), Materialien und Shader liegen als Text-Ressourcen (`.tres`, `.gdshader`) im Projekt, die Audiodateien erzeugt ein Python-Skript. Externe Assets gibt es nicht.

Die ausführliche Dokumentation liegt als VitePress-Website unter `website/` (siehe unten).

## Voraussetzungen

- **Godot 4.6.x, .NET-Version** (z. B. `Godot_v4.6.3-stable_mono`). Die Standard-Version ohne .NET kann keine C#-Projekte öffnen.
- **.NET SDK 8 oder neuer** (das Projekt zielt auf `net8.0` und nutzt `RollForward=LatestMajor`, läuft also auch mit .NET 9/10).

## Im Editor öffnen

1. Godot 4.6 .NET starten.
2. Im Projektmanager **Importieren** wählen und `project.godot` in diesem Ordner auswählen.
3. Beim ersten Öffnen importiert Godot die Ressourcen. Danach oben rechts auf **Build** klicken (oder `dotnet build` im Projektordner ausführen).
4. **F5** startet das Spiel mit dem Hauptmenü (`res://scenes/ui/MainMenu.tscn`). Mit **F6** startest du die gerade geöffnete Szene, z. B. `scenes/Main.tscn` direkt ohne Menü.

Im Hauptmenü kannst du optional einen **Seed** eingeben (nur Ziffern, max. 9 Stellen). Gleicher Seed ergibt dieselbe Strecke. Bleibt das Feld leer, wird ein zufälliger Seed gewählt.

## Steuerung

| Aktion | Taste |
|---|---|
| Laufen | W / A / S / D |
| Sprinten | Shift (halten) |
| Springen | Leertaste (länger halten = höher; löst auch den Enterhaken; im Helikopter: steigen) |
| Kamera | Maus oder Pfeiltasten; ohne Eingabe dreht sie sich nach ca. 1 s hinter die Laufrichtung. Unter WSL: Maus an den Fensterrand halten dreht weiter (absoluter Mausmodus, erzwingen mit `-- --mouse=captured` oder `-- --mouse=absolute`) |
| Kiste zerschlagen | linke Maustaste oder F |
| Gadget einsetzen | 1 = Brücke, 2 = Trampolin, 3 = Enterhaken, 4 = Helikopter (sofort; beim aktiven Haken bzw. im Flug: beenden) |
| Item wählen | Mausrad |
| Item benutzen | E oder rechte Maustaste (beim aktiven Enterhaken: lösen) |
| Pause-Menü | Esc (dort: Weiter, Neustart, Hauptmenü, Beenden, Mausempfindlichkeit, Musik, Effekte, Grafik niedrig/hoch) |

Wenn du abstürzt, startest du am letzten Checkpoint neu (Fahnen-Pad am Anfang jedes Abschnitts; im Helikopterflug werden überflogene Checkpoints automatisch aktiviert). Fehlt dir dort das passende Item, werden die Versorgungskisten im Abschnitt wieder aufgefüllt. Die Punkte setzen sich aus der zurückgelegten Strecke und einem Bonus für zerschlagene Kisten zusammen. Highscore und Einstellungen werden unter `user://` gespeichert (`highscore.json`, `settings.json`).

## Projektstruktur

```
project.godot          Projekteinstellungen, InputMap, Autoloads GameManager + AudioManager, Physik-Layer
default_bus_layout.tres Audio-Busse Master / Music / SFX
JumpRun.csproj/.sln    C#-Projekt (Godot.NET.Sdk 4.6.3, net8.0)
scenes/
  Main.tscn            Spielszene: Spieler, WorldRoot (Chunks, Placeables, WorldGenerator), Licht,
                       Backdrop (Hintergrund), AudioEventBridge (Spieler-Signale → Sounds)
  Backdrop.tscn        Berge, Inseln, Wolkenmeer
  Player.tscn          CharacterBody3D, CharacterRig (animiertes Modell), Third-Person-Kamera (SpringArm)
  Crate.tscn, Pickup.tscn
  objects/             Bridge, Trampoline, GrappleAnchor, Checkpoint, HelicopterRotor
  ui/                  MainMenu, CharacterSelect, HUD, PauseMenu
scripts/
  Game/                GameManager (Autoload: Score, Seed, Run-Zustand), GameConstants, SaveSystem, Main
  Player/              PlayerController (Bewegung, Coyote-Time, Jump-Buffer, Items), ThirdPersonCamera
  World/               WorldGenerator (Spawn vorne / Despawn hinten / Floating Origin), ChunkBuilder
                       (Sprungmuster), Chunk, ChunkLayout, MovingPlatform, Checkpoint, GrappleAnchor,
                       FrustumUtil, MaterialLibrary, PatternType, PlatformShape, ShapeMeshFactory,
                       RotatingPlatform, Backdrop
  Items/               Inventory, Crate, Pickup, BridgeItem/Bridge, TrampolineItem/Trampoline, GrappleHook,
                       HelicopterItem, HelicopterRotor
  Characters/          CharacterCatalog, CharacterModelFactory, CharacterRig (Animation), CharacterMaterials
  Audio/               AudioManager (Autoload), AudioEventBridge, Sfx
  UI/                  HUD, MainMenu, CharacterSelect, PauseMenu
art/                   Shader (platform, platform_shape, sky, backdrop, crate), Materialien (.tres),
                       characters/, audio/ (.wav), Umgebung/Licht, STYLE.md
tools/                 gen_audio.py (Audio-Synthese), verify_audio.py
website/               VitePress-Dokumentation
tests/                 Headless-Tests (Szene + C#-Skript je Test)
docs/                  GDD, CONTRACT, CONTRACT_V2, TASKS, TASKS_V2, TEST_REPORT, REVIEW, integration-notes/
```

So funktioniert die Endlos-Welt: Der `WorldGenerator` hält immer etwa 6 Abschnitte (Chunks) vor dem Spieler bereit und baut höchstens einen Chunk pro Physik-Frame. Ein Chunk hinter dem Spieler wird entfernt, wenn er mehr als 30 m entfernt **und** außerhalb des Kamera-Frustums liegt. Alle Chunks ab dem aktiven Checkpoint bleiben dabei erhalten. Als harte Obergrenze gelten 12 Chunks. Ab 1000 m wird die Welt per Floating Origin zurückgeschoben, damit die Float-Genauigkeit erhalten bleibt.

## Tests headless ausführen

Aus dem Projektordner (`G` = Pfad zur Godot-.NET-Binary, hier z. B. `.tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64`):

```bash
dotnet build                                   # 0 Fehler erwartet
$G --headless --path . --import                # Ressourcen importieren (einmalig bzw. nach Änderungen)

$G --headless --path . res://tests/SmokeTest.tscn                     # Spawn/Despawn, Logiktests
$G --headless --path . res://tests/GameplayTest.tscn                  # Springen, Kisten, Items, Respawn
$G --headless --path . res://tests/FlowTest.tscn                      # Menü → Spiel → Pause → Menü
$G --headless --fixed-fps 60 --path . res://tests/TesterLongRunTest.tscn         # 300 s Langzeitlauf, Floating Origin
$G --headless --fixed-fps 60 --path . res://tests/TesterEdgeCaseTest.tscn        # Grenzfälle (Checkpoint, Trampolin, Enterhaken …)
$G --headless --path . res://tests/TesterSecurityTest.tscn                       # manipulierte Savefiles
$G --headless --path . res://tests/TesterGeometryTest.tscn                       # Lösbarkeit von 18 000 Chunks
$G --headless --fixed-fps 60 --path . res://tests/TesterItemSolvabilityTest.tscn # BigGap/TallWall mit Physik lösbar
$G --headless --fixed-fps 60 --path . res://tests/ShapeCollisionTest.tscn        # Kollision je Plattform-Form
$G --headless --fixed-fps 60 --path . res://tests/HelicopterTest.tscn            # Flugphysik, Tank, Flugende
$G --headless --fixed-fps 60 --path . res://tests/CharacterTest.tscn             # 4 Charaktere, Animation, Settings
$G --headless --fixed-fps 60 --path . res://tests/AudioTest.tscn                 # Streams, Busse, Signal-Bridge
$G --headless --fixed-fps 60 --path . res://tests/VisualsTest.tscn               # Materialien, Himmel/Nebel, Backdrop-Budget
$G --headless --fixed-fps 60 --path . res://tests/TesterV2Test.tscn              # v2-Grenzfälle (Flugketten, Audio, Saves, Grafikstufe)
```

Alle Tests in einem Durchlauf:

```bash
for t in tests/*.tscn; do $G --headless --fixed-fps 60 --path . "res://$t" > /dev/null 2>&1; echo "$t -> exit $?"; done
```

Jeder Test beendet sich mit Exit-Code **0**, wenn alles bestanden ist, und mit einem Wert ungleich 0 bei einem Fehler. Die letzte Ausgabezeile fasst das Ergebnis zusammen (z. B. `SMOKE_TEST_PASS`, `EDGE_TEST_DONE pass=16 fail=0`). Mit `--fixed-fps 60` läuft die Simulation schneller als in Echtzeit. Die Tests deaktivieren das Speichern (`PersistenceEnabled = false`), damit dein echter Highscore unverändert bleibt. Der Sicherheitstest sichert vorhandene `user://`-Dateien vorher und stellt sie danach wieder her.

Die Testskripte unter `tests/` werden bei einem Release-Export (`ExportRelease`) nicht mitkompiliert.

Headless (Tests, CI) startet der `AudioManager` keine Musik und spielt keine Effekte, damit beim Beenden keine Audio-Objekte übrig bleiben. Spiel direkt mit einem bestimmten Charakter bzw. einer Grafikstufe starten (beides wird nicht gespeichert):

```bash
$G --headless --path . --max-fps 60 --quit-after 600 res://scenes/Main.tscn -- --character=knight   # bottle | trashcan | superhero | knight
$G --path . res://scenes/Main.tscn -- --quality=low                                                 # low | high
```

**Grafikqualität:** Im Pause-Menü schaltet „Grafik: niedrig (schneller)“ SSAO ab und reduziert die Sonnenschatten auf 2 Splits bis 60 m. Ohne gespeicherte Wahl ist im Compatibility-Renderer (`run-linux.sh`) automatisch „niedrig“ aktiv, in Forward+ „hoch“. Als Physik-Engine ist Jolt Physics fest eingestellt.

## Audio neu erzeugen

```bash
python3 tools/gen_audio.py                       # schreibt art/audio/*.wav (nur Python-Stdlib, deterministisch)
python3 tools/verify_audio.py --determinism      # Format, Pegel, Loops, Größe → AUDIO_VERIFY_PASS
$G --headless --path . --import                  # danach neu importieren
```

## Dokumentation (VitePress)

```bash
cd website
npm ci
npm run docs:dev      # lokale Vorschau
npm run docs:build    # statische Seite nach website/.vitepress/dist
```

## Git

Das Projekt ist ein Git-Repository (Branch `main`). `.godot/`, `.tools/`, `bin/`, `obj/` und die VitePress-Build-Ordner sind ausgeschlossen; `*.uid`- und `*.import`-Dateien werden eingecheckt. Ein Remote ist nicht eingerichtet:

```bash
git remote add origin <url>
git push -u origin main
```
