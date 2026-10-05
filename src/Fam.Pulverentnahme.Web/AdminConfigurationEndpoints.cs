using System.Text.Encodings.Web;

namespace Fam.Pulverentnahme.Web;

public static class AdminConfigurationEndpoints
{
    public static IEndpointRouteBuilder MapAdminConfiguration(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/admin", () => Results.Content(AdminHtml, "text/html; charset=utf-8"));

        endpoints.MapGet("/api/admin/config", (RuntimeConfigurationService runtime) =>
            Results.Ok(runtime.GetView()));

        endpoints.MapPost("/api/admin/config", (
            RuntimeConfigurationUpdate request,
            RuntimeConfigurationService runtime) =>
        {
            try
            {
                return Results.Ok(runtime.Save(request));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        endpoints.MapPost("/api/admin/test", async (
            RuntimeConfigurationService runtime,
            OxaionClient oxaion,
            CancellationToken ct) =>
        {
            try
            {
                var result = await runtime.TestCurrentAsync(oxaion, ct);
                return result.Ok
                    ? Results.Ok(result)
                    : Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        });

        return endpoints;
    }

    private const string AdminHtml = """
<!doctype html>
<html lang="de">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>FAM Pulverentnahme – Serverkonfiguration</title>
<style>
:root{font-family:Segoe UI,Arial,sans-serif;color:#17384d;background:#eef3f6}
*{box-sizing:border-box}body{margin:0}.wrap{max-width:980px;margin:0 auto;padding:24px}
h1{margin:0 0 6px}.lead{margin:0 0 20px;color:#516875}
.card{background:#fff;border:1px solid #c8d7df;border-radius:14px;padding:20px;margin:14px 0;box-shadow:0 3px 14px #17384d12}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:14px}.full{grid-column:1/-1}
label{display:block;font-weight:700;margin-bottom:5px}input,select{width:100%;padding:10px;border:1px solid #9fb8c6;border-radius:8px;font:inherit}
.check{display:flex;align-items:center;gap:8px;font-weight:600}.check input{width:auto}
.env{display:grid;grid-template-columns:1fr 1fr;gap:10px}.env label{border:2px solid #adc7d5;border-radius:10px;padding:13px;background:#f7fafb}.env input{width:auto}
.prod{border-color:#d78a80!important;background:#fff4f2!important}
.actions{display:flex;gap:10px;flex-wrap:wrap}.btn{border:0;border-radius:9px;padding:11px 17px;font:inherit;font-weight:800;cursor:pointer}
.primary{background:#1674ad;color:#fff}.secondary{background:#e6eef2;color:#17384d}
.status{padding:12px;border-radius:9px;margin-top:12px;white-space:pre-wrap}.ok{background:#e7f6ee;color:#11663f}.bad{background:#fdebea;color:#a12620}.neutral{background:#edf2f5;color:#516875}
small{color:#657b87}.warning{background:#fff4db;border:1px solid #e5bc62;padding:12px;border-radius:9px}
@media(max-width:700px){.grid,.env{grid-template-columns:1fr}.wrap{padding:12px}}
</style>
</head>
<body>
<div class="wrap">
<h1>FAM Pulverentnahme – Serverkonfiguration</h1>
<p class="lead">Nur lokal auf APP-01 über <b>127.0.0.1:5081</b>. Zugangsdaten werden nicht im Frontend oder Repository gespeichert.</p>

<div class="card">
<h2>Umgebung</h2>
<div class="env">
<label><input type="radio" name="environment" value="STAGING"> STAGING / Test</label>
<label class="prod"><input type="radio" name="environment" value="PRODUCTION"> PRODUCTION / Produktiv</label>
</div>
<div id="prodConfirmWrap" class="warning" style="display:none;margin-top:12px">
<label class="check"><input id="confirmProduction" type="checkbox"> Ich bestätige bewusst die Umschaltung auf PRODUCTION. Produktivbuchungen wirken in Oxaion.</label>
</div>
</div>

<div class="card">
<h2>SQL Server – gemeinsame native Anmeldung</h2>
<div class="grid">
<div><label for="sqlServer">SQL Server / Instanz</label><input id="sqlServer" autocomplete="off"></div>
<div><label for="sqlUser">SQL Benutzer</label><input id="sqlUser" autocomplete="off"></div>
<div><label for="sqlPassword">SQL Passwort</label><input id="sqlPassword" type="password" autocomplete="new-password" placeholder="leer = vorhandenes Passwort beibehalten"><small id="sqlPasswordState"></small></div>
<div>
<label>SQL Verschlüsselung</label>
<label class="check"><input id="sqlEncrypt" type="checkbox"> Encrypt</label>
<label class="check"><input id="sqlTrust" type="checkbox"> TrustServerCertificate</label>
</div>
<div><label for="syncosStg">Syncos STAGING Datenbank</label><input id="syncosStg" value="syncos_stg_102"></div>
<div><label for="syncosProd">Syncos PRODUCTION Datenbank</label><input id="syncosProd" value="syncos_prd_102"></div>
<div><label for="syncosSchema">Syncos Schema</label><input id="syncosSchema" value="ITSDEV"></div>
<div></div>
<div><label for="oxaionDbStg">Oxaion SQL STAGING Datenbank</label><input id="oxaionDbStg" placeholder="exakter DB-Katalogname"></div>
<div><label for="oxaionDbProd">Oxaion SQL PRODUCTION Datenbank</label><input id="oxaionDbProd" placeholder="exakter DB-Katalogname"></div>
</div>
<p><small>Ein SQL-Login wird für beide Systeme verwendet. Die Datenbank wird abhängig von STAGING/PRODUCTION ausgewählt. Die Oxaion-SQL-Verbindung bleibt rein lesend.</small></p>
</div>

<div class="card">
<h2>Oxaion HTTP</h2>
<div class="grid">
<div><label for="oxaionStgUrl">STAGING URL</label><input id="oxaionStgUrl"></div>
<div><label for="oxaionProdUrl">PRODUCTION URL</label><input id="oxaionProdUrl"></div>
<div><label for="oxaionFirm">Firma</label><input id="oxaionFirm" value="103"></div>
<div><label for="oxaionUser">Oxaion Benutzer</label><input id="oxaionUser" autocomplete="off"></div>
<div><label for="oxaionPassword">Oxaion Passwort</label><input id="oxaionPassword" type="password" autocomplete="new-password" placeholder="leer = vorhandenes Passwort beibehalten"><small id="oxaionPasswordState"></small></div>
<div><label class="check" style="margin-top:27px"><input id="developerTools" type="checkbox"> Diagnose / Dev-Infos freigeben</label></div>
</div>
</div>

<div class="card">
<div class="actions">
<button class="btn primary" id="save">Speichern</button>
<button class="btn secondary" id="test">Aktive Verbindungen testen</button>
</div>
<div id="status" class="status neutral">Konfiguration wird geladen …</div>
<p><small id="configPath"></small></p>
</div>
</div>
<script>
const $=id=>document.getElementById(id);
let current=null;
function environment(){return document.querySelector('input[name="environment"]:checked')?.value||'STAGING'}
function updateProd(){const prod=environment()==='PRODUCTION';$('prodConfirmWrap').style.display=prod?'block':'none';if(!prod)$('confirmProduction').checked=false}
document.querySelectorAll('input[name="environment"]').forEach(x=>x.addEventListener('change',updateProd));
function status(text,kind='neutral'){$('status').className='status '+kind;$('status').textContent=text}
async function json(url,opts={}){const r=await fetch(url,{headers:{'Content-Type':'application/json','X-FAM-Admin':'1'},cache:'no-store',...opts});let b=null;try{b=await r.json()}catch{}if(!r.ok)throw new Error(b?.error||b?.detail||b?.message||('HTTP '+r.status));return b}
function set(id,v){const e=$(id);if(e)e.value=v??''}
function fill(c){
 current=c;
 document.querySelector('input[name="environment"][value="'+c.environment+'"]').checked=true;
 set('sqlServer',c.sqlServer);set('sqlUser',c.sqlUser);$('sqlEncrypt').checked=!!c.sqlEncrypt;$('sqlTrust').checked=!!c.sqlTrustServerCertificate;
 set('syncosStg',c.syncosStagingDatabase);set('syncosProd',c.syncosProductionDatabase);set('syncosSchema',c.syncosSchema);
 set('oxaionDbStg',c.oxaionStagingDatabase);set('oxaionDbProd',c.oxaionProductionDatabase);
 set('oxaionStgUrl',c.oxaionStagingUrl);set('oxaionProdUrl',c.oxaionProductionUrl);set('oxaionFirm',c.oxaionFirm);set('oxaionUser',c.oxaionUser);
 $('developerTools').checked=!!c.developerToolsEnabled;
 $('sqlPasswordState').textContent=c.sqlPasswordConfigured?'Passwort ist gespeichert.':'Noch kein Passwort gespeichert.';
 $('oxaionPasswordState').textContent=c.oxaionPasswordConfigured?'Passwort ist gespeichert.':'Noch kein Passwort gespeichert.';
 $('configPath').textContent='Gespeichert unter: '+c.configPath;
 updateProd();
 status((c.persisted?'Gespeicherte':'Noch nicht persistierte')+' Konfiguration · aktiv: '+c.environment+' · Syncos '+(c.selectedSyncosDatabase||'—')+' · Oxaion SQL '+(c.selectedOxaionDatabase||'—'),'ok');
}
async function load(){try{fill(await json('/api/admin/config'))}catch(e){status(e.message,'bad')}}
$('save').onclick=async()=>{
 try{
  status('Konfiguration wird gespeichert …');
  const payload={
   environment:environment(),sqlServer:$('sqlServer').value,sqlUser:$('sqlUser').value,sqlPassword:$('sqlPassword').value||null,
   sqlEncrypt:$('sqlEncrypt').checked,sqlTrustServerCertificate:$('sqlTrust').checked,
   syncosStagingDatabase:$('syncosStg').value,syncosProductionDatabase:$('syncosProd').value,syncosSchema:$('syncosSchema').value,
   oxaionStagingDatabase:$('oxaionDbStg').value,oxaionProductionDatabase:$('oxaionDbProd').value,
   oxaionStagingUrl:$('oxaionStgUrl').value,oxaionProductionUrl:$('oxaionProdUrl').value,oxaionFirm:$('oxaionFirm').value,
   oxaionUser:$('oxaionUser').value,oxaionPassword:$('oxaionPassword').value||null,developerToolsEnabled:$('developerTools').checked,
   confirmProduction:$('confirmProduction').checked
  };
  const saved=await json('/api/admin/config',{method:'POST',body:JSON.stringify(payload)});
  $('sqlPassword').value='';$('oxaionPassword').value='';fill(saved);
  status('Konfiguration gespeichert und für neue Backend-Aufrufe aktiviert. Bereits angemeldete Bediener müssen sich nach einem Umgebungswechsel neu anmelden.','ok');
 }catch(e){status(e.message,'bad')}
};
$('test').onclick=async()=>{try{status('Verbindungen werden geprüft …');const r=await json('/api/admin/test',{method:'POST',body:'{}'});status('SYNCOS SQL: '+r.syncosSqlMessage+'\nOXAION SQL: '+r.oxaionSqlMessage+'\nOXAION HTTP: '+r.oxaionHttpMessage,r.ok?'ok':'bad')}catch(e){status(e.message,'bad')}};
load();
</script>
</body>
</html>
""";
}
