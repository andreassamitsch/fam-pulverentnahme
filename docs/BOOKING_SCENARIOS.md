# Buchungsszenarien

Die folgenden Szenarien beschreiben den fachlichen Sollablauf. Konkrete Oxaion-Programme, Endpunkte, Parameter und Buchungsschluessel bleiben bis zur Bestaetigung `TODO`.

## Szenario A: Pulver nachfuellen auf vorgesehener Maschine

- **Trigger:** Bediener scannt den Fertigungsauftrags-QR-Code und bestaetigt die vorgeschlagene Maschine.
- **Pruefungen:** QR-Struktur und Pflichtwerte validieren; Maschinenbestand eindeutig ermitteln; Pulverartikel und Mix-Charge auf Kompatibilitaet pruefen.
- **Bedieneranzeige:** Fertigungsauftrag, Rohmaterial, vorgesehene und tatsaechliche Maschine sowie ermittelter Bestand; anschliessend Bestaetigung.
- **Backend-Aktion:** Transaktions-ID erzeugen, Eingaben und Bestand protokollieren, Idempotenz sicherstellen und den freigegebenen Oxaion-Aufruf koordinieren.
- **Oxaion-Aktion:** Maschinenbestand lesen und nach Bestaetigung ueber die noch zu identifizierende BDE-/PPS-Fachlogik buchen. `TODO`: konkreten Aufruf bestaetigen.
- **Ergebnisstatus:** `SUCCESS` bei bestaetigter Buchung; sonst passender Fehlerstatus.
- **Fehlerbehandlung:** Sperre, fachliche Ablehnung und unklarer Verbindungsabbruch werden getrennt behandelt; kein blinder Retry.

## Szenario B: Pulver nachfuellen auf kurzfristig geaenderter Maschine

- **Trigger:** Bediener scannt den Fertigungsauftrag, waehlt eine andere Maschine und scannt diese verpflichtend.
- **Pruefungen:** Alle Pruefungen aus Szenario A; zusaetzlich abweichende Maschine eindeutig erfassen und deren Bestand pruefen.
- **Bedieneranzeige:** Planmaschine und tatsaechlich verwendete Maschine klar getrennt; Hinweis auf die Abweichung und Bestaetigung.
- **Backend-Aktion:** Plan- und Ist-Maschine unveraenderbar dem Vorgang zuordnen; keine organisatorische Freigabeentscheidung simulieren.
- **Oxaion-Aktion:** Bestand und Buchung beziehen sich auf die tatsaechlich verwendete Maschine. `TODO`: konkrete Schnittstellen und Parameter bestaetigen.
- **Ergebnisstatus:** `SUCCESS` bei bestaetigter Buchung auf die Ist-Maschine.
- **Fehlerbehandlung:** Ohne erfolgreichen Maschinenscan stoppen; inkompatibler Bestand fuehrt in Szenario E.

## Szenario C: Gewaehlte Maschine ist leer

- **Trigger:** Bestandsabfrage nach Auswahl beziehungsweise Scan der tatsaechlichen Maschine liefert eindeutig keinen Pulverbestand.
- **Pruefungen:** Sicherstellen, dass das Ergebnis eindeutig ist und kein Abfragefehler als Leerbestand interpretiert wird.
- **Bedieneranzeige:** `Maschine leer - Nachfuellen zulaessig` und die Daten der geplanten Befuellung.
- **Backend-Aktion:** Ergebnis protokollieren und nach Bedienerbestaetigung den Nachfuellvorgang starten.
- **Oxaion-Aktion:** Bestaetigten Bestand liefern und anschliessend die freigegebene Befuellungsbuchung ausfuehren. `TODO`: konkrete Logik klaeren.
- **Ergebnisstatus:** Nach Bestandspruefung weiter in `VALIDATING`; abschliessend `SUCCESS` oder spezifischer Fehlerstatus.
- **Fehlerbehandlung:** Nicht eindeutige oder widerspruechliche Antwort wird als Szenario K behandelt.

## Szenario D: Gewaehlte Maschine enthaelt bereits passendes Pulver

- **Trigger:** Bestandsabfrage liefert einen eindeutigen Pulverartikel mit kompatibler Mix-Charge.
- **Pruefungen:** Artikel, Charge, Mix-Charge und zulaessige Kompatibilitaet anhand noch festzulegender fachlicher Regeln pruefen.
- **Bedieneranzeige:** Vorhandenes Pulver und Systemmenge sowie `Passendes Pulver - Nachfuellen zulaessig`.
- **Backend-Aktion:** Pruefergebnis und Quelldaten protokollieren; erst nach Bestaetigung buchen.
- **Oxaion-Aktion:** Bestand lesen und die freigegebene Nachfuellbuchung ausfuehren. `TODO`: Kompatibilitaetsregeln und Aufruf bestaetigen.
- **Ergebnisstatus:** `SUCCESS` bei bestaetigter Buchung.
- **Fehlerbehandlung:** Nicht bestaetigte Kompatibilitaet gilt nicht als passend; der Vorgang wird gestoppt oder in Szenario E uebergeben.

