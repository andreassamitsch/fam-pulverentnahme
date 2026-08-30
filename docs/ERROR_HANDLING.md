# Fehler- und Transaktionshandling

## Ziel und Grundregel

Jeder produktive Buchungsvorgang muss eindeutig, nachvollziehbar und gegen Doppelbuchungen geschuetzt sein.

Automatischer Retry ist nur zulaessig, wenn technisch zweifelsfrei feststeht, dass Oxaion den urspruenglichen Buchungsauftrag **nicht** verarbeitet hat. In allen anderen unklaren Faellen gilt `UNCERTAIN` beziehungsweise `MANUAL_REVIEW_REQUIRED`.

## Transaktions-ID

- Das Backend erzeugt vor jeder produktiven Buchung eine global eindeutige, unveraenderliche Transaktions-/Vorgangs-ID.
- Die ID korreliert Bedienvorgang, Backend-Protokoll, einzelne Buchungsschritte und Oxaion-Referenzen.
- Die ID wird dem Bediener bei Fehlern und unklaren Ergebnissen angezeigt.
- Ein Pulverwechsel mit mehreren Schritten benoetigt eine nachvollziehbare Korrelation zwischen Gesamtvorgang und Teilschritten.
- `TODO`: Klaeren, ob und wie die Vorgangs-ID in den bestaetigten Oxaion-Aufrufen als externe Referenz uebergeben und spaeter abgefragt werden kann.

## Idempotency und Duplicate Prevention

- Derselbe fachliche Auftrag darf nicht mehrfach wirksam gebucht werden.
- Das Backend prueft vor dem Versand, ob fuer die Vorgangs-ID bereits ein laufender, erfolgreicher oder unklarer Versand existiert.
- Parallel eintreffende identische Anforderungen muessen serialisiert oder durch eine eindeutige Persistenzregel abgefangen werden.
- Eine bereits erfolgreiche Vorgangs-ID liefert das gespeicherte Ergebnis und loest keine neue Buchung aus.
- Eine Vorgangs-ID im Status `SENDING_TO_OXAION`, `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED` darf keine weitere Buchung ausloesen.
- Clientseitige Button-Sperren verbessern die Bedienung, ersetzen aber niemals die serverseitige Duplicate Prevention.
- `TODO`: Persistenztechnik, Eindeutigkeitsbedingungen und Aufbewahrungszeit festlegen.

## Fachliche Status

Mindestens folgende Zustaende muessen unterscheidbar sein; die technischen Namen duerfen spaeter angepasst werden:

- `CREATED`: Vorgang angelegt, noch nichts an Oxaion gesendet
- `VALIDATING`: Eingaben und Oxaion-Ausgangsdaten werden geprueft
- `SENDING_TO_OXAION`: Versand wurde begonnen; Ergebnis ist noch nicht bestaetigt
- `SUCCESS`: Oxaion hat die erfolgreiche Buchung eindeutig bestaetigt
- `REJECTED`: Oxaion hat den Auftrag fachlich eindeutig abgelehnt
- `LOCKED`: Oxaion-Datensatz ist gesperrt; Buchung wurde nicht durchgefuehrt
- `UNCERTAIN`: Auftrag koennte verarbeitet worden sein, Ergebnis ist nicht eindeutig
- `MANUAL_REVIEW_REQUIRED`: Automatische Klaerung ist nicht moeglich oder nicht zulaessig

Jeder Statuswechsel wird mit Zeitstempel, Ursache und technischer beziehungsweise fachlicher Referenz protokolliert.

## Oxaion-Sperren

- Eine bestaetigte Oxaion-Datensatzsperre ist ein eigener fachlicher Zustand `LOCKED`.
- Bei Sperre gilt die Buchung als nicht durchgefuehrt, sofern die Oxaion-Schnittstelle dies eindeutig bestaetigt.
- Der tatsaechlich sperrende Benutzer wird nur aus zuverlaessiger Oxaion-Sperrinformation angezeigt.
- `Zuletzt geaendert von` ist kein Ersatz fuer die Sperrinformation.
- Ist der Sperrer nicht ermittelbar, lautet die Anzeige `Gesperrt durch: nicht ermittelbar`.
- Es gibt keine automatische Endlosschleife; ein erneuter Versuch wird bewusst durch den Bediener gestartet.
- `TODO`: Oxaion-Sperrabfrage, Fehlerkennung und Benutzerermittlung technisch bestaetigen.

## HTTP Timeout und Connection Reset

Timeout oder Connection Reset beweisen allein nicht, dass Oxaion den Auftrag nicht verarbeitet hat.

