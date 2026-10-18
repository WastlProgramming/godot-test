# Steuerung

## Tastatur & Maus

| Aktion | Taste |
|---|---|
| Laufen | <kbd>W</kbd> <kbd>A</kbd> <kbd>S</kbd> <kbd>D</kbd> |
| Sprinten | <kbd>Shift</kbd> halten |
| Springen | <kbd>Leertaste</kbd> – länger halten = höher |
| Kamera drehen | Maus |
| Kiste zerschlagen | linke Maustaste oder <kbd>F</kbd> |
| Brücke einsetzen | <kbd>1</kbd> |
| Trampolin einsetzen | <kbd>2</kbd> |
| Enterhaken einsetzen | <kbd>3</kbd> (während der Haken zieht: lösen) |
| Helikopter einsetzen | <kbd>4</kbd> (während des Flugs: Flug beenden) |
| Item wählen | Mausrad |
| Gewähltes Item benutzen | <kbd>E</kbd> oder rechte Maustaste |
| Pause-Menü | <kbd>Esc</kbd> |

Die Tasten <kbd>1</kbd> bis <kbd>4</kbd> wählen das Item **und** setzen es sofort ein. Fehlt ein Item oder ist der
Einsatz gerade nicht möglich, erscheint ein kurzer Hinweis im HUD (z. B. „Nur am Boden möglich“ oder
„Kein Anker in Reichweite“).

## Besondere Belegungen

- **Leertaste beim Enterhaken:** löst den Haken (du behältst den Schwung).
- **Leertaste im Helikopter:** gedrückt halten = steigen, loslassen = langsam sinken.
- **Coyote-Time und Sprungpuffer:** Du kannst bis 0,12 s nach dem Verlassen einer Kante noch springen, und ein bis
  0,12 s vor der Landung gedrückter Sprung wird ausgeführt.

## Pause-Menü

Weiter, Neustart, Hauptmenü und Beenden. Außerdem stellst du hier **Mausempfindlichkeit**, **Musik-** und
**Effektlautstärke** ein, und mit **„Grafik: niedrig (schneller)“** schaltest du SSAO ab und vereinfachst die
Schatten (siehe [Grafik](../technik/grafik#grafikqualitat)). Die Werte werden in `user://settings.json` gespeichert.
Die Musik läuft im Pause-Menü weiter, das Rotorgeräusch des Helikopters pausiert mit dem Spiel.

## Charakterauswahl

| Aktion | Taste |
|---|---|
| Vorheriger / nächster Charakter | <kbd>A</kbd> / <kbd>←</kbd> bzw. <kbd>D</kbd> / <kbd>→</kbd> |
| Auswählen (und speichern) | <kbd>Enter</kbd> |
| Zurück ohne Speichern | <kbd>Esc</kbd> |

## HUD

Oben siehst du Strecke und Punkte. Unten liegen vier Item-Slots (`[1] Brücke`, `[2] Trampolin`, `[3] Enterhaken`,
`[4] Helikopter`) mit Zählern im Format `x12` – ohne Obergrenze. Während eines Helikopterflugs erscheint eine
**Tankanzeige**.
