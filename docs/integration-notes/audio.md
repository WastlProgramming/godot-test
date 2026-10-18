# Integration-Notes AUDIO

Alles Audio-Eigene ist fertig (`tools/gen_audio.py`, `tools/verify_audio.py`, `art/audio/*.wav`,
`default_bus_layout.tres`, `scripts/Audio/{AudioManager,AudioEventBridge,Sfx}.cs`, `tests/AudioTest.{tscn,cs}`).
Für den INTEGRATOR bleiben folgende Änderungen in fremden Dateien:

## 1. `project.godot`
```ini
[autoload]

GameManager="*res://scripts/Game/GameManager.cs"
AudioManager="*res://scripts/Audio/AudioManager.cs"
```
Bus-Layout: `res://default_bus_layout.tres` wird von Godot automatisch geladen (Default-Pfad von
`audio/buses/default_bus_layout`), kein Eintrag nötig. Fehlt es, legt `AudioManager` die Busse `Music`/`SFX` zur
Laufzeit an.

## 2. `scenes/Main.tscn`
Knoten `Main/AudioEventBridge` (Typ `Node`, Skript `res://scripts/Audio/AudioEventBridge.cs`), z. B.:
```
[ext_resource type="Script" path="res://scripts/Audio/AudioEventBridge.cs" id="X_audio"]
[node name="AudioEventBridge" type="Node" parent="."]
script = ExtResource("X_audio")
```
Die Bridge sucht den Spieler deferred über die Gruppe `player` (bis 5 s lang) und dessen Kind `Inventory`; fehlende
Signale werden still übersprungen.

## 3. Lautstärken (GameManager → AudioManager) und Pause-Menü
- Beim Start (z. B. in `GameManager._Ready` nach dem Laden der Settings oder in `Main._Ready`):
  ```csharp
  if (JumpRun.Audio.AudioManager.Instance is { } am)
  { am.MusicVolume = GameManager.Current.MusicVolume; am.SfxVolume = GameManager.Current.SfxVolume; }
  ```
  Achtung Autoload-Reihenfolge: `GameManager` vor `AudioManager`; im `GameManager._Ready` ist `AudioManager.Instance`
  noch null → Zuweisung deferred (`CallDeferred`) oder in `Main._Ready`/`MainMenu._Ready` machen.
- Pause-Menü: `%MusicSlider`/`%SfxSlider` `ValueChanged` → `GameManager.MusicVolume = v` **und**
  `AudioManager.Instance?.MusicVolume = v` (analog SFX).
- `AudioManager` setzt `ProcessMode = Always`, Musik läuft im Pause-Menü weiter.

## 4. Tests / README
- Neuer Test: `$G --headless --path . res://tests/AudioTest.tscn` → letzte Zeile `AUDIO_TEST_PASS`, Exit 0.
  Läuft auch ohne Autoload (legt dann eine lokale `AudioManager`-Instanz an).
- Audio-Prüfung ohne Godot: `python3 tools/gen_audio.py && python3 tools/verify_audio.py --determinism`
  → `AUDIO_VERIFY_PASS`.
- Nach Neuerzeugung der WAVs einmal `$G --headless --path . --import` laufen lassen (erzeugt `*.wav.import`).

## 5. Hinweise
- Mix-Balance: alle WAVs sind auf −3 dBFS Peak normalisiert; die relative Lautstärke je Sound steht in
  `Sfx.TrimDb(name)`, Musik zusätzlich `AudioManager.MusicBaseDb = -7 dB`. Wer nachjustieren will: nur dort.
- `ui_hover` wird automatisch bei `MouseEntered` jedes aktiven `BaseButton` gespielt (−16 dB, sehr leise). Wenn
  unerwünscht: in `AudioManager.HookButton` die `MouseEntered`-Zeile entfernen (AUDIO-Datei).
- `grapple_attach` wird 0,1 s nach `GrappleStarted` gespielt (vereinfachte Variante aus §6.4) und entfällt, wenn
  `GrappleEnded` vorher kommt.
