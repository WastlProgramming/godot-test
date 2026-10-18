# Tests ausführen

Alle Tests laufen **headless** – ohne Fenster, Grafikkarte oder Audiogerät. Jeder Test besteht aus einer Szene
(`tests/*.tscn`) und einem C#-Skript (`tests/*.cs`). Er beendet sich mit Exit-Code **0**, wenn alles bestanden ist,
und mit einem Wert ungleich 0 bei einem Fehler. Die letzte Ausgabezeile fasst das Ergebnis zusammen.

## Vorbereitung

`G` ist der Pfad zur Godot-.NET-Binary, unter Linux/WSL z. B.:

```bash
G=.tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64
dotnet build                     # 0 Fehler erwartet
$G --headless --path . --import  # Ressourcen importieren (einmalig bzw. nach Änderungen)
```

Unter Windows funktioniert dasselbe mit `Godot_v4.6.3-stable_mono_win64_console.exe` in PowerShell.

## Die Tests

| Test | Prüft | Letzte Zeile |
|---|---|---|
| `SmokeTest` | Spawn/Despawn, Logiktests (Inventar, Formgeometrie, Meshes, Drop-Tabelle, Musterabdeckung) | `SMOKE_TEST_PASS` |
| `GameplayTest` | Springen, Kisten, Items, Respawn | `GAMEPLAY_TEST_PASS` |
| `FlowTest` | Menü → Spiel → Pause → Menü | `FLOW_TEST_PASS` |
| `TesterLongRunTest` | 300 s Langzeitlauf, Floating Origin | `LONGRUN_TEST_PASS` |
| `TesterEdgeCaseTest` | Grenzfälle (Checkpoint, Trampolin, Enterhaken …) | `EDGE_TEST_DONE pass=… fail=0` |
| `TesterSecurityTest` | manipulierte Savefiles | `SECURITY_TEST_DONE pass=… fail=0` |
| `TesterGeometryTest` | Lösbarkeit von 18 000 Chunks | `GEOMETRY_TEST_DONE …` |
| `TesterItemSolvabilityTest` | `BigGap`/`TallWall` mit echter Physik lösbar | `ITEM_SOLVE_DONE pass=… fail=0` |
| `ShapeCollisionTest` | Kollision je Plattformform, drehende Scheibe | `SHAPE_COLLISION_TEST_PASS` |
| `HelicopterTest` | Flugphysik, Tank, Flugende, Taste 4, Rotor | `HELICOPTER_TEST_PASS` |
| `CharacterTest` | 4 Charaktere, Animation, Budget, Settings | `CHARACTER_TEST_PASS` |
| `AudioTest` | Streams, Busse, Signal-Bridge | `AUDIO_TEST_PASS` |
| `VisualsTest` | Materialien, Himmel/Nebel, Backdrop-Budget | `VISUALS_TEST_PASS` |
| `TesterV2Test` | v2-Grenzfälle: Inventar-Überlauf, Save-Felder, Audio-Pool/Pause, verkettete Flüge (Höhe, Checkpoints, Chunk-Limit), Grafikqualität | `TESTER_V2_DONE pass=… known=0 fail=0` (`-- --strict`: bekannte Befunde zählen als Fehler) |

Einzeln starten:

```bash
$G --headless --path . res://tests/SmokeTest.tscn
$G --headless --fixed-fps 60 --path . res://tests/HelicopterTest.tscn
```

Mit `--fixed-fps 60` läuft die Simulation schneller als in Echtzeit – sinnvoll für die physiklastigen Tests.

## Alle Tests in einem Durchlauf

```bash
for t in tests/*.tscn; do
  $G --headless --fixed-fps 60 --path . "res://$t" > /dev/null 2>&1
  echo "$t -> exit $?"
done
```

## Hinweise

- Die Tests schalten das Speichern ab (`PersistenceEnabled = false`), dein echter Highscore bleibt unverändert. Der
  Sicherheitstest sichert vorhandene `user://`-Dateien vorher und stellt sie danach wieder her.
- Headless spielt der `AudioManager` keine Musik und keine Effekte ab.
- Testskripte unter `tests/` werden beim Release-Export (Konfiguration `ExportRelease`) nicht mitkompiliert.
- Audio lässt sich ohne Godot prüfen: `python3 tools/verify_audio.py --determinism` → `AUDIO_VERIFY_PASS`.
