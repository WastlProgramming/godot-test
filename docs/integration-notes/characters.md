# Integration-Notes – CHARACTERS

Stand: alle CHARACTERS-Aufgaben (TASKS_V2 C1–C6) umgesetzt; `dotnet build` 0 Fehler; alle `tests/*.tscn` (außer dem
nicht ausgeführten LongRun) Exit 0, inkl. `CharacterTest`, `FlowTest`, `TesterSecurityTest`.

## Umgesetzt (eigene Dateien)
- `scripts/Characters/`: `CharacterDefinition.cs` (+ `CharacterStyle`), `CharacterCatalog.cs`, `CharacterModelFactory.cs`,
  `CharacterRig.cs`, `CharacterMaterials.cs`.
- `art/characters/*.tres`: 13 StandardMaterial3D (`char_eye_white`, `char_eye_dark`, `char_limb`, `char_gold`, `bottle_glass`,
  `bottle_label`, `trash_can`, `trash_lid`, `hero_suit`, `hero_skin`, `knight_steel`, `knight_tabard`, `knight_eye_glow`).
  Fehlt eine Datei, baut `CharacterMaterials.Create` dasselbe Material im Code (keine statischen Caches).
- `scenes/Player.tscn`: `Visual/Body` und `Visual/Visor` entfernt, `Visual/Rig` (CharacterRig) neu, `Camera3D.far = 1500`.
  Alle anderen Knoten unverändert.
- `scenes/ui/CharacterSelect.tscn` + `scripts/UI/CharacterSelect.cs`, `scenes/ui/MainMenu.tscn` (`%CharacterButton`,
  Footer-Text mit Helikopter) + `scripts/UI/MainMenu.cs` (Button, Laufzeit-Icon `art/ui/icons/helicopter.svg`, falls vorhanden).
- `art/ui/theme.tres`: neu, nur additiv: `CharacterCard`, `CharacterCardSelected` (Button) und `CharacterPreviewFrame` (PanelContainer).
- `scripts/Game/GameManager.cs`: `CharacterSelectScenePath`, `SelectedCharacter`, `MusicVolume`, `SfxVolume`, Signal
  `CharacterChanged(string id)`, `item_4` in `EnsureInputMap`. Die Werte werden in `_Ready` aus den Settings geladen und in
  `SaveAll` gespeichert.
- `scripts/Game/SaveSystem.cs`: `SettingsData.Character/MusicVolume/SfxVolume`, JSON `character`/`music_volume`/`sfx_volume`
  (`version` bleibt 1), `ClampVolume`, `IsAcceptableCharacterId`, `SerializeSettings` (intern, für den Roundtrip-Test).
- `tests/CharacterTest.tscn/.cs` → `CHARACTER_TEST_PASS`.

Messwerte (CharacterTest):

| Id | Meshes | Dreiecke | Materialien | Höhe (m) | ModelTopHeight |
|---|---|---|---|---|---|
| bottle | 19 | 2144 | 6 | 1.74 | 1.74 |
| trashcan | 20 | 2280 | 6 | 1.72 | 1.72 |
| superhero | 18 | 2076 | 5 | 1.87 | 1.87 |
| knight | 24 | 2396 | 5 | 1.96 | 1.96 |

`CharacterRig._Process` erzeugt keine verwalteten Allokationen; der Test misst das mit `GC.GetAllocatedBytesForCurrentThread`.

## Für den INTEGRATOR
1. **Lautstärken (CONTRACT_V2 §10.3):** Beim Start `AudioManager.Instance.MusicVolume = GameManager.MusicVolume` bzw.
   `SfxVolume` setzen. Die Pause-Menü-Slider `%MusicSlider`/`%SfxSlider` schreiben auf `GameManager.MusicVolume/SfxVolume`
   (der Setter klemmt auf 0..1 und markiert die Settings als dirty) und zusätzlich auf den AudioManager.
   `GameManager` selbst kennt den AudioManager nicht (Kompilier-Entkopplung).
2. **PlayerController (GAMEPLAY, optional aufräumen):** `_Ready` sucht weiterhin `Visual/Body` und `Visual/Visor` (null-sicher,
   es passiert also nichts). Diese Zeilen und `MaterialNames.PlayerBody/PlayerAccent` können entfallen.
3. **Rotor-Höhe:** `CharacterRig` setzt `IPlayerAnimState.ModelTopHeight` in seinem `_Ready`, also *vor*
   `PlayerController._Ready` (Kinder werden zuerst bereit). Der Rotor sitzt damit schon beim ersten Frame richtig.
4. **`art/STYLE.md`** um einen Abschnitt „Charaktere“ ergänzen:
   - Flasche: Aqua-PET `#8FE6DE` mit Rim, Etikett Koralle `#FF7AA2`, Deckel und Schuhe Gold `#E8A317`.
   - Mülleimer: Grün `#45A866`, Deckel/Rillen `#2C7A47`, Banane Gold.
   - Superheld: Anzug `#3D5AFE` (wie der v1-Spieler), Umhang/Gürtel/Emblem/Stiefel Gold, Maske Navy `#161A2B`.
   - Ritter: Stahl `#C9D1DC` (metallisch), Wappenrock/Schild `#2F6FA8`, Federbusch/Parierstange Gold, Augen leuchten `#7FE6FF`.
   - Rot, Orange und Magenta werden bewusst nicht verwendet.
5. **Docs (`website/spiel/charaktere.md`):** Die Inhalte stehen oben bzw. in `CharacterCatalog` (Namen und Beschreibungen).
   Steuerung im Auswahl-Screen: A/← und D/→ wechseln, Enter wählt aus (speichert), Esc geht ohne Speichern zurück.
   Im Hauptmenü öffnet der Button „Charakter: <Name>“ den Auswahl-Screen.
6. `project.godot` braucht von CHARACTERS keine Änderung. `item_4` legt `GameManager.EnsureInputMap` als Fallback an;
   der Eintrag in `project.godot` bleibt laut §10.1 Aufgabe des INTEGRATOR.
