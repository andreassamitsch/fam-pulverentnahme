'use strict';

const $=id=>document.getElementById(id);
const DB_NAME='fam-pulver-staging';
const STORE='operations';
const ACTIVE_KEY='active';
let active=null;

function pad(n){return String(n).padStart(2,'0')}
function today(){const d=new Date();return `${d.getFullYear()}-${pad(d.getMonth()+1)}-${pad(d.getDate())}`}
function parseQty(v){const n=Number(String(v).replace(',','.'));if(!Number.isFinite(n))throw new Error(`Ungültige Menge: ${v}`);return n}
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
  productionDate:$('productionDate').value,bookingDate:$('bookingDate').value,bookingText:$('bookingText').value.trim(),simulateFailure:$('simulateFailure').value||null
}}

function validate(r){const req=['clientOperationId','personnelNo','article','oldMixWarehouse','oldMixBatch','addWarehouse','addBatch','targetWarehouse','targetBatch','productionDate','bookingDate'];const m=req.filter(k=>!r[k]);if(m.length)throw new Error('Pflichtfelder fehlen: '+m.join(', '));if(r.oldMixAmountKg<=0||r.addAmountKg<=0)throw new Error('Mengen müssen > 0 sein.');if(r.targetBatch===r.oldMixBatch||r.targetBatch===r.addBatch)throw new Error('Neue Mix-Charge muss von beiden Quellchargen verschieden sein.')}

function renderSummary(){try{const r=values();$('summary').innerHTML=`<b>${esc(r.article)}</b><br>1) ${esc(r.oldMixWarehouse)} / <b>${esc(r.oldMixBatch||'–')}</b> → ${r.oldMixAmountKg.toFixed(3)} kg<br>2) ${esc(r.addWarehouse)}${r.addStorageBin?' / '+esc(r.addStorageBin):''} / <b>${esc(r.addBatch||'–')}</b> → ${r.addAmountKg.toFixed(3)} kg<br>Neue Mix-Charge: <b>${esc(r.targetBatch||'–')}</b> auf ${esc(r.targetWarehouse)}<br>Personal: ${esc(r.personnelNo)} ${esc(r.personnelName)}`}catch{}}
function showResult(ok,title,data){$('result').classList.remove('hidden');$('resultTitle').className='resultTitle '+(ok?'ok':'bad');$('resultTitle').textContent=title;$('documentNo').textContent=data?.documentNo?'Lagerbeleg: '+data.documentNo:'';$('resultJson').textContent=JSON.stringify(data,null,2)}

async function api(url,opts={}){const r=await fetch(url,{headers:{'Content-Type':'application/json',...(opts.headers||{})},...opts});let body=null;try{body=await r.json()}catch{}return {ok:r.ok,status:r.status,body}}

async function health(){const e=$('health');e.className='status neutral';e.textContent='Backend wird geprüft …';try{const r=await api('/api/health');if(!r.ok)throw new Error();e.className=r.body.passwordConfigured?'status ok':'status bad';e.textContent=r.body.passwordConfigured?`✓ Backend bereit · ${r.body.serverUrl} · Firma ${r.body.firm} · User ${r.body.user}`:'⛔ Backend bereit, aber Oxaion__Password ist nicht konfiguriert.'}catch{e.className='status bad';e.textContent='✗ Backend nicht erreichbar.'}}
async function oxaionHealth(){const e=$('health');e.className='status neutral';e.textContent='Oxaion-Verbindung wird getestet …';const r=await api('/api/health/oxaion');e.className=r.ok?'status ok':'status bad';e.textContent=r.ok?'✓ '+r.body.message:'✗ '+(r.body?.detail||'Oxaion nicht erreichbar.')}

async function submit(){
  if(active){alert('Es gibt bereits einen offenen Vorgang. Zuerst diesen klären.');return}
  let r;try{r=values();validate(r)}catch(e){alert(e.message);return}
  const msg=`ECHTE STAGING-BUCHUNG?\n\n${r.article}\n1) ${r.oldMixWarehouse} / ${r.oldMixBatch}: ${r.oldMixAmountKg.toFixed(3)} kg\n2) ${r.addWarehouse} / ${r.addBatch}: ${r.addAmountKg.toFixed(3)} kg\n→ ${r.targetWarehouse} / ${r.targetBatch}\n\nPersonal: ${r.personnelNo} ${r.personnelName}`;
  if(!confirm(msg))return;
  active={request:r,syncStatus:'SYNCING',server:null,createdAt:new Date().toISOString()};await dbSet(active);renderRecovery();$('bookBtn').disabled=true;
  try{
    const res=await api('/api/mix',{method:'POST',body:JSON.stringify(r)});
    active.server=res.body||null;
    active.syncStatus=res.ok&&res.body?.status==='SUCCESS'?'SYNCED':'NEEDS_RECONCILE';
    await dbSet(active);
    if(res.body?.status==='SUCCESS'){
      showResult(true,'✓ Buchung vollständig verifiziert',res.body);await dbClear();active=null;
    }else{
      showResult(false,'⚠ Buchung nicht eindeutig abgeschlossen',res.body);renderRecovery();
    }
  }catch(e){
    active.syncStatus='UNKNOWN';await dbSet(active);
    showResult(false,'⚠ Verbindung zum Backend unterbrochen',{clientOperationId:r.clientOperationId,message:'Nicht erneut buchen. Status über Recovery prüfen.'});renderRecovery();
  }
}

