# Buchungsszenarien

Die folgenden Szenarien beschreiben den fachlichen Sollablauf. Konkrete Oxaion-Programme, Endpunkte, Parameter und Buchungsschluessel bleiben bis zur Bestaetigung `TODO`.

## Szenario A: Pulver nachfuellen auf vorgesehener Maschine

- **Trigger:** Bediener scannt den Fertigungsauftrags-QR-Code und bestaetigt die vorgeschlagene Maschine.
- **Pruefungen:** QR-Struktur und Pflichtwerte validieren; Maschinenbestand eindeutig ermitteln; Pulverartikel und Mix-Charge auf Kompatibilitaet pruefen; jede Nachfuellquelle aus aktuellem positivem Oxaion-Bestand waehlen und Menge gegen verfuegbaren Bestand pruefen.
- **Bedieneranzeige:** Fertigungsauftrag, Rohmaterial, vorgesehene und tatsaechliche Maschine sowie ermittelter Bestand; fuer jede Nachfuellcharge Oxaion-Lagerort, interner Lagerplatzschluessel soweit vorhanden, Charge, verfuegbarer Bestand und Einfuellmenge; anschliessend Bestaetigung.
- **Backend-Aktion:** Transaktions-ID erzeugen, Eingaben und Bestand protokollieren, Idempotenz sicherstellen; unmittelbar vor dem ersten schreibenden Oxaion-Aufruf Maschinenbestand und alle Nachfuellquellen erneut validieren.
- **Oxaion-Aktion:** Maschinenbestand lesen; Nachfuellquellen ueber die bestaetigten Auskuenfte `LB30340R`/`LB30430R` beziehungsweise bei `LAG1626` den bestaetigten `LB30230R`-Lagerortbestand lesen; danach den bestaetigten Mix-/Chargenumbuchungsablauf ausfuehren.
- **Ergebnisstatus:** `SUCCESS` bei bestaetigter und vollstaendig verifizierter Buchung; sonst passender Fehlerstatus.
- **Fehlerbehandlung:** Sperre, fachliche Ablehnung, Quellenbestandskonflikt und unklarer Verbindungsabbruch werden getrennt behandelt; kein blinder Retry.

## Szenario B: Pulver nachfuellen auf kurzfristig geaenderter Maschine

- **Trigger:** Bediener scannt den Fertigungsauftrag, waehlt eine andere Maschine und scannt diese verpflichtend.
- **Pruefungen:** Alle Pruefungen aus Szenario A; zusaetzlich abweichende Maschine eindeutig erfassen und deren Bestand pruefen.
- **Bedieneranzeige:** Planmaschine und tatsaechlich verwendete Maschine klar getrennt; Hinweis auf die Abweichung und Bestaetigung.
- **Backend-Aktion:** Plan- und Ist-Maschine unveraenderbar dem Vorgang zuordnen; keine organisatorische Freigabeentscheidung simulieren.
- **Oxaion-Aktion:** Bestand und Buchung beziehen sich auf die tatsaechlich verwendete Maschine. `TODO`: Schnittstelle fuer die spaetere FA-Materialrueckmeldung bestaetigen.
- **Ergebnisstatus:** `SUCCESS` bei bestaetigter Buchung auf die Ist-Maschine.
- **Fehlerbehandlung:** Ohne erfolgreichen Maschinenscan stoppen; inkompatibler Bestand fuehrt in Szenario E.

## Szenario C: Gewaehlte Maschine ist leer

- **Trigger:** Bestandsabfrage nach Auswahl beziehungsweise Scan der tatsaechlichen Maschine liefert eindeutig keinen Pulverbestand.
- **Pruefungen:** Sicherstellen, dass das Ergebnis eindeutig ist und kein Abfragefehler als Leerbestand interpretiert wird.
- **Bedieneranzeige:** `Maschine leer - Nachfuellen zulaessig` und die Daten der geplanten Befuellung.
- **Backend-Aktion:** Ergebnis protokollieren und nach Bedienerbestaetigung den Nachfuellvorgang starten.
- **Oxaion-Aktion:** Bestaetigten Bestand liefern und anschliessend die freigegebene Befuellungsbuchung ausfuehren. `TODO`: konkreten Leer-Maschinen-Ablauf fuer alle Faelle klaeren.
- **Ergebnisstatus:** Nach Bestandspruefung weiter in `VALIDATING`; abschliessend `SUCCESS` oder spezifischer Fehlerstatus.
- **Fehlerbehandlung:** Nicht eindeutige oder widerspruechliche Antwort wird als Szenario K behandelt.

