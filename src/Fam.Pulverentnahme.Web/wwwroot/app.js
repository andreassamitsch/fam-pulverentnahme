'use strict';

const $=id=>document.getElementById(id);
const DB_NAME='fam-pulver-staging';
const STORE='operations';
const ACTIVE_KEY='active';
let active=null;
let machineStock=null;
let stockLoading=false;

function pad(n){return String(n).padStart(2,'0')}
function today(){const d=new Date();return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}`}
function parseQty(v){const s=String(v??'').trim();if(!s)throw new Error('Menge fehlt.');const n=Number(s.replace(',','.'));if(!Number.isFinite(n))throw new Error(`Ungültige Menge: ${v}`);return n}
function formatQty(v){return Number(v).toFixed(3).replace('.',',')}
function newBatch(){const d=new Date();return `RP10WEB_${d.getFullYear()}${pad(d.getMonth()+1)}${pad(d.getDate())}_${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`}
function esc(v){return String(v??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]))}
function opId(){
  const c=globalThis.crypto;
  if(c&&typeof c.randomUUID==='function')return c.randomUUID();
  if(c&&typeof c.getRandomValues==='function'){
    const bytes=new Uint8Array(16);c.getRandomValues(bytes);
    bytes[6]=(bytes[6]&0x0f)|0x40;bytes[8]=(bytes[8]&0x3f)|0x80;
    const h=Array.from(bytes,b=>b.toString(16).padStart(2,'0'));
    return `${h.slice(0,4).join('')}-${h.slice(4,6).join('')}-${h.slice(6,8).join('')}-${h.slice(8,10).join('')}-${h.slice(10,16).join('')}`;
  }
  throw new Error('Dieser Browser kann keine sichere clientOperationId erzeugen. Bitte einen aktuellen Android-Browser verwenden.');
}

function openDb(){return new Promise((resolve,reject)=>{const r=indexedDB.open(DB_NAME,1);r.onupgradeneeded=()=>r.result.createObjectStore(STORE);r.onsuccess=()=>resolve(r.result);r.onerror=()=>reject(r.error)})}
async function dbGet(){const db=await openDb();return new Promise((resolve,reject)=>{const tx=db.transaction(STORE,'readonly');const r=tx.objectStore(STORE).get(ACTIVE_KEY);r.onsuccess=()=>resolve(r.result||null);r.onerror=()=>reject(r.error)})}
async function dbSet(v){const db=await openDb();return new Promise((resolve,reject)=>{const tx=db.transaction(STORE,'readwrite');tx.objectStore(STORE).put(v,ACTIVE_KEY);tx.oncomplete=()=>resolve();tx.onerror=()=>reject(tx.error)})}
async function dbClear(){const db=await openDb();return new Promise((resolve,reject)=>{const tx=db.transaction(STORE,'readwrite');tx.objectStore(STORE).delete(ACTIVE_KEY);tx.oncomplete=()=>resolve();tx.onerror=()=>reject(tx.error)})}

function values(){return {
  clientOperationId:active?.request?.clientOperationId||opId(),
  personnelNo:$('personnelNo').value.trim(),personnelName:$('personnelName').value.trim(),
  article:$('article').value.trim(),articleText:$('articleText').value.trim(),
  oldMixWarehouse:$('oldMixWarehouse').value.trim(),oldMixWarehouseText:$('oldMixWarehouseText').value.trim(),oldMixStorageBin:$('oldMixStorageBin').value.trim(),oldMixBatch:$('oldMixBatch').value.trim(),oldMixAmountKg:parseQty($('oldMixAmountKg').value),
  addWarehouse:$('addWarehouse').value.trim(),addWarehouseText:$('addWarehouseText').value.trim(),addStorageBin:$('addStorageBin').value.trim(),addBatch:$('addBatch').value.trim(),addAmountKg:parseQty($('addAmountKg').value),
  targetWarehouse:$('targetWarehouse').value.trim(),targetWarehouseText:$('targetWarehouseText').value.trim(),targetStorageBin:$('targetStorageBin').value.trim(),targetBatch:$('targetBatch').value.trim(),
  productionDate:$('productionDate').value,bookingDate:$('bookingDate').value,bookingText:$('bookingText').value.trim(),simulateFailure:$('simulateFailure').value||null,retryOfClientOperationId:null
}}

function validate(r){const req=['clientOperationId','personnelNo','article','oldMixWarehouse','oldMixBatch','addWarehouse','addBatch','targetWarehouse','targetBatch','productionDate','bookingDate'];const m=req.filter(k=>!r[k]);if(m.length)throw new Error('Pflichtfelder fehlen: '+m.join(', '));if(r.oldMixAmountKg<=0||r.addAmountKg<=0)throw new Error('Mengen müssen > 0 sein.');if(r.targetBatch===r.oldMixBatch||r.targetBatch===r.addBatch)throw new Error('Neue Mix-Charge muss von beiden Quellchargen verschieden sein.')}

function friendlyServerMessage(s){
  const raw=s?.message||'';
  if(s?.status==='REJECTED'&&/U180500/.test(raw)&&/KOBGDT/.test(raw)){
    return 'Keine Buchung durchgeführt. Die für das Buchungsdatum benötigte Lagerbuchhaltungsperiode ist in oxaion noch nicht eröffnet. Bitte Periode in oxaion öffnen lassen bzw. das zulässige Buchungsdatum klären. Danach kann derselbe Buchungsauftrag bewusst mit einer neuen clientOperationId erneut versucht werden.';
  }
  if(s?.status==='REJECTED')return `Keine Buchung durchgeführt. oxaion hat den Vorgang eindeutig abgelehnt. Ursache: ${raw||'siehe technische Meldung'}`;
  if(s?.status==='CONFLICT')return raw||'Der aktuelle Maschinenbestand stimmt nicht mehr mit dem vorbereiteten Vorgang überein. Es wurde keine Materialbuchung gestartet.';
  if(s?.status==='MACHINE_STOCK_UNAVAILABLE')return raw||'Der aktuelle Maschinenbestand konnte nicht sicher gelesen werden. Es wurde keine Materialbuchung gestartet.';
  return raw||'Noch keine genaue Backend-Meldung verfügbar.';
}
function resultForDisplay(s){
  if(!s)return s;
  const friendly=friendlyServerMessage(s);
  return friendly===s.message?s:{...s,message:friendly,technicalMessage:s.message};
}

function renderSummary(){
  try{
    const r=values();
    $('summary').innerHTML=`<b>${esc(r.article)}</b><br>1) ${esc(r.oldMixWarehouse)} / <b>${esc(r.oldMixBatch||'–')}</b> → <b>${r.oldMixAmountKg.toFixed(3)} kg</b> (vollständiger Oxaion-Tankbestand)<br>2) ${esc(r.addWarehouse)}${r.addStorageBin?' / '+esc(r.addStorageBin):''} / <b>${esc(r.addBatch||'–')}</b> → ${r.addAmountKg.toFixed(3)} kg<br>Neue Mix-Charge: <b>${esc(r.targetBatch||'–')}</b> auf ${esc(r.targetWarehouse)}<br>Personal: ${esc(r.personnelNo)} ${esc(r.personnelName)}`;
  }catch{
    $('summary').innerHTML=`<b>${esc($('article').value.trim())}</b><br>Aktueller Maschinenbestand muss zuerst eindeutig aus oxaion gelesen werden.`;
  }
}
function showResult(ok,title,data){$('result').classList.remove('hidden');$('resultTitle').className='resultTitle '+(ok?'ok':'bad');$('resultTitle').textContent=title;$('documentNo').textContent=data?.documentNo?'Lagerbeleg: '+data.documentNo:'';$('resultJson').textContent=JSON.stringify(data,null,2)}

async function api(url,opts={}){const r=await fetch(url,{headers:{'Content-Type':'application/json',...(opts.headers||{})},...opts});let body=null;try{body=await r.json()}catch{}return {ok:r.ok,status:r.status,body}}

function updateBookState(){
  $('bookBtn').disabled=Boolean(active)||stockLoading||machineStock?.status!=='UNIQUE'||machineStock?.rows?.length!==1;
}

function clearStockFields(){
  $('oldMixBatch').value='';
  $('oldMixAmountKg').value='';
  $('oldMixStorageBin').value='';
  renderSummary();
}

function markStockStale(message='Maschinenbestand geändert bzw. Eingabe geändert – bitte neu aus oxaion lesen.'){
  if(active)return;
  machineStock=null;
  clearStockFields();
  const e=$('stockStatus');e.className='status neutral';e.textContent=message;
  updateBookState();
}

async function refreshMachineStock(){
  if(active){updateBookState();return null}
  const warehouse=$('oldMixWarehouse').value.trim();
  const article=$('article').value.trim();
  const warehouseText=$('oldMixWarehouseText').value.trim();
  const articleText=$('articleText').value.trim();
  const e=$('stockStatus');
  if(!warehouse||!article){markStockStale('Maschine/Lagerort und Artikel sind für die Oxaion-Bestandsabfrage erforderlich.');return null}

  stockLoading=true;machineStock=null;clearStockFields();updateBookState();
  e.className='status neutral';e.textContent=`Oxaion-Bestand wird gelesen: ${warehouse} / ${article} …`;
  $('stockRefreshBtn').disabled=true;
  try{
    const q=new URLSearchParams({warehouse,article,warehouseText,articleText});
    const r=await api('/api/machine-stock?'+q.toString());
    if(!r.ok)throw new Error(r.body?.detail||r.body?.error||'Oxaion-Bestandsabfrage fehlgeschlagen.');
    machineStock=r.body;
    const rows=machineStock?.rows||[];
    if(machineStock?.status==='UNIQUE'&&rows.length===1){
      const row=rows[0];
      $('oldMixBatch').value=row.batch||'';
      $('oldMixAmountKg').value=formatQty(row.quantityKg);
      if(row.articleText&&!$('articleText').value.trim())$('articleText').value=row.articleText;
      e.className='status ok';
      e.textContent=`✓ Eindeutig: ${row.article} · Charge ${row.batch} · ${formatQty(row.quantityKg)} kg auf ${row.warehouse}.`;
    }else{
      clearStockFields();
      e.className='status bad';
      e.textContent='⛔ '+(machineStock?.message||'Maschinenbestand ist nicht eindeutig.');
    }
    renderSummary();
    return machineStock;
  }catch(err){
    machineStock=null;clearStockFields();e.className='status bad';e.textContent='⛔ '+err.message;return null;
  }finally{
    stockLoading=false;$('stockRefreshBtn').disabled=false;updateBookState();
  }
}

async function health(){const e=$('health');e.className='status neutral';e.textContent='Backend wird geprüft …';try{const r=await api('/api/health');if(!r.ok)throw new Error();e.className=r.body.passwordConfigured?'status ok':'status bad';e.textContent=r.body.passwordConfigured?`✓ Backend bereit · ${r.body.serverUrl} · Firma ${r.body.firm} · User ${r.body.user}`:'⛔ Backend bereit, aber Oxaion__Password ist nicht konfiguriert.';return Boolean(r.body.passwordConfigured)}catch{e.className='status bad';e.textContent='✗ Backend nicht erreichbar.';return false}}
async function oxaionHealth(){const e=$('health');e.className='status neutral';e.textContent='Oxaion-Verbindung wird getestet …';const r=await api('/api/health/oxaion');e.className=r.ok?'status ok':'status bad';e.textContent=r.ok?'✓ '+r.body.message:'✗ '+(r.body?.detail||'Oxaion nicht erreichbar.')}

async function handleTerminalRejected(server){
  if(!active)return;
  active.server=server||active.server;
  active.syncStatus='REJECTED';
  await dbSet(active);
  showResult(false,'⛔ Buchung von oxaion abgelehnt – nichts gebucht',resultForDisplay(active.server));
  renderRecovery();
}

async function handleSafePreWriteFailure(server,title){
  showResult(false,title,resultForDisplay(server));
  await dbClear();active=null;renderRecovery();
  await refreshMachineStock().catch(()=>{});
}

async function sendRequest(r){
  active={request:r,syncStatus:'SYNCING',server:null,createdAt:new Date().toISOString()};
  await dbSet(active);renderRecovery();updateBookState();
  try{
    const res=await api('/api/mix',{method:'POST',body:JSON.stringify(r)});

    if(res.body?.stage==='MACHINE_STOCK_VALIDATION'&&(res.body?.status==='CONFLICT'||res.body?.status==='MACHINE_STOCK_UNAVAILABLE')){
      await handleSafePreWriteFailure(res.body,res.body.status==='CONFLICT'?'⛔ Maschinenbestand geändert – nichts gebucht':'⛔ Maschinenbestand nicht prüfbar – nichts gebucht');
      return;
    }

    if(res.status===400){
      await handleSafePreWriteFailure(res.body||{message:'Ungültige Anfrage. Es wurde keine Materialbuchung gestartet.'},'⛔ Anfrage vom Backend abgelehnt – nichts gebucht');
      return;
    }

    active.server=res.body||null;
    active.syncStatus=res.ok&&res.body?.status==='SUCCESS'?'SYNCED':'NEEDS_RECONCILE';
    await dbSet(active);
    if(res.body?.status==='SUCCESS'){
      showResult(true,'✓ Buchung vollständig verifiziert',res.body);await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});
    }else if(res.body?.status==='REJECTED'){
      await handleTerminalRejected(res.body);
    }else{
      showResult(false,'⚠ Buchung nicht eindeutig abgeschlossen',res.body);renderRecovery();
    }
  }catch(e){
    active.syncStatus='UNKNOWN';await dbSet(active);
    showResult(false,'⚠ Verbindung zum Backend unterbrochen',{clientOperationId:r.clientOperationId,message:'Nicht erneut buchen. Status über Recovery prüfen.'});renderRecovery();
  }
}

async function submit(){
  if(active){alert('Es gibt bereits einen offenen Vorgang. Zuerst diesen klären.');return}

  // Bediener sieht den Bestand bereits vorher; unmittelbar vor dem Senden wird er nochmals
  // gelesen. Das Backend führt danach zusätzlich seine eigene Revalidierung vor der Buchung aus.
  await refreshMachineStock();
  if(machineStock?.status!=='UNIQUE'||machineStock?.rows?.length!==1){alert('Der aktuelle Maschinenbestand ist nicht eindeutig bestätigt. Es wird nichts gebucht.');return}

  let r;try{r=values();validate(r)}catch(e){alert(e.message);return}
  const msg=`ECHTE STAGING-BUCHUNG?\n\n${r.article}\n1) KOMPLETTER TANKBESTAND\n${r.oldMixWarehouse} / ${r.oldMixBatch}: ${r.oldMixAmountKg.toFixed(3)} kg\n\n2) NEUE PULVERCHARGE\n${r.addWarehouse} / ${r.addBatch}: ${r.addAmountKg.toFixed(3)} kg\n\n→ ${r.targetWarehouse} / ${r.targetBatch}\n\nPersonal: ${r.personnelNo} ${r.personnelName}`;
  if(!confirm(msg))return;
  await sendRequest(r);
}

async function retryRejected(){
  if(!active||active.server?.status!=='REJECTED')return;
  const previousId=active.request.clientOperationId;
  const r={...active.request,clientOperationId:opId(),retryOfClientOperationId:previousId,simulateFailure:null};
  try{validate(r)}catch(e){alert(e.message);return}
  const msg=`NACH BEHEBUNG ERNEUT VERSUCHEN?\n\nDer vorherige Vorgang wurde von oxaion eindeutig abgelehnt und hat nichts gebucht.\n\nAlle Buchungsdaten werden unverändert übernommen. Das Backend prüft den aktuellen Maschinenbestand vor einem neuen Buchungsversuch erneut.\n\nVorherige clientOperationId:\n${previousId}\n\nNeue clientOperationId:\n${r.clientOperationId}\n\n${r.article}\n1) ${r.oldMixWarehouse} / ${r.oldMixBatch}: ${r.oldMixAmountKg.toFixed(3)} kg\n2) ${r.addWarehouse} / ${r.addBatch}: ${r.addAmountKg.toFixed(3)} kg\n→ ${r.targetWarehouse} / ${r.targetBatch}\n\nNur fortfahren, wenn die gemeldete Ursache in oxaion behoben wurde.`;
  if(!confirm(msg))return;
  await sendRequest(r);
}

async function refreshActiveStatus(){
  if(!active)return;
  const id=active.request.clientOperationId;
  const status=await api('/api/mix/'+encodeURIComponent(id));
  if(!status.ok&&status.status!==422)return;
  active.server=status.body||active.server;
  active.syncStatus=status.body?.status==='SUCCESS'?'SYNCED':status.body?.status==='REJECTED'?'REJECTED':'NEEDS_RECONCILE';
  await dbSet(active);
  if(status.body?.status==='SUCCESS'){
    showResult(true,'✓ Bereits erfolgreich gebucht',status.body);await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});
  }else if(status.body?.status==='REJECTED'){
    await handleTerminalRejected(status.body);return;
  }
  renderRecovery();
}

async function reconcile(){
  if(!active)return;
  const id=active.request.clientOperationId;
  $('reconcileBtn').disabled=true;
  try{
    const status=await api('/api/mix/'+encodeURIComponent(id));
    if(status.ok&&status.body){
      active.server=status.body;
      await dbSet(active);
      if(status.body.status==='SUCCESS'){
        showResult(true,'✓ Bereits erfolgreich gebucht',status.body);await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});return;
      }
      if(status.body.status==='REJECTED'){
        await handleTerminalRejected(status.body);return;
      }
      if(!status.body.documentNo){
        active.syncStatus='NEEDS_RECONCILE';await dbSet(active);
        showResult(false,'⚠ Keine bestätigte Oxaion-Belegnummer',resultForDisplay(status.body));
        renderRecovery();
        return;
      }
    }

    const r=await api('/api/mix/'+encodeURIComponent(id)+'/reconcile',{method:'POST',body:'{}'});
    active.server=r.body||status.body||null;
    active.syncStatus=r.body?.status==='SUCCESS'?'SYNCED':'NEEDS_RECONCILE';
    await dbSet(active);
    if(r.body?.status==='SUCCESS'){
      showResult(true,'✓ Recovery erfolgreich verifiziert',r.body);await dbClear();active=null;await refreshMachineStock().catch(()=>{});
    }else if(r.body?.status==='REJECTED'){
      await handleTerminalRejected(r.body);return;
    }else showResult(false,'⚠ Manuelle Prüfung / weiterer Recovery-Schritt erforderlich',r.body);
    renderRecovery();
  }finally{$('reconcileBtn').disabled=false}
}

function renderRecovery(){
  const c=$('recovery');
  if(!active){c.classList.add('hidden');updateBookState();return}
  c.classList.remove('hidden');updateBookState();
  const s=active.server;const r=active.request;
  const rejected=s?.status==='REJECTED';
  const stage=s?.stage||'–';
  const message=friendlyServerMessage(s);
  $('recoveryTitle').textContent=rejected?'⛔ Abgelehnter Vorgang – nichts gebucht':'⚠ Offener / unklarer Vorgang';
  $('recoveryText').innerHTML=`<b>clientOperationId:</b> ${esc(r.clientOperationId)}<br>${r.retryOfClientOperationId?`<b>Wiederholung von:</b> ${esc(r.retryOfClientOperationId)}<br>`:''}<b>Status:</b> ${esc(s?.status||active.syncStatus)}<br><b>Stage:</b> ${esc(stage)}<br><b>Beleg:</b> ${esc(s?.documentNo||'noch nicht bestätigt')}<br><b>Neue Mix-Charge:</b> ${esc(r.targetBatch)}<br><br><b>Meldung:</b><br>${esc(message)}<br><br>${rejected?'<b>Nach Behebung der Ursache kann mit exakt denselben Buchungsdaten ein neuer, verknüpfter Versuch gestartet werden.</b>':'<b>Nicht erneut buchen.</b> Zuerst den angezeigten Status klären.'}`;
  $('reconcileBtn').classList.toggle('hidden',rejected);
  $('retryRejectedBtn').classList.toggle('hidden',!rejected);
  $('reconcileBtn').textContent=s?.documentNo?'Status in oxaion prüfen':'Backend-Status aktualisieren';
}

async function clearLocal(){
  if(!active)return;
  if(!confirm('Nur die lokale Recovery-Anzeige löschen? Das ändert NICHTS in oxaion. Nur verwenden, wenn der ERP-Zustand manuell geprüft wurde oder ein REJECTED-Vorgang nicht nochmals versucht werden soll.'))return;
  const typed=prompt('Zur Bestätigung clientOperationId eingeben:\n'+active.request.clientOperationId);
  if(typed!==active.request.clientOperationId)return;
  await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});
}

async function init(){
  $('productionDate').value=today();$('bookingDate').value=today();$('targetBatch').value=newBatch();
  document.addEventListener('input',e=>{
    renderSummary();
    if(!active&&['article','articleText','oldMixWarehouse','oldMixWarehouseText'].includes(e.target?.id))markStockStale();
  });
  $('newBatchBtn').onclick=()=>{if(active){alert('Offenen Vorgang zuerst klären.');return}$('targetBatch').value=newBatch();renderSummary()};
  $('bookBtn').onclick=submit;
  $('healthBtn').onclick=oxaionHealth;
  $('stockRefreshBtn').onclick=()=>refreshMachineStock();
  $('reconcileBtn').onclick=reconcile;
  $('retryRejectedBtn').onclick=retryRejected;
  $('clearLocalBtn').onclick=clearLocal;
  active=await dbGet();renderRecovery();renderSummary();
  const ready=await health();
  if(active)refreshActiveStatus().catch(()=>{});
  else if(ready)await refreshMachineStock();
  if('serviceWorker'in navigator)navigator.serviceWorker.register('/sw.js').catch(()=>{});
}
init();