async function refreshActiveStatus(){
  if(!active)return;
  const id=active.request.clientOperationId;
  const status=await api('/api/mix/'+encodeURIComponent(id));
  if(!status.ok)return;
  active.server=status.body||active.server;
  active.syncStatus=status.body?.status==='SUCCESS'?'SYNCED':'NEEDS_RECONCILE';
  await dbSet(active);
  if(status.body?.status==='SUCCESS'){
    showResult(true,'✓ Bereits erfolgreich gebucht',status.body);await dbClear();active=null;
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
        showResult(true,'✓ Bereits erfolgreich gebucht',status.body);await dbClear();active=null;renderRecovery();return;
      }
      if(!status.body.documentNo){
        active.syncStatus='NEEDS_RECONCILE';await dbSet(active);
        showResult(false,'⚠ Keine bestätigte Oxaion-Belegnummer',status.body);
        renderRecovery();
        return;
      }
    }

    const r=await api('/api/mix/'+encodeURIComponent(id)+'/reconcile',{method:'POST',body:'{}'});
    active.server=r.body||status.body||null;
    active.syncStatus=r.body?.status==='SUCCESS'?'SYNCED':'NEEDS_RECONCILE';
    await dbSet(active);
    if(r.body?.status==='SUCCESS'){
      showResult(true,'✓ Recovery erfolgreich verifiziert',r.body);await dbClear();active=null;
    }else showResult(false,'⚠ Manuelle Prüfung / weiterer Recovery-Schritt erforderlich',r.body);
    renderRecovery();
  }finally{$('reconcileBtn').disabled=false}
}

function renderRecovery(){
  const c=$('recovery');
  if(!active){c.classList.add('hidden');$('bookBtn').disabled=false;return}
  c.classList.remove('hidden');$('bookBtn').disabled=true;
  const s=active.server;const r=active.request;
  const stage=s?.stage||'–';
  const message=s?.message||'Noch keine genaue Backend-Meldung verfügbar.';
  $('recoveryText').innerHTML=`<b>clientOperationId:</b> ${esc(r.clientOperationId)}<br><b>Status:</b> ${esc(s?.status||active.syncStatus)}<br><b>Stage:</b> ${esc(stage)}<br><b>Beleg:</b> ${esc(s?.documentNo||'noch nicht bestätigt')}<br><b>Neue Mix-Charge:</b> ${esc(r.targetBatch)}<br><br><b>Meldung:</b><br>${esc(message)}<br><br><b>Nicht erneut buchen.</b> Zuerst den angezeigten Status klären.`;
  $('reconcileBtn').textContent=s?.documentNo?'Status in oxaion prüfen':'Backend-Status aktualisieren';
}

async function clearLocal(){if(!active)return;if(!confirm('Nur die lokale Recovery-Anzeige löschen? Das ändert NICHTS in oxaion. Nur verwenden, wenn der ERP-Zustand manuell geprüft wurde.'))return;const typed=prompt('Zur Bestätigung clientOperationId eingeben:\n'+active.request.clientOperationId);if(typed!==active.request.clientOperationId)return;await dbClear();active=null;renderRecovery()}

async function init(){
  $('productionDate').value=today();$('bookingDate').value=today();$('targetBatch').value=newBatch();
  document.addEventListener('input',renderSummary);$('newBatchBtn').onclick=()=>{if(active){alert('Offenen Vorgang zuerst klären.');return}$('targetBatch').value=newBatch();renderSummary()};
  $('bookBtn').onclick=submit;$('healthBtn').onclick=oxaionHealth;$('reconcileBtn').onclick=reconcile;$('clearLocalBtn').onclick=clearLocal;
  active=await dbGet();renderRecovery();renderSummary();await health();
  if(active)refreshActiveStatus().catch(()=>{});
  if('serviceWorker'in navigator)navigator.serviceWorker.register('/sw.js').catch(()=>{});
}
init();