## Szenario D: Gewaehlte Maschine enthaelt bereits passendes Pulver

- **Trigger:** Bestandsabfrage liefert einen eindeutigen Pulverartikel mit kompatibler Mix-Charge.
- **Pruefungen:** Artikel, Charge, Mix-Charge und zulaessige Kompatibilitaet pruefen; Nachfuellquellen muessen als aktuelle Oxaion-Bestandspositionen gewaehlt sein. Lagerort/Lagerplatz/Charge duerfen nicht als frei nachgebildete Buchungsschluessel verwendet werden.
- **Bedieneranzeige:** Vorhandenes Pulver und Systemmenge sowie `Passendes Pulver - Nachfuellen zulaessig`; fuer jede neue Quelle nur die von Oxaion gelieferten Auswahlwerte und die editierbare Einfuellmenge.
- **Backend-Aktion:** Pruefergebnis und Quelldaten protokollieren; direkt vor der Buchung aktuellen Tankbestand und jede ausgewaehlte Quellenposition erneut lesen.
- **Oxaion-Aktion:** Bestand lesen und die bestaetigte Nachfuell-/Mixbuchung ausfuehren.
- **Ergebnisstatus:** `SUCCESS` bei vollstaendig bestaetigter LM/LN-Verifikation.
- **Fehlerbehandlung:** Nicht bestaetigte Kompatibilitaet gilt nicht als passend; geaenderte oder unzureichende Nachfuellquelle fuehrt ohne Materialbuchung in Szenario O.

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
- **Oxaion-Aktion:** Keine erfolgreiche Buchung; liefert fachlichen Fehlercode beziehungsweise Meldung.
- **Ergebnisstatus:** `REJECTED`.
- **Fehlerbehandlung:** Kein automatischer Retry ohne fachliche Korrektur; nach Korrektur bewusster neuer Versuch mit nachvollziehbarer Zuordnung.

## Szenario I: Netzwerk-/HTTP-Abbruch vor Versand Backend -> Oxaion

- **Trigger:** Das Backend kann technisch zweifelsfrei nachweisen, dass der Buchungsauftrag Oxaion nicht erreicht hat.
- **Pruefungen:** Versandphase und Telemetrie muessen den Nichtversand eindeutig belegen.
- **Bedieneranzeige:** `Anfrage wurde nicht an Oxaion gesendet` mit Massnahme zum bewussten erneuten Versuch.
- **Backend-Aktion:** Fehler vor Versand protokollieren; derselbe fachliche Vorgang darf kontrolliert und idempotent erneut angestossen werden.
- **Oxaion-Aktion:** Keine, da Oxaion den Auftrag nachweislich nicht erhalten hat.
- **Ergebnisstatus:** Technischer Fehler vor Versand; genauer Statusname wird bei Implementierung festgelegt.
- **Fehlerbehandlung:** Automatischer Retry ist nur bei diesem zweifelsfreien Nichtversand und nach definierter Retry-Regel zulaessig.

## Szenario J: Netzwerk-/HTTP-Abbruch mit unklarem Buchungsergebnis Backend -> Oxaion

- **Trigger:** Verbindung bricht waehrend oder nach dem Versand ab; Oxaion koennte die Anfrage verarbeitet haben.
- **Pruefungen:** Transaktions-ID, Versandzeitpunkt und vorhandene Oxaion-Referenzen auswerten; Ergebnis nach Moeglichkeit ueber eine bestaetigte Statusabfrage klaeren.
- **Bedieneranzeige:** `Buchungsergebnis unklar - nicht erneut buchen` mit Transaktions-ID und Eskalationsmassnahme.
- **Backend-Aktion:** Status `UNCERTAIN` setzen, weitere automatische Buchungsversuche blockieren und manuelle Pruefung vorbereiten.
- **Oxaion-Aktion:** Moeglicherweise erfolgt; fuer den getesteten Mix-Beleg existiert eine lesende Ergebnispruefung, fuer andere Buchungsarten weiterhin `TODO`.
- **Ergebnisstatus:** `UNCERTAIN`, spaeter gegebenenfalls `SUCCESS`, `REJECTED` oder `MANUAL_REVIEW_REQUIRED` nach Klaerung.
- **Fehlerbehandlung:** Kein automatischer Retry; Abgleich mit Oxaion und dokumentierte manuelle Entscheidung.

## Szenario K: Unerwarteter oder nicht eindeutiger Maschinenbestand