## Szenario E: Gewaehlte Maschine enthaelt falsches Pulver

- **Trigger:** Eindeutiger Maschinenbestand zeigt einen anderen Pulverartikel oder eine nicht kompatible Mix-Charge.
- **Pruefungen:** Bestand und Inkompatibilitaet eindeutig belegen; niemals unterschiedliche Pulver als kompatibel annehmen.
- **Bedieneranzeige:** `Pulverwechsel erforderlich`, vorhandenes Pulver und Aktion zum Wechsel in **Pulver tauschen**.
- **Backend-Aktion:** Nachfuellvorgang ohne Buchung beenden beziehungsweise als nicht ausfuehrbar markieren; Kontext sicher an den Pulverwechselprozess uebergeben.
- **Oxaion-Aktion:** Nur Bestandsabfrage; keine Materialbuchung im Nachfuellprozess.
- **Ergebnisstatus:** Fachlich abgebrochener Nachfuellvorgang; fuer einen gestarteten Wechsel entsteht eine eigene Transaktions-ID.
- **Fehlerbehandlung:** Kein automatisches Vermischen und keine automatische Umbuchung.

## Szenario F: Pulverwechsel

- **Trigger:** Bediener startet **Pulver tauschen** oder wechselt aus Szenario E; Maschinenscan ist verpflichtend.
- **Pruefungen:** Maschine eindeutig identifizieren; genau einen plausiblen Pulverartikel, eine Mix-Charge und einen Systembestand ermitteln; neue Rohmaterialcharge validieren; alle Buchungsschritte bestaetigen.
- **Bedieneranzeige:** Maschine, vorhandener Pulverartikel, Mix-Charge und vollstaendige Systemrestmenge; Bestaetigung der Entnahme; danach Scan der neuen Rohmaterialcharge und Anzeige der neuen Mix-Charge.
- **Backend-Aktion:** Gesamtvorgang und einzelne Buchungsschritte nachvollziehbar korrelieren; vollstaendige Entnahmemenge aus dem Systembestand uebernehmen; Ruecklagerung, Mix-Chargenerzeugung und Neubefuellung kontrolliert koordinieren.
- **Oxaion-Aktion:** Bestand abrufen, vollstaendige Entnahme und Ruecklagerung abbilden, anschliessend neue Rohmaterialcharge/Mix-Charge und Maschinenbefuellung ueber freigegebene Fachlogik buchen. `TODO`: Programme, Reihenfolge, Atomaritaet und Buchungsschluessel klaeren.
- **Ergebnisstatus:** `SUCCESS` nur wenn alle fachlich erforderlichen Schritte eindeutig bestaetigt sind; Zwischen- und Teilzustaende muessen protokolliert werden.
- **Fehlerbehandlung:** Bei Teilfehler oder unklarem Ergebnis keine eigenstaendige Kompensations- oder Wiederholungsbuchung; `UNCERTAIN` beziehungsweise `MANUAL_REVIEW_REQUIRED`.

## Szenario G: Oxaion-Datensatz gesperrt

- **Trigger:** Oxaion meldet bei Pruefung oder Buchung eine tatsaechliche Datensatzsperre.
- **Pruefungen:** Sperrstatus aus der Oxaion-Sperrlogik auswerten; `zuletzt geaendert von` nicht als Sperrer verwenden.
- **Bedieneranzeige:** `Fertigungsauftrag gesperrt. Die Materialbuchung wurde nicht durchgefuehrt.` Zusaetzlich Sperrer oder `nicht ermittelbar` und konkrete Warte-/Eskalationsmassnahme.
- **Backend-Aktion:** Vorgang als `LOCKED` protokollieren, Transaktions-ID anzeigen und keine automatische Endlosschleife starten.
- **Oxaion-Aktion:** Keine Buchung; soweit zuverlaessig moeglich Sperrinformation und sperrenden Benutzer liefern. `TODO`: Sperrabfrage identifizieren.
- **Ergebnisstatus:** `LOCKED`; Buchung wurde nicht durchgefuehrt.
- **Fehlerbehandlung:** Erneuter Versuch nur bewusst/manuell; bei fortbestehender Sperre Produktionsleitung informieren.

## Szenario H: Oxaion lehnt Buchung fachlich ab

