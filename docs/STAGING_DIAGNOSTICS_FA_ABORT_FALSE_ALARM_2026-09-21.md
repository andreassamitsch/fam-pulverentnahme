# STAGING: Falscher Leerbildschirm-Alarm bei FA-Jobabbruch (21.09.2026)

## Android-Livebefund

Nach Auswahl von `Korrekturbuchung Fertigungsauftrag (Jobabbruch)` zeigte die App im aktiven Prozess die reguläre Karte `Maschinentank scannen` und den noch gesperrten nächsten Schritt `Fertigungsauftrag scannen`. Zusätzlich erschien fälschlich die gelbe Diagnosekarte `Anzeigeproblem erkannt`.

Das vom Bediener kopierte UI-Protokoll `20260910-ui-diag-2` bestätigte zum Zeitpunkt der Auswahl:

- `shell=process`, `activeMode=fa-abort-correction`, `rememberedMode=fa-abort-correction`;
- `auth=true`, `selected=true`, `authenticated=true`;
- `instruction=Maschinentank scannen.`;
- `visibleIds=[]` und anschließend `BLANK_PROCESS_DETECTED`;
- der Screenshot zeigte jedoch die sichtbare reguläre Karte des neuen FA-Jobabbruch-Prozesses.

Es lag zu diesem Zeitpunkt kein fehlgeschlagener Tankscan und keine nachgewiesene fehlende Buchungskarte vor: Der Fehler wurde bereits nach der Prozessauswahl und **vor** einem Scan ausgelöst. Das Service-Worker-Steuerungsflag war `true`, und der Prozess wurde erfolgreich gewählt.

## Konkrete Ursache

Der Diagnose-Logger `wwwroot/ui-diagnostics.js` zählte in `snapshot()` und `hasVisibleProcessContent()` die bisher vorhandenen Prozess-Panels, aber nicht das neu eingeführte `faAbortProcess`. Dadurch wurde der ordnungsgemäß sichtbare Jobabbruch-Panel im Logger als `visibleIds=[]` behandelt und nach 500 ms die falsche Warnkarte angezeigt. Der manuelle Wiederherstellungsbutton rief außerdem unabhängig vom gewählten Modus den Nachfüll-Guard auf, obwohl dieser für `fa-abort-correction` nicht zuständig ist.

## Behebung

- `faAbortProcess` in das Sichtbarkeitsprotokoll und die eindeutige Zuordnung `fa-abort-correction -> faAbortProcess` aufgenommen.
- Bei eigenständigen Prozessen zählt ausschließlich das tatsächlich zum aktiven Modus gehörige sichtbare Panel als gesunder UI-Zustand; Legacy-Karten gelten nur für `replenish`.
- Sichtbarkeit anhand des real gerenderten Elements (`getClientRects()`) geprüft, statt nur dessen eigener CSS-Display-Eigenschaft.
- Diagnoselog um `expectedPanelId`, `expectedPanelVisible` und Klicks auf `abortTankScan`/`abortOrderScan` erweitert.
- Der Nachfüll-Guard wird durch die manuelle Diagnose-Wiederherstellung nur bei aktivem Nachfüllen angesprochen; für andere Prozesse wird ausschließlich der normale UI-Refresh angestoßen.
- Service-Worker-App-Shell von v32 auf v33 erhöht und statische Assets beim Installieren erneut aus dem Netz geladen.
- Regressionstest ergänzt, der die Zuordnung aller eigenständigen Panels einschließlich des Jobabbruchs im Diagnosecode prüft.

Diese Änderung betrifft nur Anzeige/Diagnose; es werden weder Oxaion-Programme noch FA-Storno-, I1/I2-, LF/LE- oder MK-Buchungsschritte geändert.

## Erforderlicher Android-Test

Nach Deployment des neuen STAGING-Builds PWA vollständig schließen und erneut öffnen. Anmelden, den Jobabbruchprozess wählen und zunächst **ohne Scan** prüfen, dass `Maschinentank scannen` ohne Warnkarte angezeigt wird. Danach den Tank-Scan prüfen. Falls nach dem Scan erneut ein wirklich leerer Bereich entsteht, `Diagnose` im Header drücken, Protokoll kopieren und den Zustand mit der Version `20260921-ui-diag-3` melden. In diesem Fall müssen `expectedPanelId=faAbortProcess` und `expectedPanelVisible` die weitere Untersuchung ermöglichen.
