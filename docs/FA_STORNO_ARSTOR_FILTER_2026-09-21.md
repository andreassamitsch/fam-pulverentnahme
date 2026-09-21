# FA-Jobabbruch: Auswahl gueltiger Rueckmeldungen ueber ARSTOR (21.09.2026)

## Neuer Android-Befund / verbindliche Anforderung

Der Bediener meldet, dass die FA-Jobabbruch-Korrektur nicht gebucht werden kann, wenn mehrere historisch gleichartige Rueckmeldungen in der Oxaion-Stornoliste stehen. Laut fachlicher Rueckmeldung kennzeichnet `ARSTOR=N` eine gueltige, noch nicht stornierte Rueckmeldung. Bei der automatischen Auswahl duerfen nur solche Rueckmeldungen beruecksichtigt werden. Innerhalb dieser gueltigen Treffer gelten die bisherigen eindeutigen Pruefungen fuer FA, Materialposition, Artikel, urspruengliche Menge, Tanklager und Mix-Charge unveraendert. Auch nach dem Storno ist der Gueltigkeitsstatus der **exakten** Rueckmeldung zu kontrollieren; der urspruengliche Rueckmeldeschluessel koennte weiterhin als historischer Eintrag in der Liste sichtbar sein. FA- und Tank-/Mix-Endzustand bleiben zwingend zu verifizieren.

## Technischer Ist-Stand / noch benoetigter Nachweis

Die am 18.09.2026 aufgezeichnete `PW22021R *FIRSTLIST`-Antwort (`1789725755077out.xml` in `FA Mat Storno - Schwund und Plus LB buchungen.7z`) enthaelt den Rueckmeldeschluessel und die sichtbaren Felder `PWARMP.ARAKKZ`, `PWARMP.ARPOSN`, `_INTERN.WW_TX50`, `_INTERN.WW_TX70B`, aber **kein `ARSTOR` und kein `PWARMP.ARSTOR` im `ROW`**. Ein `ARSTOR`-Feld im separaten Header-/Dialog-DTA belegt nicht den Stornostatus eines einzelnen Listeneintrags. Der aktuelle Code `FaAbortCorrectionService.ParseFeedbacks` kann somit aus dieser Listenantwort nicht entscheiden, welcher Treffer `ARSTOR=N` hat.

**Offen vor schreibender Umsetzung:** Ein JET-/HTTP-Mitschnitt, der zeigt, wie im Oxaion-Stornodialog fuer genau diese `PW22021R`-Rueckmeldeliste der Filter `ARSTOR=N` wirksam gesetzt wird (Programme, Commands, Filterfelder und die danach zurueckgegebene Liste). Alternativ muss ein bestaetigter Oxaion-HTTP-Leseweg den `ARSTOR`-Status je Rueckmeldeschluessel nachweisen. Nie bloss `ARSTOR=N` an einem vermuteten Requestfeld uebergeben und die Filterung als erfolgreich behaupten.

Bis dieser Nachweis vorliegt: keine Lockerung der bisherigen Eindeutigkeitspruefung, keine Auswahl der vermeintlich letzten Rueckmeldung und kein blinder Storno-Retry nach unklarem Ausgang. Keine neue STAGING-Buchungsfreigabe allein aufgrund dieses fachlichen Hinweises.
