# Audio

Musik und Soundeffekte sind **komplett synthetisiert** – mit der Python-Standardbibliothek, ohne externe Samples.
Die erzeugten `.wav`-Dateien liegen eingecheckt unter `art/audio/`.

## Musik

`music_main.wav`: ein freundlicher Chiptune-Loop in C-Dur, **105 BPM, 32 Takte (≈ 73 s)**. Er wird auf einem
ringförmigen Puffer gerendert: Ausklänge und Echos über das Ende hinaus landen wieder am Anfang. Dadurch ist der
Loop-Punkt nahtlos, ganz ohne Fade.

Die Musik startet automatisch, läuft szenenübergreifend (Menü → Spiel) und auch im Pause-Menü weiter.

## Soundeffekte

| Name | Auslöser |
|---|---|
| `jump` | Sprung |
| `land` | Landung (höchstens alle 0,15 s) |
| `swing` | Schlag |
| `crate_break` | Kiste zerbricht (positional an der Kiste) |
| `pickup` | Item eingesammelt |
| `bridge_place` | Brücke gelegt |
| `trampoline_place` | Trampolin gestellt |
| `trampoline_bounce` | Trampolin-Impuls |
| `grapple_shoot` | Enterhaken abgefeuert |
| `grapple_attach` | Enterhaken greift (0,1 s nach dem Schuss) |
| `helicopter_start` | Helikopter gestartet |
| `helicopter_loop` | Rotorgeräusch während des Flugs (Loop) |
| `respawn` | Respawn |
| `checkpoint` | neuer Checkpoint |
| `item_fail` | Item-Einsatz nicht möglich |
| `ui_click` | Klick auf einen Button |
| `ui_hover` | Maus über einem Button (sehr leise) |

Format: Mono, 22 050 Hz, 16 Bit PCM, jede Datei auf −3 dBFS Spitze normalisiert. Die Lautstärkebalance zwischen den
Sounds steht in `Sfx.TrimDb(name)`, die Musik liegt zusätzlich um `AudioManager.MusicBaseDb = -7 dB` tiefer.

## Busse

`default_bus_layout.tres` definiert `Master` → `Music` und `SFX`. Fehlt die Datei, legt der `AudioManager` die Busse
zur Laufzeit an. Im **Pause-Menü** stellst du Musik- und Effektlautstärke ein (linear 0..1, 0 = stumm); die Werte
werden in `user://settings.json` gespeichert (`music_volume`, `sfx_volume`, Standard 0,7 / 0,8).

## `AudioManager` (Autoload)

```csharp
AudioManager.Instance?.PlayMusic("music_main", fadeSeconds: 1f);
AudioManager.Instance?.StopMusic(0.5f);
AudioManager.Instance?.PlaySfx(Sfx.Jump);                       // 2D
AudioManager.Instance?.PlaySfx(Sfx.CrateBreak, crate.GlobalPosition); // 3D
AudioManager.Instance?.SetLoop(Sfx.HelicopterLoop, true);
AudioManager.Instance!.MusicVolume = 0.5f;
```

- **Pools:** 12 × `AudioStreamPlayer` (2D) und 8 × `AudioStreamPlayer3D`; ist ein Pool voll, wird die älteste Stimme
  übernommen.
- **Laden:** `res://art/audio/{name}.wav` mit Cache. Unbekannte Namen erzeugen nur einmal eine Warnung.
- **Loops** werden zur Laufzeit gesetzt (`LoopMode = Forward`), unabhängig von den Import-Einstellungen.
- **UI-Sounds:** Der Manager hängt sich an `SceneTree.NodeAdded` und verbindet jeden `BaseButton` automatisch mit
  `ui_click` bzw. `ui_hover`.
- **Headless** (Tests/CI) ist die Wiedergabe abgeschaltet (`PlaybackEnabled = false`), damit beim Beenden keine
  Audio-Objekte übrig bleiben.

## `AudioEventBridge`

Hängt als `Main/AudioEventBridge` in der Spielszene, sucht den Spieler über die Gruppe `player` und verbindet sich
**per Signalname** mit dessen Signalen und mit `Inventory.ItemAdded`. In den ersten 0,5 s nach Szenenstart werden
`respawn` und `checkpoint` unterdrückt, damit der Start nicht klingelt.

## Audio neu erzeugen

```bash
python3 tools/gen_audio.py                    # schreibt art/audio/*.wav (deterministisch)
python3 tools/verify_audio.py --determinism   # Format, Pegel, Loops, Größe → AUDIO_VERIFY_PASS
$G --headless --path . --import               # danach in Godot neu importieren
```

Alle Zufallsquellen haben feste Seeds, zwei Läufe erzeugen bytegleiche Dateien. Die Gesamtgröße von `art/audio/`
muss unter 6 MB bleiben.