- Fehler vor zweifelsfreiem Versand: kontrollierter Retry kann gemaess definierter Regel zulaessig sein.
- Fehler waehrend oder nach moeglichem Versand: Status `UNCERTAIN`; keine erneute Buchung.
- Das Backend protokolliert Versandbeginn, Abbruchzeitpunkt, Zieloperation, Transaktions-ID und vorhandene Oxaion-Referenzen.
- Nach Moeglichkeit wird der Ausgang ueber eine fachlich bestaetigte Oxaion-Status- oder Referenzabfrage geklaert.
- `TODO`: Timeoutwerte und verifizierbare Oxaion-Ergebnisabfrage festlegen.

## HTTP-Fehlercodes

HTTP-Status und fachliches Ergebnis werden getrennt bewertet:

- Ein HTTP-Erfolgscode ist nicht automatisch eine erfolgreiche Materialbuchung; die fachliche Antwort muss geprueft werden.
- Ein HTTP-Fehlercode ist nicht automatisch der Beweis, dass keine Buchung erfolgte; Versandphase und Oxaion-Verhalten sind entscheidend.
- Authentifizierungs- und Autorisierungsfehler fuehren zu keiner automatischen Wiederholung und muessen administrativ geklaert werden.
- Rate-Limit-, Gateway- und Serverfehler duerfen nur wiederholt werden, wenn Nichtverarbeitung zweifelsfrei nachgewiesen ist.
- Konkrete Zuordnung von HTTP-Codes zu Oxaion-Ergebnissen bleibt bis zur Schnittstellenanalyse `TODO`.

## Fachliche Oxaion-Fehler

- Eine eindeutige fachliche Ablehnung wird als `REJECTED` gespeichert.
- Die bereinigte Oxaion-Meldung wird in eine verstaendliche Bedienermeldung mit konkreter Massnahme ueberfuehrt.
- Ein Retry ohne fachliche Korrektur ist nicht zulaessig.
- Unbekannte Codes werden nicht interpretiert oder erfunden, sondern als offen dokumentiert und gegebenenfalls zur manuellen Pruefung gegeben.

## Unklare Buchungsergebnisse

Ein Ergebnis ist unklar, wenn Oxaion die Anfrage moeglicherweise erhalten oder verarbeitet hat, aber keine eindeutige bestaetigte Antwort vorliegt.

Dann gilt:

1. Status auf `UNCERTAIN` setzen.
2. Weitere automatische und manuelle Doppel-Ausloesung derselben Vorgangs-ID blockieren.
3. Vorhandene Referenzen und Protokolle sichern, ohne Secrets zu speichern.
4. Eine bestaetigte Ergebnisabfrage versuchen, sofern diese rein lesend und fachlich geklaert ist.
5. Bei fehlender Klaerbarkeit auf `MANUAL_REVIEW_REQUIRED` setzen.
6. Ergebnis der manuellen Pruefung mit Benutzer, Zeitpunkt, Begruendung und finalem Status protokollieren.

## Retry-Regeln

- Kein Retry bei `SUCCESS`, `REJECTED`, `LOCKED`, `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED`.
- Ein automatischer Retry ist nur bei eindeutigem Nichtversand beziehungsweise nachgewiesener Nichtverarbeitung zulaessig.
- Anzahl, Abstand und technische Bedingungen erlaubter Retries werden begrenzt und protokolliert.
- Ein manueller erneuter Versuch nach `LOCKED` ist ein bewusster Bedienvorgang und muss nachvollziehbar dem vorherigen Vorgang zugeordnet werden.
- `TODO`: Konkrete Retry-Policy erst nach Analyse der Oxaion-Schnittstelle festlegen.

## Manuelle Nachbearbeitung

Eine manuelle Nachbearbeitung benoetigt mindestens:

- Transaktions-ID und Zeitstempel
- Benutzer und Buchungsart
- Fertigungsauftrag, Material, Charge und Mix-Charge
- Plan- und Ist-Maschine
- angeforderte Menge, Quell- und Ziellager
- Versandstatus und bereinigte Oxaion-Referenz/-Antwort
- bisherigen Statusverlauf
- dokumentierte Pruefung in Oxaion
- Entscheidung, Begruendung, verantwortliche Person und finalen Status

Die Nachbearbeitung darf keine unkontrollierte direkte SQL-Buchung in Oxaion verwenden.

## Protokoll- und Sicherheitsregeln

- Keine Passwoerter, Tokens, Connection Strings oder sonstigen Secrets protokollieren.
- Personenbezogene Daten nur im erforderlichen Umfang speichern und angemessen schuetzen.
- Request- und Response-Daten vor der Persistierung filtern beziehungsweise redigieren.
- Audit-Daten gegen unbeabsichtigte Aenderung schuetzen und eine noch festzulegende Aufbewahrungsregel anwenden.