- **Trigger:** Kein plausibler Bestand trotz physischem Pulver, mehrere unerwartete Bestaende oder nicht eindeutig zuordenbare Chargen.
- **Pruefungen:** Antwortvollstaendigkeit, Eindeutigkeit und Zuordnung pruefen; keine Auswahl oder Menge erraten.
- **Bedieneranzeige:** `Maschinenbestand nicht eindeutig. Vorgang wurde gestoppt.` Dazu gefundene, unkritische Kontextdaten und konkrete Massnahme zur Klaerung.
- **Backend-Aktion:** Vorgang ohne Buchung stoppen, Rohantwort datenschutzgerecht protokollieren und manuelle Pruefung markieren.
- **Oxaion-Aktion:** Nur Bestandsabfrage; keine Entnahme-, Ruecklagerungs- oder Befuellungsbuchung.
- **Ergebnisstatus:** `MANUAL_REVIEW_REQUIRED` oder ein eindeutiger Validierungsstatus.
- **Fehlerbehandlung:** Bestand in Oxaion und physische Situation klaeren; danach bewussten neuen Vorgang starten oder den bestehenden nach definierter Regel fortsetzen.

## Szenario L: Smartphone verliert Verbindung zum Backend / Offline-Erfassung

- **Trigger:** Die PWA ist geoeffnet oder wird aus dem lokalen App-Cache gestartet, das Backend ist aber nicht erreichbar.
- **Pruefungen:** Backend-Erreichbarkeit ueber einen echten Connectivity-/Health-Aufruf pruefen; `navigator.onLine` allein reicht nicht. Fuer fachliche Entscheidungen nur einen zuvor eindeutig bestaetigten lokalen Maschinenzustand verwenden, der alle benoetigten Felder und einen gueltigen Zeitstempel besitzt.
- **Bedieneranzeige:** Deutlicher Offline-Status. Ein lokal gespeicherter Vorgang wird als `Offline erfasst - noch nicht serverseitig bestaetigt` angezeigt und niemals als erfolgreich gebucht.
- **Frontend-Aktion:** Beim lokalen Anlegen eine eindeutige `clientOperationId` erzeugen; Scans und erlaubte Prozessdaten in `IndexedDB` speichern; vollstaendige synchronisierbare Vorgaenge in die Outbox mit `PENDING_SYNC` stellen.
- **Backend-Aktion:** Keine, solange das Backend nicht erreichbar ist.
- **Oxaion-Aktion:** Keine produktive Buchung durch die PWA im Offline-Zustand.
- **Ergebnisstatus:** Lokaler Sync-Status `LOCAL_DRAFT` oder `PENDING_SYNC`; kein serverseitiges `SUCCESS`.
- **Fehlerbehandlung:** Ist der lokale Maschinenzustand nicht eindeutig oder aelter als die noch festzulegende maximale Gueligkeitsdauer, keine sichere Freigabe vortaeuschen. Die Auswahl produktiver Nachfuellquellen wird vor dem Buchen online erneut aus Oxaion validiert.
- **Offen:** Welche konkreten Schritte je Prozess offline bis `PENDING_SYNC` vorbereitet werden duerfen und wie lange ein Maschinenzustand als gueltig gilt, steht in `docs/OPEN_POINTS.md`.

## Szenario M: Verbindung wiederhergestellt / Outbox-Synchronisation

- **Trigger:** Das Backend ist nach einer Offline-Phase wieder erreichbar oder die App wird mit offenen `PENDING_SYNC`-Vorgaengen gestartet.
- **Pruefungen:** Jeden Outbox-Eintrag mit unveraenderter `clientOperationId` uebertragen; serverseitig Idempotenz pruefen; aktuellen Maschinenzustand und alle Nachfuellquellen erneut aus der fuehrenden Quelle validieren.
- **Bedieneranzeige:** `Synchronisation laeuft` und anschliessend getrennte Anzeige von lokalem Sync-Status und serverseitigem Buchungsstatus.
- **Backend-Aktion:** Eine bereits bekannte `clientOperationId` dem bestehenden Vorgang zuordnen und keine zweite wirksame Buchung erzeugen. Unbekannte ID genau einmal als neuen serverseitigen Vorgang anlegen. Vor produktiver Oxaion-Buchung aktuelle fachliche Daten erneut pruefen.
- **Oxaion-Aktion:** Nur nach erfolgreicher serverseitiger Revalidierung und nach den normalen Buchungsregeln.
- **Ergebnisstatus:** Lokal `SYNCED`, wenn die Zuordnung zum Backend eindeutig ist; serverseitig separat zum Beispiel `CREATED`, `VALIDATING`, `SUCCESS`, `REJECTED`, `LOCKED`, `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED`.
- **Fehlerbehandlung:** Hat sich der Zustand seit der Offline-Erfassung geaendert, keine automatische Ueberschreibung oder Blindbuchung. Lokaler Status `CONFLICT` beziehungsweise serverseitig `MANUAL_REVIEW_REQUIRED` und konkrete Bedienermassnahme.

