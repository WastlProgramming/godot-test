# Charaktere

Vier Charaktere stehen zur Wahl. Sie sind **rein kosmetisch**: Kollision (Kapsel mit 0,4 m Radius, 1,8 m Höhe),
Geschwindigkeit und Sprungkraft sind für alle gleich. Alle Modelle sind prozedural aus Grundformen gebaut und haben
animierte Arme und Beine.

| Charakter | Id | Höhe | Beschreibung im Spiel |
|---|---|---|---|
| **Flasche** | `bottle` | 1,74 m | Eine flinke PET-Flasche mit Schraubdeckel. Leicht, spritzig und immer gut gelaunt. |
| **Mülleimer** | `trashcan` | 1,72 m | Die rostfreie Tonne lüftet beim Springen den Deckel – und schaut neugierig durch den Spalt. |
| **Superheld** (Standard) | `superhero` | 1,87 m | Maske auf, Umhang im Wind: Der Superheld fliegt am liebsten mit dem Arm voraus. |
| **Ritter** | `knight` | 1,96 m | Helm mit Federbusch, Schild links, Schwert rechts – jede Kiste zittert vor ihm. |

## Aussehen

- **Flasche:** Körper aus Aqua-PET (`#8FE6DE`) mit Hals und Schraubdeckel, Etikett-Band in Koralle (`#FF7AA2`), Deckel
  und Schuhe in Gold (`#E8A317`), dünne Arme und Beine.
- **Mülleimer:** Grüne Tonne (`#45A866`) mit Rillen, Deckel (`#2C7A47`) als „Kopf“ – er klappt beim Springen auf, die
  Augen schauen durch den Spalt. Als Gag klebt eine Banane an der Tonne.
- **Superheld:** Anzug in Königsblau (`#3D5AFE`), goldener Umhang, Gürtel, Emblem und Stiefel, Maske in Navy. Der Umhang
  schwingt mit dem Tempo.
- **Ritter:** Stahlrüstung (`#C9D1DC`, metallisch), Wappenrock und Schild in Blau (`#2F6FA8`), Helm mit Visier und goldenem
  Federbusch, leuchtende Augen. Schwert rechts (schlägt mit), Schild links.

Rot, Orange und Magenta werden bei den Charakteren bewusst nicht verwendet – diese Farben sind für Hindernisse und
Fehler reserviert.

## Auswahl

1. Im Hauptmenü auf **„Charakter: &lt;Name&gt;“** klicken.
2. Mit <kbd>A</kbd>/<kbd>←</kbd> und <kbd>D</kbd>/<kbd>→</kbd> (oder den Pfeil-Buttons) blättern. Die Vorschau zeigt den
   Charakter auf einer Drehscheibe mit gelegentlichem Demo-Lauf.
3. **Auswählen** bzw. <kbd>Enter</kbd> speichert die Wahl in `user://settings.json` (Feld `character`) und kehrt ins
   Hauptmenü zurück. **Zurück** bzw. <kbd>Esc</kbd> verwirft die Änderung.

Eine unbekannte oder manipulierte Id in den Einstellungen fällt automatisch auf den Superhelden zurück.

## Animation

Die Animation ist vollständig prozedural (`CharacterRig`, kein `AnimationPlayer`). Animiert werden nur die
Rotationen von Gelenk-Pivots:

```
Rig
└── Hips
    ├── Body
    │   ├── Head
    │   ├── ArmL   (Ritter: Shield)
    │   └── ArmR   (Ritter: Sword)
    ├── LegL
    └── LegR
```

Der Rig liest jeden Frame den Zustand des Spielers über das Interface `IPlayerAnimState` und blendet weich zwischen
diesen Posen:

| Zustand | Bewegung |
|---|---|
| Idle | Leichtes Atmen |
| Laufen | Schrittzyklus mit ≈ 1,4 m Schrittlänge, Phase folgt der Geschwindigkeit, Arme gegenphasig, Amplitude wächst mit dem Tempo |
| Sprung | Arme hoch, Beine angezogen, kurzes Strecken beim Absprung |
| Fallen | Arme seitlich, leichtes Rudern |
| Helikopter | Beine baumeln, Arme greifen zum Rotor (Superheld: ein Arm nach vorn) |
| Enterhaken | Rechter Arm zeigt zum Anker |
| Schlag | Rechter Arm schwingt 0,25 s (Ritter mit Schwert) |
| Landung | Kurze Stauchung (0,22 s) |

Umhang und Deckel reagieren zusätzlich auf Tempo und Sprünge. Die Animation ist frame-rate-unabhängig und erzeugt pro
Frame keine Speicherallokationen.

Nach dem Modellbau setzt der Rig `ModelTopHeight` auf die Modellhöhe – daran montiert der `PlayerController` den
Helikopter-Rotor, sodass er bei jedem Charakter direkt über dem Kopf sitzt.

## Budget pro Charakter

| Id | Meshes | Dreiecke | Materialien |
|---|---|---|---|
| bottle | 19 | 2144 | 6 |
| trashcan | 20 | 2280 | 6 |
| superhero | 18 | 2076 | 5 |
| knight | 24 | 2396 | 5 |

Vorgabe: höchstens 24 Meshes, 4000 Dreiecke und 6 Materialien je Charakter. Gemessen wird das im `CharacterTest`.
