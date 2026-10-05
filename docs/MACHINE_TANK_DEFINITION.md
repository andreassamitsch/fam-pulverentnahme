# Maschinentanks: dynamische Definition aus Oxaion

Stand: 02.10.2026

## Verbindliche fachliche Definition

Maschinentanks werden **nicht** als statische Liste in der WebApp konfiguriert.

Die fuehrende Definition kommt dynamisch aus Oxaion `ULGSTP`.

Bestaetigte Abfrage:

```sql
SELECT *
FROM ULGSTP
WHERE LGFIRM = '103'
  AND LGLGART = '02';
```

Fuer das Backend wird dieselbe Bedingung parametrisiert und nur mit den benoetigten Feldern gelesen:

```sql
SELECT
    LG.LGLAGO,
    LG.LGBEZC
FROM OXAION.ULGSTP AS LG
WHERE LG.LGFIRM = @firm
  AND LG.LGLGART = N'02'
ORDER BY LG.LGLAGO;
```

Bedeutung:

- `LGFIRM`: Oxaion-Firma, aktuell `103`
- `LGLGART = '02'`: verbindliche Kennzeichnung fuer Tanklagerorte
- `LGLAGO`: Oxaion-Lagerortcode des Tanks
- `LGBEZC`: Bezeichnung des Tanklagerorts

Es gibt keine zusaetzliche hart codierte Whitelist wie `EOS1`/`EOS2`.

## Aktuell bestaetigter PRD-Stand 02.10.2026

Die direkte Abfrage in Oxaion PRODUCTION lieferte fuer Firma `103` und Lagerortart `02`:

- `EOS1` – `EOS 1 -Tank`
- `EOS2` – `EOS 2 -Tank`
- `M400-01` – `EP-M400S-01-Tank`
- `M400-02` – `EP-M400S-02-Tank`
- `M650` – `EP-M650-01-Tank`

Diese Liste ist nur ein dokumentierter Snapshot. Die WebApp darf diese Werte nicht fest codieren. Neue oder entfernte Tanklagerorte werden ueber `ULGSTP` wirksam.

## Verwendung in der WebApp

Die dynamische Liste wird fuer alle tankbezogenen Prozesse verwendet:

- Tank-QR-Validierung
- Pulver nachfuellen
- Pulver auf Fertigungsauftrag buchen
- Pulver aus Tank auslagern
- Neues Pulver in Tank fuellen
- Jobabbruch-Korrektur, wenn der Tank aus der Originalrueckmeldung abgeleitet wird
- Maschinentankbereich der Lageruebersicht

`GET /api/machines` liefert die aktuell aus Oxaion SQL gelesene Liste.

`GET /api/machines/{warehouse}/stock` akzeptiert einen Lagerort nur, wenn er im aktuell aktiven Oxaion-SQL-Ziel fuer die aktive Firma mit `LGLGART = '02'` vorhanden ist.

Die PWA laedt die Maschinenliste vor einem Tankscan erneut, damit ein Umgebungswechsel oder eine Aenderung in Oxaion nicht durch eine alte Browserliste verdeckt wird.

## STAGING / PRODUCTION

Die Tankdefinition folgt automatisch der im lokalen Serverdienst ausgewaehlten Oxaion-SQL-Datenbank:

- STAGING -> konfigurierte Oxaion-STAGING-SQL-Datenbank
- PRODUCTION -> konfigurierte Oxaion-PRODUCTION-SQL-Datenbank

Damit koennen STAGING und PRODUCTION unterschiedliche Tanklagerorte besitzen, ohne dass dafuer eine WebApp-Konfiguration gepflegt werden muss.

Die gemeinsame native SQL-Anmeldung benoetigt deshalb Leseberechtigung auf:

- `OXAION.ULGSTP`
- den bereits dokumentierten rein lesenden Lagerbestandsabfragen

Die lokale Admin-Funktion `Aktive Verbindungen testen` prueft ab diesem Stand auch den Lesezugriff auf `ULGSTP`.

## Sicherheitsgrenze

Die dynamische Tankdefinition ist eine rein lesende Oxaion-SQL-Funktion.

Sie ist keine Freigabe fuer direkte SQL-Buchungen. Alle ERP-Buchungen bleiben ausschliesslich auf den bestaetigten Oxaion-HTTP-/Fachlogikpfaden.

Ein Lagerort, der nicht mit `LGLGART = '02'` in der aktiven Oxaion-Umgebung vorhanden ist, darf von der WebApp nicht als Maschinentank verwendet werden.
