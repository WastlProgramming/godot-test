# Installation & Starten

## Voraussetzungen

| Was | Version | Hinweis |
|---|---|---|
| Godot Engine | **4.6.x, .NET-Version** (z. B. `Godot_v4.6.3-stable_mono`) | Die Standard-Version ohne .NET kann keine C#-Projekte öffnen. |
| .NET SDK | **8 oder neuer** | Das Projekt zielt auf `net8.0` mit `RollForward=LatestMajor`, läuft also auch mit .NET 9/10. |
| Node.js | 18 oder neuer | Nur für diese Dokumentation (`website/`). |

Externe Assets brauchst du nicht: Meshes entstehen im Code, Materialien und Shader liegen als Text-Ressourcen im
Projekt, die Audiodateien erzeugt ein Python-Skript und sind bereits eingecheckt.

## Windows: im Godot-Editor

1. Godot 4.6 .NET herunterladen und starten.
2. Im Projektmanager **Importieren** wählen und die Datei `project.godot` im Projektordner auswählen.
3. Beim ersten Öffnen importiert Godot alle Ressourcen. Danach oben rechts auf **Build** klicken
   (alternativ `dotnet build` im Projektordner).
4. **F5** startet das Spiel mit dem Hauptmenü (`res://scenes/ui/MainMenu.tscn`).
   **F6** startet die gerade geöffnete Szene, z. B. `scenes/Main.tscn` direkt ohne Menü.

Unter Windows nutzt Godot den Renderer **Forward+**. Alle Effekte sind so gebaut, dass sie auch im
Compatibility-Renderer (OpenGL 3) funktionieren, siehe [Grafik](../technik/grafik).

## Linux / WSL: `run-linux.sh`

Im Projekt liegt eine Godot-.NET-Binary unter `.tools/godot/` (nicht versioniert). Das Skript `run-linux.sh`
startet das Spiel damit:

```bash
./run-linux.sh                # Hauptmenü
./run-linux.sh res://scenes/Main.tscn   # direkt ins Spiel
```

Das Skript macht im Kern Folgendes:

```bash
export GALLIUM_DRIVER=d3d12
exec .tools/godot/Godot_v4.6.3-stable_mono_linux.x86_64 --path . \
     --display-driver x11 --rendering-driver opengl3 "$@"
```

::: tip GPU-Hinweis für WSL
`GALLIUM_DRIVER=d3d12` lässt Mesa über **D3D12** auf der Windows-Grafikkarte rendern statt in Software (llvmpipe).
Ohne diese Variable läuft das Spiel unter WSL meist nur mit wenigen Bildern pro Sekunde.
Gerendert wird mit **OpenGL 3 (Compatibility)** über X11 (WSLg). Wenn du statt der Grafikkarte llvmpipe siehst,
prüfe mit `glxinfo -B | grep renderer`, ob dort `D3D12` steht, und aktualisiere ggf. den GPU-Treiber unter Windows.
:::

Fehlt `.tools/godot/`, lade die Linux-Version **Godot 4.6.3 .NET** herunter, entpacke sie nach `.tools/godot/` oder
passe den Pfad in `run-linux.sh` an.

## Hauptmenü

- **Starten** beginnt einen Lauf.
- **Seed** (optional, nur Ziffern, max. 9 Stellen): Gleicher Seed ergibt dieselbe Strecke. Bleibt das Feld leer,
  wird ein zufälliger Seed gewählt.
- **Charakter: &lt;Name&gt;** öffnet die [Charakterauswahl](../spiel/charaktere).
- Der Highscore wird angezeigt und unter `user://highscore.json` gespeichert, Einstellungen unter
  `user://settings.json`.

## Direkt mit einem Charakter starten

Für Tests und Aufnahmen lässt sich der Charakter per Kommandozeile setzen, ohne die Einstellungen zu ändern:

```bash
$G --path . res://scenes/Main.tscn -- --character=knight   # bottle | trashcan | superhero | knight
$G --path . res://scenes/Main.tscn -- --quality=low        # Grafikstufe nur für diesen Start (low | high)
```

Beide Schalter gelten nur für diesen Start und werden nicht in `user://settings.json` geschrieben.

`$G` steht dabei für den Pfad zur Godot-.NET-Binary.