- **Trigger:** Oxaion verarbeitet die Anfrage und liefert eine eindeutige fachliche Ablehnung.
- **Pruefungen:** Antwort sicher der Transaktions-ID zuordnen und technische Fehler von fachlicher Ablehnung trennen.
- **Bedieneranzeige:** Verstaendliche, moeglichst konkrete Ablehnungsursache und erforderliche Massnahme; keine Zugangsdaten oder internen Secrets anzeigen.
- **Backend-Aktion:** Request-Referenz, bereinigte Antwort und fachlichen Fehler protokollieren.
- **Oxaion-Aktion:** Keine erfolgreiche Buchung; liefert fachlichen Fehlercode beziehungsweise Meldung gemaess der noch zu untersuchenden Schnittstelle.
- **Ergebnisstatus:** `REJECTED`.
- **Fehlerbehandlung:** Kein automatischer Retry ohne fachliche Korrektur; nach Korrektur bewusster neuer Versuch mit nachvollziehbarer Zuordnung.

## Szenario I: Netzwerk-/HTTP-Abbruch vor Versand

- **Trigger:** Das Backend kann technisch zweifelsfrei nachweisen, dass der Buchungsauftrag Oxaion nicht erreicht hat.
- **Pruefungen:** Versandphase und Telemetrie muessen den Nichtversand eindeutig belegen.
- **Bedieneranzeige:** `Anfrage wurde nicht an Oxaion gesendet` mit Massnahme zum bewussten erneuten Versuch.
- **Backend-Aktion:** Fehler vor Versand protokollieren; derselbe fachliche Vorgang darf kontrolliert und idempotent erneut angestossen werden.
- **Oxaion-Aktion:** Keine, da Oxaion den Auftrag nachweislich nicht erhalten hat.
- **Ergebnisstatus:** Technischer Fehler vor Versand; genauer Statusname wird bei Implementierung festgelegt.
- **Fehlerbehandlung:** Automatischer Retry ist nur bei diesem zweifelsfreien Nichtversand und nach definierter Retry-Regel zulaessig.

## Szenario J: Netzwerk-/HTTP-Abbruch mit unklarem Buchungsergebnis

- **Trigger:** Verbindung bricht waehrend oder nach dem Versand ab; Oxaion koennte die Anfrage verarbeitet haben.
- **Pruefungen:** Transaktions-ID, Versandzeitpunkt und vorhandene Oxaion-Referenzen auswerten; Ergebnis nach Moeglichkeit ueber eine bestaetigte Statusabfrage klaeren.
- **Bedieneranzeige:** `Buchungsergebnis unklar - nicht erneut buchen` mit Transaktions-ID und Eskalationsmassnahme.
- **Backend-Aktion:** Status `UNCERTAIN` setzen, weitere automatische Buchungsversuche blockieren und manuelle Pruefung vorbereiten.
- **Oxaion-Aktion:** Moeglicherweise erfolgt; `TODO`: belastbare Ergebnis-/Statusabfrage identifizieren.
- **Ergebnisstatus:** `UNCERTAIN`, spaeter gegebenenfalls `SUCCESS`, `REJECTED` oder `MANUAL_REVIEW_REQUIRED` nach Klaerung.
- **Fehlerbehandlung:** Kein automatischer Retry; Abgleich mit Oxaion und dokumentierte manuelle Entscheidung.

## Szenario K: Unerwarteter oder nicht eindeutiger Maschinenbestand

- **Trigger:** Kein plausibler Bestand trotz physischem Pulver, mehrere unerwartete Bestaende oder nicht eindeutig zuordenbare Chargen.
- **Pruefungen:** Antwortvollstaendigkeit, Eindeutigkeit und Zuordnung pruefen; keine Auswahl oder Menge erraten.
- **Bedieneranzeige:** `Maschinenbestand nicht eindeutig. Vorgang wurde gestoppt.` Dazu gefundene, unkritische Kontextdaten und konkrete Massnahme zur Klaerung.
- **Backend-Aktion:** Vorgang ohne Buchung stoppen, Rohantwort datenschutzgerecht protokollieren und manuelle Pruefung markieren.
- **Oxaion-Aktion:** Nur Bestandsabfrage; keine Entnahme-, Ruecklagerungs- oder Befuellungsbuchung.
- **Ergebnisstatus:** `MANUAL_REVIEW_REQUIRED` oder ein bei Implementierung festgelegter eindeutiger Validierungsstatus.
- **Fehlerbehandlung:** Bestand in Oxaion und physische Situation klaeren; danach bewussten neuen Vorgang starten oder den bestehenden nach definierter Regel fortsetzen.