## Szenario N: PWA-Update waehrend laufendem oder ungesynctem Vorgang

- **Trigger:** Eine neue Frontend-/Service-Worker-Version ist verfuegbar, waehrend ein kritischer Vorgang, eine Synchronisation oder noch nicht sicher migrationsfaehige `PENDING_SYNC`-Daten vorhanden sind.
- **Pruefungen:** Aktiven Prozess, ungespeicherte Daten, Outbox und laufende Synchronisation pruefen.
- **Bedieneranzeige:** `Neue Version verfuegbar. Aktualisierung erfolgt, sobald der aktuelle Vorgang sicher abgeschlossen ist.`
- **Frontend-Aktion:** Neue Version darf vorgeladen werden, aber kein erzwungener Reload und keine Aktivierung, die Datenverlust verursachen kann. Service-Worker-Cache und IndexedDB getrennt behandeln.
- **Backend-Aktion:** Keine fachliche Sonderbuchung. API-/Frontend-Kompatibilitaet muss bei der technischen Umsetzung versioniert werden.
- **Oxaion-Aktion:** Keine.
- **Ergebnisstatus:** Aktueller Vorgang behaelt seinen Zustand; Update wird erst in einem sicheren Zustand aktiviert.
- **Fehlerbehandlung:** IndexedDB-Schemamigrationen muessen `PENDING_SYNC`-Daten erhalten. Kann Kompatibilitaet nicht garantiert werden, Update nicht mitten im Vorgang erzwingen und klare administrative Meldung ausgeben.

## Szenario O: Nachfuellquelle geaendert oder nicht mehr gueltig

- **Trigger:** Eine zuvor ausgewaehlte Oxaion-Bestandsposition ist vor der Buchung nicht mehr vorhanden, nicht mehr eindeutig, hat weniger Bestand als angefordert oder stimmt bei Lagerort/Lagerplatz/Charge nicht mehr mit dem vorbereiteten Vorgang ueberein.
- **Pruefungen:** Exakte Kombination aus Artikel, Lagerort, internem Lagerplatzschluessel, Charge und verfuegbarer Menge erneut lesen. Dieselbe exakte Quellenposition darf in einem Vorgang nicht doppelt vorkommen.
- **Bedieneranzeige:** `Nachfuellbestand geaendert - nichts gebucht` mit aktueller unkritischer Bestandsinformation und Aufforderung, die Quelle neu aus Oxaion auszuwaehlen.
- **Backend-Aktion:** Status/Antwort `CONFLICT` in Stage `SOURCE_STOCK_VALIDATION`; keine `LB20100J`-/`LB20115J`-Buchungsfolge starten.
- **Oxaion-Aktion:** Ausschliesslich lesende Bestandsabfragen `LB30340R`, `LB30430R` beziehungsweise bei bestaetigtem `LAG1626` `LB30230R`; keine Materialbuchung.
- **Ergebnisstatus:** Sicherer Vor-Buchungs-Konflikt, kein unklarer ERP-Ausgang.
- **Fehlerbehandlung:** Aktuelle Quelle erneut aus Oxaion auswaehlen und danach einen normalen Buchungsversuch mit den aktualisierten Daten starten. Kein blindes Weitersenden der alten Lagerplatzdarstellung.


## Szenario P: Tank-Auslagern mit Wiegeabweichung

