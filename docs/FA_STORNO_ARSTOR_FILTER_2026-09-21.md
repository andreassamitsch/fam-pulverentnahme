# FA-Jobabbruch: Oxaion-Stornoliste und korrigierter HTTP-Aufruf (21.09.2026)

## Beobachtungen aus dem Android-Test

Der Bediener bestaetigt anhand des Oxaion-Standardfensters `Rueckmeldung stornieren`, dass dort ohne manuellen ARSTOR-Filter nur gueltige, noch stornierbare Rueckmeldungen erscheinen. Das am 21.09.2026 gezeigte Fenster zeigt fuer den getesteten FA genau eine MK-Rueckmeldung (Materialposition 10, 2,815 kg RP.00010). Die Anzeige des Fensters allein beweist nicht, welchen Buchungs-/Fehlerstatus ein frueherer App-POST hatte; deshalb bestehende Vorgangs-ID und Oxaion-Status vor einem erneuten Schreibversuch pruefen.

## Technischer Nachweis aus dem vorhandenen FAM-JET vom 18.09.2026

Der vorhandene Mitschnitt enthielt bereits die erforderliche Sequenz: `PW22000J *LOADNEW (STORNO=J)` -> `PW22000J *STON` -> `PW22021R *GETHDR` -> `PW22021R *FIRSTLIST` -> `PW22021R *STORNO` -> `PW22021R *GETU01 / *FIRSTLIST`. Im Referenzfall liefert die erste FIRSTLIST genau eine gueltige Rueckmeldung mit eindeutigem KEY; nach erfolgreichem STORNO liefert dieselbe Standardliste null Zeilen. Ein gesonderter HTTP-Filteraufruf fuer ARSTOR ist in diesem Mitschnitt weder vorhanden noch erforderlich. Das fehlende ARSTOR-Feld pro ROW ist somit nicht automatisch ein Problem: die Oxaion-Standardliste liefert bereits die gueltigen Stornokandidaten.

Der eigentliche nachweisbare Implementierungsfehler im Backend lag in der Payload-Konstruktion: Das originale `PW22021R *STORNO` verwendet das DTA aus `PW22021R *GETHDR` (im Mitschnitt 26 Felder) und ergaenzt die exakten Rueckmeldeschluesselfelder (im Mitschnitt insgesamt 30 Felder). Der bisherige App-Code verwendete dagegen `Merge(storno.Header.Dta, storno.State)` und haengte alle Felder des vorherigen `PW22000J *STON`-Dialogzustands an bzw. ueberschrieb Header-Werte. Diese unbestaetigte, wesentlich groessere Payload ist entfernt. Ob sie die konkrete zuvor am Android-Geraet angezeigte Fehlermeldung verursacht hat, ist ohne dessen Transaktionsstatus nicht abschliessend nachgewiesen.

## Implementierter Stand

- Quelle der Kandidaten: von Oxaion standardmaessig gefilterte `PW22021R *FIRSTLIST` mit `STOP`-Nachweis. Falls ein ROW explizit einen `ARSTOR`-Status liefert, werden nur `N`-Zeilen ausgewertet; es wird jedoch **kein eigener unbestaetigter Filterparameter an Oxaion gesendet**.
- Vor dem Storno muss genau ein Kandidat auf FA, Materialposition, Artikel, urspruengliche Menge, Tanklager und Mix-Charge passen. Null oder mehrere Treffer blockieren den Schreibschritt weiterhin.
- `PW22021R *STORNO` erhaelt ausschliesslich die von Oxaion in `GETHDR` bestaetigten Headerfelder plus den eindeutig ausgewaehlten Rueckmeldeschluessel und die bestaetigte Session-ID. Kein Merge des alten PW22000J-Dialogzustands.
- Nach dem Aufruf gelten weiter die drei verifizierten Nachweise: urspruengliche Rueckmeldung aus der gueltigen Stornoliste verschwunden, FA-Materialverbrauch/Status zurueckgesetzt und dieselbe Tank-Mix-Charge exakt um die Originalmenge erhoeht. Erst danach darf die neue MK gebucht werden.
- Bei `UNCERTAIN` oder `MANUAL_REVIEW_REQUIRED` weder Storno noch korrigierte MK blind wiederholen. Vor einem weiteren Versuch den Status der bereits angelegten WebApp-Operation in Oxaion klaeren.

Regressionstests pruefen die 26+4-Felder-Payload und die Kandidatenauswahl in einer bereits gefilterten Standardliste sowie den Fall expliziter `ARSTOR=N/J`-ROW-Felder.