- **Trigger:** Beim Vorgang `Pulver aus Tank auslagern` weicht die auf 0,001 kg normalisierte gewogene Netto-Pulvermenge `Qphys` vom unmittelbar zuvor bestaetigten Oxaion-Tankbestand `Qsys` ab.
- **Pruefungen:** Mitarbeiter, Tank, Artikel, Mix-Charge und `Qsys` erneut online bestaetigen. Differenz ausschliesslich als `abs(Qphys-Qsys)` bestimmen.
- **Backend-Aktion bei Minderbestand:** Eigene idempotente Teiltransaktion mit `I2 = Bestandskorr. Abgang (Schwund)` auf exakt Tank/Artikel/Mix-Charge. Die App setzt die FAM-Kontierung explizit in der Position: Geschaeftsbereich `PSWERK=21` und Kostenstelle `PSKSTL=6000`; eine leere `LBSKSB`-Vorbelegung im Buchungsschluessel ist zulaessig.
- **Oxaion-Bewegungsrichtung:** Die Korrekturposition setzt den vom Lagerdialog verwendeten Richtungszustand explizit: `TX_B1SB01=2` fuer I2/Abgang und `TX_B1SB01=1` fuer I1/Zugang. Dieser Zustand ist getrennt von GB (`PSWERK`) und Kostenstelle (`PSKSTL`).
- **Backend-Aktion bei Mehrbestand:** Eigene idempotente Teiltransaktion mit `I1 = Bestandskorrektur Zugang` auf exakt Tank/Artikel/Mix-Charge. Im aktuellen FAM-STAGING-Referenzablauf Kostenstelle 5100.
- **Oxaion-Aktion:** Lagerbeleg `LB20100J`; Korrekturposition `LB20115J`, bestaetigter `TCODE=WIN2`-/`LOADWIN2`-Ablauf; Persistierung `LB20110R *UPD`; anschliessend Beleg schliessen und exakt verifizieren.
- **Fortsetzung:** Erst nach eindeutig erfolgreicher Korrektur und erneut gelesenem Tankbestand exakt `Qphys` wird die bewaehrte `LF/LE`-Umlagerung ueber `Qphys` gestartet.
- **Ergebnisstatus:** Gesamtvorgang `SUCCESS` nur, wenn gegebenenfalls I1/I2 sowie LF/LE jeweils eindeutig verifiziert sind.
- **Fehlerbehandlung:** Unklarer I1/I2-Ausgang blockiert LF/LE. Eine bereits bestaetigte Korrektur wird bei spaeter unklarem LF/LE-Ausgang nicht automatisch rueckgaengig gemacht oder erneut gebucht.

## Szenario Q: Korrekturbuchung Fertigungsauftrag nach Jobabbruch

- **Trigger:** Pulver wurde bereits beim Druckstart auf den Fertigungsauftrag gebucht; der Druckjob wurde abgebrochen und der gewogene tatsaechliche Ist-Verbrauch ist kleiner als der urspruenglich gebuchte Verbrauch.
- **Pruefungen vor Storno:** Mitarbeiter, Tank/Mix, FA, Materialposition, `AMMATV` und `AMMPST` erneut online bestaetigen. Der aktuell bestaetigte Stornoablauf ist fuer die komplett abgebuchte Materialposition `AMMPST=9` mit positivem Verbrauch freigegeben.
- **Originalrueckmeldung:** `PW22000J *LOADNEW` mit `STORNO=J` und danach `PW22000J *STON`; die Oxaion-Standard-Stornoliste `PW22021R` lesen. Sie gilt als bereits auf gueltige/stornierbare Rueckmeldungen gefiltert. Fuer einen automatischen Storno muss genau eine Rueckmeldung auf FA, Materialposition, Artikel, urspruengliche Menge, **Tanklager und Mix-Charge** passen. Bereits beim FA-Scan wird diese Quelle gegen den zuvor gescannten Tank geprueft. Ist die Tankcharge inzwischen eine andere, wird der Vorgang vor der Mengeneingabe gesperrt; es erfolgt kein Storno. Der Bediener muss den Fall in Oxaion pruefen und gegebenenfalls manuell ueber Lagerbelege korrigieren.
- **Storno:** `PW22021R *STORNO` mit exakter Rueckmeldenummer, Rueckmeldedatum und Rueckmeldeuhrzeit aus diesem Listeneintrag.
- **Storno-Verifikation:** Eine HTTP-Antwort oder die im Referenzfall nur aus der XML-Deklaration bestehende Antwort ist kein Erfolgsbeweis. Vor dem zweiten Schreibschritt muessen (a) der exakte Rueckmeldeschluessel aus der Liste verschwunden sein, (b) die FA-Materialposition `AMMATV=0 / AMMPST=0` zeigen und (c) dieselbe Tank-Mix-Charge exakt um die urspruengliche Verbrauchsmenge erhoeht sein.
- **Neue Rueckmeldung:** Nur nach dieser dreifachen Bestaetigung wird der gewogene korrigierte Ist-Verbrauch mit der bestaetigten normalen MK-Logik neu gebucht. Bei `0,000 kg` ist keine neue MK erforderlich.
- **Ergebnisstatus:** `SUCCESS` erst nach finaler exakter Verifikation von FA-Materialposition und Tank/Mix.
- **Fehlerbehandlung:** Bei unklarem Storno kein Blind-Retry und keine neue MK. Ist der Storno sicher erfolgreich, aber die neue MK unklar oder fehlgeschlagen, bleibt dieser Zwischenzustand sichtbar; der Gesamtvorgang darf nicht erneut von vorne gestartet werden.
