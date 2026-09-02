'use strict';

const $=id=>document.getElementById(id);
const DB_NAME='fam-pulver-staging';
const STORE='operations';
const ACTIVE_KEY='active';
let active=null;
let machineStock=null;
let stockLoading=false;
let sourceWarehouses=[];
let sourceLoading=0;

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

function sourceCards(){return [...document.querySelectorAll('.replenishment-source')]}
function sourceField(card,name){return card.querySelector(`[data-field="${name}"]`)}
function requestSources(r){
  if(Array.isArray(r?.additionalSources)&&r.additionalSources.length)return r.additionalSources;
  return [{warehouse:r?.addWarehouse||'',warehouseText:r?.addWarehouseText||'',storageBin:r?.addStorageBin||'',batch:r?.addBatch||'',amountKg:Number(r?.addAmountKg||0)}];
}
function sourceKey(p){return `${p?.warehouse||''}\u001f${p?.storageBin||''}\u001f${p?.batch||''}`}
function sourceReady(card){
  if(!card._selected)return false;
  try{const amount=parseQty(sourceField(card,'amountKg').value);return amount>0&&amount<=Number(card._selected.quantityKg)+0.0005}catch{return false}
}
function allSourcesReady(){const cards=sourceCards();return cards.length>0&&cards.every(sourceReady)}
function readAdditionalSources(){
  return sourceCards().map((card,i)=>{
    if(!card._selected)throw new Error(`Nachfüllcharge ${i+1}: Bitte eine Oxaion-Bestandsposition auswählen.`);
    const amount=parseQty(sourceField(card,'amountKg').value);
    if(amount<=0)throw new Error(`Nachfüllcharge ${i+1}: Menge muss > 0 sein.`);
    if(amount>Number(card._selected.quantityKg)+0.0005)throw new Error(`Nachfüllcharge ${i+1}: Maximal ${formatQty(card._selected.quantityKg)} kg verfügbar.`);
    return {
      warehouse:card._selected.warehouse,
      warehouseText:card._selected.warehouseText||card._selected.warehouse,
      storageBin:card._selected.storageBin||'',
      batch:card._selected.batch,
      amountKg:amount
    };
  });
}
function renumberSources(){
  const cards=sourceCards();
  cards.forEach((card,i)=>{
    card.querySelector('.sourceTitle').textContent=`Nachfüllcharge ${i+1} · Oxaion-Position ${i+2}`;
    const remove=card.querySelector('.removeSource');
    remove.classList.toggle('hidden',cards.length===1);
    remove.disabled=Boolean(active);
  });
}
function resetSourceSelection(card,message='Bitte zuerst Lagerort und danach die Bestandsposition auswählen.'){
  card._positions=[];card._selected=null;
  sourceField(card,'position').innerHTML='<option value="">Bestandsposition auswählen …</option>';
  sourceField(card,'position').disabled=true;
  sourceField(card,'warehouseText').value='';sourceField(card,'storageBin').value='';sourceField(card,'batch').value='';sourceField(card,'available').value='';
  const s=sourceField(card,'status');s.className='status neutral sourceStockStatus';s.textContent=message;
  updateBookState();renderSummary();
}
function populateWarehouseSelect(card,preferred=''){
  const select=sourceField(card,'warehouse');
  select.innerHTML='<option value="">Lagerort auswählen …</option>'+sourceWarehouses.map(w=>`<option value="${esc(w.warehouse)}">${esc(w.warehouse)} · ${esc(w.warehouseText)} · ${formatQty(w.quantityKg)} kg</option>`).join('');
  if(preferred&&sourceWarehouses.some(w=>w.warehouse===preferred))select.value=preferred;
  resetSourceSelection(card,sourceWarehouses.length?'Bitte Lagerort auswählen.':'Für diesen Artikel wurde kein positiver Quellbestand gefunden.');
}
async function loadSourcePositions(card,preserveKey=''){
  const warehouse=sourceField(card,'warehouse').value;
  if(!warehouse){resetSourceSelection(card,'Bitte Lagerort auswählen.');return false}
  sourceLoading++;updateBookState();
  const status=sourceField(card,'status');status.className='status neutral sourceStockStatus';status.textContent=`Oxaion-Bestandspositionen für ${warehouse} werden gelesen …`;
  sourceField(card,'position').disabled=true;
  try{
    const article=$('article').value.trim();
    const r=await api('/api/source-stock/positions?'+new URLSearchParams({article,warehouse}).toString());
    if(!r.ok)throw new Error(r.body?.detail||r.body?.error||'Oxaion-Bestandspositionen konnten nicht gelesen werden.');
    const positions=Array.isArray(r.body)?r.body:[];card._positions=positions;card._selected=null;
    const posSelect=sourceField(card,'position');
    posSelect.innerHTML='<option value="">Bestandsposition auswählen …</option>'+positions.map((p,i)=>{
      const place=p.storageBin?`Platz ${p.storageBin}`:'ohne Lagerplatz';
      return `<option value="${i}">${esc(place)} · Charge ${esc(p.batch)} · ${formatQty(p.quantityKg)} kg</option>`;
    }).join('');
    posSelect.disabled=positions.length===0;
    let match=-1;if(preserveKey)match=positions.findIndex(p=>sourceKey(p)===preserveKey);
    if(match<0&&positions.length===1)match=0;
    if(match>=0){posSelect.value=String(match);applySourcePosition(card)}
    else{
      sourceField(card,'warehouseText').value=sourceWarehouses.find(w=>w.warehouse===warehouse)?.warehouseText||warehouse;
      sourceField(card,'storageBin').value='';sourceField(card,'batch').value='';sourceField(card,'available').value='';
      status.className=positions.length?'status neutral sourceStockStatus':'status bad sourceStockStatus';
      status.textContent=positions.length?`${positions.length} positive Bestandsposition(en) gefunden. Bitte auswählen.`:'⛔ Kein positiver Bestand für diesen Lagerort gefunden.';
    }
    return match>=0;
  }catch(err){
    card._positions=[];card._selected=null;sourceField(card,'position').innerHTML='<option value="">Bestandsposition nicht verfügbar</option>';
    status.className='status bad sourceStockStatus';status.textContent='⛔ '+err.message;return false;
  }finally{sourceLoading--;updateBookState();renderSummary()}
}
function applySourcePosition(card){
  const raw=sourceField(card,'position').value;
  if(raw===''){card._selected=null;sourceField(card,'storageBin').value='';sourceField(card,'batch').value='';sourceField(card,'available').value='';updateBookState();renderSummary();return}
  const idx=Number(raw);
  if(!Number.isInteger(idx)||idx<0||idx>=card._positions.length){card._selected=null;sourceField(card,'storageBin').value='';sourceField(card,'batch').value='';sourceField(card,'available').value='';updateBookState();renderSummary();return}
  const p=card._positions[idx];card._selected=p;
  sourceField(card,'warehouseText').value=p.warehouseText||p.warehouse;
  sourceField(card,'storageBin').value=p.storageBin||'';
  sourceField(card,'batch').value=p.batch||'';
  sourceField(card,'available').value=formatQty(p.quantityKg);
  const amount=sourceField(card,'amountKg');amount.max=String(p.quantityKg);
  const status=sourceField(card,'status');status.className='status ok sourceStockStatus';status.textContent=`✓ Oxaion: ${p.warehouse}${p.storageBin?' / '+p.storageBin:''} · Charge ${p.batch} · ${formatQty(p.quantityKg)} kg verfügbar.`;
  updateBookState();renderSummary();
}
function addSource(data={}){
  const card=document.createElement('div');card.className='sourceCard replenishment-source';card._positions=[];card._selected=null;
  card.innerHTML=`<div class="sourceHeader"><b class="sourceTitle"></b><button type="button" class="secondary removeSource">Entfernen</button></div><div class="grid3"><label>Quell-Lagerort<select data-field="warehouse"></select></label><label>Oxaion-Bestandsposition<select data-field="position" disabled><option value="">Bestandsposition auswählen …</option></select></label><label>Lagerort-Bezeichnung<input data-field="warehouseText" readonly></label><label>Interner Lagerplatz<input data-field="storageBin" readonly placeholder="kein Lagerplatz"></label><label>Charge<input data-field="batch" readonly></label><label>Verfügbar kg<input data-field="available" readonly></label><label>Einfüllmenge kg<input data-field="amountKg" inputmode="decimal" value="0,001"></label></div><div data-field="status" class="status neutral sourceStockStatus">Oxaion-Quelle noch nicht ausgewählt.</div>`;
  if(data.amountKg!==undefined)sourceField(card,'amountKg').value=formatQty(data.amountKg);
  sourceField(card,'warehouse').onchange=()=>loadSourcePositions(card);
  sourceField(card,'position').onchange=()=>applySourcePosition(card);
  sourceField(card,'amountKg').oninput=()=>{const s=sourceField(card,'status');if(card._selected){const a=Number(String(sourceField(card,'amountKg').value).replace(',','.'));if(Number.isFinite(a)&&a>Number(card._selected.quantityKg)+0.0005){s.className='status bad sourceStockStatus';s.textContent=`⛔ Nur ${formatQty(card._selected.quantityKg)} kg verfügbar.`}else{s.className='status ok sourceStockStatus';s.textContent=`✓ Oxaion: ${card._selected.warehouse}${card._selected.storageBin?' / '+card._selected.storageBin:''} · Charge ${card._selected.batch} · ${formatQty(card._selected.quantityKg)} kg verfügbar.`}}updateBookState();renderSummary()};
  card.querySelector('.removeSource').onclick=()=>{if(active){alert('Offenen Vorgang zuerst klären.');return}if(sourceCards().length<=1)return;card.remove();renumberSources();renderSummary();updateBookState()};
  $('additionalSources').appendChild(card);populateWarehouseSelect(card,data.warehouse||'');renumberSources();renderSummary();
  if(data.warehouse)loadSourcePositions(card,sourceKey(data)).catch(()=>{});
}
function addAnotherSource(){if(active){alert('Offenen Vorgang zuerst klären.');return}addSource();const cards=sourceCards();sourceField(cards[cards.length-1],'warehouse').focus()}
async function refreshSourceWarehouses(){
  if(active)return false;
  const article=$('article').value.trim();if(!article){sourceWarehouses=[];sourceCards().forEach(c=>populateWarehouseSelect(c));return false}
  sourceLoading++;updateBookState();
  try{
    const r=await api('/api/source-stock/warehouses?'+new URLSearchParams({article}).toString());if(!r.ok)throw new Error(r.body?.detail||r.body?.error||'Quell-Lagerorte konnten nicht gelesen werden.');
    sourceWarehouses=Array.isArray(r.body)?r.body:[];
    for(const card of sourceCards()){const preferred=card._selected?.warehouse||sourceField(card,'warehouse').value;const preserve=card._selected?sourceKey(card._selected):'';populateWarehouseSelect(card,preferred);if(preferred&&sourceField(card,'warehouse').value)await loadSourcePositions(card,preserve)}
    return true;
  }catch(err){sourceWarehouses=[];sourceCards().forEach(c=>{populateWarehouseSelect(c);const s=sourceField(c,'status');s.className='status bad sourceStockStatus';s.textContent='⛔ '+err.message});return false}
  finally{sourceLoading--;updateBookState();renderSummary()}
}
async function refreshSelectedSources(){
  let ok=true;
  for(const card of sourceCards()){
    const selected=card._selected;if(!selected){ok=false;continue}
    const warehouse=selected.warehouse;sourceField(card,'warehouse').value=warehouse;
    const found=await loadSourcePositions(card,sourceKey(selected));if(!found)ok=false;
  }
  return ok&&allSourcesReady();
}

function values(){
  const additionalSources=readAdditionalSources();if(!additionalSources.length)throw new Error('Mindestens eine Nachfüllcharge ist erforderlich.');const first=additionalSources[0];
  return {clientOperationId:active?.request?.clientOperationId||opId(),personnelNo:$('personnelNo').value.trim(),personnelName:$('personnelName').value.trim(),article:$('article').value.trim(),articleText:$('articleText').value.trim(),oldMixWarehouse:$('oldMixWarehouse').value.trim(),oldMixWarehouseText:$('oldMixWarehouseText').value.trim(),oldMixStorageBin:$('oldMixStorageBin').value.trim(),oldMixBatch:$('oldMixBatch').value.trim(),oldMixAmountKg:parseQty($('oldMixAmountKg').value),addWarehouse:first.warehouse,addWarehouseText:first.warehouseText,addStorageBin:first.storageBin,addBatch:first.batch,addAmountKg:first.amountKg,additionalSources,targetWarehouse:$('targetWarehouse').value.trim(),targetWarehouseText:$('targetWarehouseText').value.trim(),targetStorageBin:$('targetStorageBin').value.trim(),targetBatch:$('targetBatch').value.trim(),productionDate:$('productionDate').value,bookingDate:$('bookingDate').value,bookingText:$('bookingText').value.trim(),simulateFailure:$('simulateFailure').value||null,retryOfClientOperationId:null};
}
function validate(r){const req=['clientOperationId','personnelNo','article','oldMixWarehouse','oldMixBatch','targetWarehouse','targetBatch','productionDate','bookingDate'];const m=req.filter(k=>!r[k]);if(m.length)throw new Error('Pflichtfelder fehlen: '+m.join(', '));if(r.oldMixAmountKg<=0)throw new Error('Tankbestand muss > 0 sein.');const sources=requestSources(r);if(!sources.length)throw new Error('Mindestens eine Nachfüllcharge ist erforderlich.');sources.forEach((s,i)=>{if(!s.warehouse||!s.batch)throw new Error(`Nachfüllcharge ${i+1}: Oxaion-Bestandsposition fehlt.`);if(Number(s.amountKg)<=0)throw new Error(`Nachfüllcharge ${i+1}: Menge muss > 0 sein.`)});const keys=new Set();sources.forEach((s,i)=>{const k=sourceKey(s);if(keys.has(k))throw new Error(`Nachfüllcharge ${i+1}: Dieselbe Oxaion-Bestandsposition wurde bereits ausgewählt.`);keys.add(k)});if(r.targetBatch===r.oldMixBatch)throw new Error('Neue Mix-Charge muss von der bisherigen Mix-Charge verschieden sein.');const same=sources.findIndex(s=>s.batch===r.targetBatch);if(same>=0)throw new Error(`Neue Mix-Charge muss von Nachfüllcharge ${same+1} verschieden sein.`)}

function friendlyServerMessage(s){const raw=s?.message||'';if(s?.status==='REJECTED'&&/U180500/.test(raw)&&/KOBGDT/.test(raw))return 'Keine Buchung durchgeführt. Die für das Buchungsdatum benötigte Lagerbuchhaltungsperiode ist in oxaion noch nicht eröffnet. Bitte Periode in oxaion öffnen lassen bzw. das zulässige Buchungsdatum klären. Danach kann derselbe Buchungsauftrag bewusst mit einer neuen clientOperationId erneut versucht werden.';if(s?.status==='REJECTED')return `Keine Buchung durchgeführt. oxaion hat den Vorgang eindeutig abgelehnt. Ursache: ${raw||'siehe technische Meldung'}`;if(s?.status==='CONFLICT'&&s?.stage==='SOURCE_STOCK_VALIDATION')return raw||'Eine Nachfüllquelle stimmt nicht mehr mit dem aktuellen Oxaion-Bestand überein. Es wurde keine Materialbuchung gestartet.';if(s?.status==='SOURCE_STOCK_UNAVAILABLE')return raw||'Die aktuellen Nachfüllbestände konnten nicht sicher gelesen werden. Es wurde keine Materialbuchung gestartet.';if(s?.status==='CONFLICT')return raw||'Der aktuelle Maschinenbestand stimmt nicht mehr mit dem vorbereiteten Vorgang überein. Es wurde keine Materialbuchung gestartet.';if(s?.status==='MACHINE_STOCK_UNAVAILABLE')return raw||'Der aktuelle Maschinenbestand konnte nicht sicher gelesen werden. Es wurde keine Materialbuchung gestartet.';return raw||'Noch keine genaue Backend-Meldung verfügbar.'}
function resultForDisplay(s){if(!s)return s;const friendly=friendlyServerMessage(s);return friendly===s.message?s:{...s,message:friendly,technicalMessage:s.message}}
function sourceSummaryLines(r){return requestSources(r).map((s,i)=>`${i+2}) ${esc(s.warehouse)}${s.storageBin?' / '+esc(s.storageBin):''} / <b>${esc(s.batch||'–')}</b> → ${Number(s.amountKg).toFixed(3)} kg`).join('<br>')}
function sourceConfirmLines(r){return requestSources(r).map((s,i)=>`${i+2}) ${s.warehouse}${s.storageBin?' / '+s.storageBin:''} / ${s.batch}: ${Number(s.amountKg).toFixed(3)} kg`).join('\n')}
function renderSummary(){try{const r=values();$('summary').innerHTML=`<b>${esc(r.article)}</b><br>1) ${esc(r.oldMixWarehouse)} / <b>${esc(r.oldMixBatch||'–')}</b> → <b>${r.oldMixAmountKg.toFixed(3)} kg</b> (vollständiger Oxaion-Tankbestand)<br>${sourceSummaryLines(r)}<br>Neue Mix-Charge: <b>${esc(r.targetBatch||'–')}</b> auf ${esc(r.targetWarehouse)}<br>Personal: ${esc(r.personnelNo)} ${esc(r.personnelName)}`}catch{$('summary').innerHTML=`<b>${esc($('article').value.trim())}</b><br>Maschinenbestand und alle Nachfüllquellen müssen eindeutig aus oxaion ausgewählt sein.`}}
function showResult(ok,title,data){$('result').classList.remove('hidden');$('resultTitle').className='resultTitle '+(ok?'ok':'bad');$('resultTitle').textContent=title;$('documentNo').textContent=data?.documentNo?'Lagerbeleg: '+data.documentNo:'';$('resultJson').textContent=JSON.stringify(data,null,2)}
async function api(url,opts={}){const r=await fetch(url,{headers:{'Content-Type':'application/json',...(opts.headers||{})},...opts});let body=null;try{body=await r.json()}catch{}return {ok:r.ok,status:r.status,body}}

function updateBookState(){$('bookBtn').disabled=Boolean(active)||stockLoading||sourceLoading>0||machineStock?.status!=='UNIQUE'||machineStock?.rows?.length!==1||!allSourcesReady();$('addSourceBtn').disabled=Boolean(active)||sourceLoading>0;sourceCards().forEach(card=>card.querySelector('.removeSource').disabled=Boolean(active))}
function clearStockFields(){$('oldMixBatch').value='';$('oldMixAmountKg').value='';$('oldMixStorageBin').value='';renderSummary()}
function markStockStale(message='Maschinenbestand geändert bzw. Eingabe geändert – bitte neu aus oxaion lesen.'){if(active)return;machineStock=null;clearStockFields();const e=$('stockStatus');e.className='status neutral';e.textContent=message;updateBookState()}
async function refreshMachineStock(){if(active){updateBookState();return null}const warehouse=$('oldMixWarehouse').value.trim();const article=$('article').value.trim();const warehouseText=$('oldMixWarehouseText').value.trim();const articleText=$('articleText').value.trim();const e=$('stockStatus');if(!warehouse||!article){markStockStale('Maschine/Lagerort und Artikel sind für die Oxaion-Bestandsabfrage erforderlich.');return null}stockLoading=true;machineStock=null;clearStockFields();updateBookState();e.className='status neutral';e.textContent=`Oxaion-Bestand wird gelesen: ${warehouse} / ${article} …`;$('stockRefreshBtn').disabled=true;try{const q=new URLSearchParams({warehouse,article,warehouseText,articleText});const r=await api('/api/machine-stock?'+q.toString());if(!r.ok)throw new Error(r.body?.detail||r.body?.error||'Oxaion-Bestandsabfrage fehlgeschlagen.');machineStock=r.body;const rows=machineStock?.rows||[];if(machineStock?.status==='UNIQUE'&&rows.length===1){const row=rows[0];$('oldMixBatch').value=row.batch||'';$('oldMixAmountKg').value=formatQty(row.quantityKg);if(row.articleText&&!$('articleText').value.trim())$('articleText').value=row.articleText;e.className='status ok';e.textContent=`✓ Eindeutig: ${row.article} · Charge ${row.batch} · ${formatQty(row.quantityKg)} kg auf ${row.warehouse}.`}else{clearStockFields();e.className='status bad';e.textContent='⛔ '+(machineStock?.message||'Maschinenbestand ist nicht eindeutig.')}renderSummary();return machineStock}catch(err){machineStock=null;clearStockFields();e.className='status bad';e.textContent='⛔ '+err.message;return null}finally{stockLoading=false;$('stockRefreshBtn').disabled=false;updateBookState()}}

async function health(){const e=$('health');e.className='status neutral';e.textContent='Backend wird geprüft …';try{const r=await api('/api/health');if(!r.ok)throw new Error();e.className=r.body.passwordConfigured?'status ok':'status bad';e.textContent=r.body.passwordConfigured?`✓ Backend bereit · ${r.body.serverUrl} · Firma ${r.body.firm} · User ${r.body.user}`:'⛔ Backend bereit, aber Oxaion__Password ist nicht konfiguriert.';return Boolean(r.body.passwordConfigured)}catch{e.className='status bad';e.textContent='✗ Backend nicht erreichbar.';return false}}
async function oxaionHealth(){const e=$('health');e.className='status neutral';e.textContent='Oxaion-Verbindung wird getestet …';const r=await api('/api/health/oxaion');e.className=r.ok?'status ok':'status bad';e.textContent=r.ok?'✓ '+r.body.message:'✗ '+(r.body?.detail||'Oxaion nicht erreichbar.')}
async function handleTerminalRejected(server){if(!active)return;active.server=server||active.server;active.syncStatus='REJECTED';await dbSet(active);showResult(false,'⛔ Buchung von oxaion abgelehnt – nichts gebucht',resultForDisplay(active.server));renderRecovery()}
async function handleSafePreWriteFailure(server,title){showResult(false,title,resultForDisplay(server));await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});await refreshSourceWarehouses().catch(()=>{})}
async function sendRequest(r){active={request:r,syncStatus:'SYNCING',server:null,createdAt:new Date().toISOString()};await dbSet(active);renderRecovery();updateBookState();try{const res=await api('/api/mix',{method:'POST',body:JSON.stringify(r)});if((res.body?.stage==='MACHINE_STOCK_VALIDATION'||res.body?.stage==='SOURCE_STOCK_VALIDATION')&&(res.body?.status==='CONFLICT'||res.body?.status==='MACHINE_STOCK_UNAVAILABLE'||res.body?.status==='SOURCE_STOCK_UNAVAILABLE')){const source=res.body.stage==='SOURCE_STOCK_VALIDATION';const title=source?(res.body.status==='CONFLICT'?'⛔ Nachfüllbestand geändert – nichts gebucht':'⛔ Nachfüllbestand nicht prüfbar – nichts gebucht'):(res.body.status==='CONFLICT'?'⛔ Maschinenbestand geändert – nichts gebucht':'⛔ Maschinenbestand nicht prüfbar – nichts gebucht');await handleSafePreWriteFailure(res.body,title);return}if(res.status===400){await handleSafePreWriteFailure(res.body||{message:'Ungültige Anfrage. Es wurde keine Materialbuchung gestartet.'},'⛔ Anfrage vom Backend abgelehnt – nichts gebucht');return}active.server=res.body||null;active.syncStatus=res.ok&&res.body?.status==='SUCCESS'?'SYNCED':'NEEDS_RECONCILE';await dbSet(active);if(res.body?.status==='SUCCESS'){showResult(true,'✓ Buchung vollständig verifiziert',res.body);await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});await refreshSourceWarehouses().catch(()=>{})}else if(res.body?.status==='REJECTED'){await handleTerminalRejected(res.body)}else{showResult(false,'⚠ Buchung nicht eindeutig abgeschlossen',res.body);renderRecovery()}}catch(e){active.syncStatus='UNKNOWN';await dbSet(active);showResult(false,'⚠ Verbindung zum Backend unterbrochen',{clientOperationId:r.clientOperationId,message:'Nicht erneut buchen. Status über Recovery prüfen.'});renderRecovery()}}
async function submit(){if(active){alert('Es gibt bereits einen offenen Vorgang. Zuerst diesen klären.');return}await refreshMachineStock();if(machineStock?.status!=='UNIQUE'||machineStock?.rows?.length!==1){alert('Der aktuelle Maschinenbestand ist nicht eindeutig bestätigt. Es wird nichts gebucht.');return}const sourcesOk=await refreshSelectedSources();if(!sourcesOk){alert('Mindestens eine Nachfüllquelle ist nicht mehr eindeutig oder die Menge übersteigt den aktuellen Oxaion-Bestand. Es wird nichts gebucht.');return}let r;try{r=values();validate(r)}catch(e){alert(e.message);return}const msg=`ECHTE STAGING-BUCHUNG?\n\n${r.article}\n1) KOMPLETTER TANKBESTAND\n${r.oldMixWarehouse} / ${r.oldMixBatch}: ${r.oldMixAmountKg.toFixed(3)} kg\n\nNACHFÜLLCHARGEN (aus oxaion)\n${sourceConfirmLines(r)}\n\n→ ${r.targetWarehouse} / ${r.targetBatch}\n\nOxaion-Positionen: ${1+requestSources(r).length}\nErwartete LM/LN-Bewegungen: ${2*(1+requestSources(r).length)}\n\nPersonal: ${r.personnelNo} ${r.personnelName}`;if(!confirm(msg))return;await sendRequest(r)}
async function retryRejected(){if(!active||active.server?.status!=='REJECTED')return;const previousId=active.request.clientOperationId;const r={...active.request,clientOperationId:opId(),retryOfClientOperationId:previousId,simulateFailure:null};try{validate(r)}catch(e){alert(e.message);return}const msg=`NACH BEHEBUNG ERNEUT VERSUCHEN?\n\nDer vorherige Vorgang wurde von oxaion eindeutig abgelehnt. Alle Buchungsdaten werden unverändert übernommen und serverseitig gegen die aktuellen Maschinen- und Nachfüllbestände geprüft.\n\nVorherige clientOperationId:\n${previousId}\n\nNeue clientOperationId:\n${r.clientOperationId}\n\n${r.article}\n1) ${r.oldMixWarehouse} / ${r.oldMixBatch}: ${Number(r.oldMixAmountKg).toFixed(3)} kg\n${sourceConfirmLines(r)}\n→ ${r.targetWarehouse} / ${r.targetBatch}\n\nNur fortfahren, wenn die gemeldete Ursache in oxaion behoben wurde.`;if(!confirm(msg))return;await sendRequest(r)}
async function refreshActiveStatus(){if(!active)return;const id=active.request.clientOperationId;const status=await api('/api/mix/'+encodeURIComponent(id));if(!status.ok&&status.status!==422)return;active.server=status.body||active.server;active.syncStatus=status.body?.status==='SUCCESS'?'SYNCED':status.body?.status==='REJECTED'?'REJECTED':'NEEDS_RECONCILE';await dbSet(active);if(status.body?.status==='SUCCESS'){showResult(true,'✓ Bereits erfolgreich gebucht',status.body);await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});await refreshSourceWarehouses().catch(()=>{})}else if(status.body?.status==='REJECTED'){await handleTerminalRejected(status.body);return}renderRecovery()}
async function reconcile(){if(!active)return;const id=active.request.clientOperationId;$('reconcileBtn').disabled=true;try{const status=await api('/api/mix/'+encodeURIComponent(id));if(status.ok&&status.body){active.server=status.body;await dbSet(active);if(status.body.status==='SUCCESS'){showResult(true,'✓ Bereits erfolgreich gebucht',status.body);await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});await refreshSourceWarehouses().catch(()=>{});return}if(status.body.status==='REJECTED'){await handleTerminalRejected(status.body);return}if(!status.body.documentNo){active.syncStatus='NEEDS_RECONCILE';await dbSet(active);showResult(false,'⚠ Keine bestätigte Oxaion-Belegnummer',resultForDisplay(status.body));renderRecovery();return}}const r=await api('/api/mix/'+encodeURIComponent(id)+'/reconcile',{method:'POST',body:'{}'});active.server=r.body||status.body||null;active.syncStatus=r.body?.status==='SUCCESS'?'SYNCED':'NEEDS_RECONCILE';await dbSet(active);if(r.body?.status==='SUCCESS'){showResult(true,'✓ Recovery erfolgreich verifiziert',r.body);await dbClear();active=null;await refreshMachineStock().catch(()=>{});await refreshSourceWarehouses().catch(()=>{})}else if(r.body?.status==='REJECTED'){await handleTerminalRejected(r.body);return}else showResult(false,'⚠ Manuelle Prüfung / weiterer Recovery-Schritt erforderlich',r.body);renderRecovery()}finally{$('reconcileBtn').disabled=false}}
function renderRecovery(){const c=$('recovery');if(!active){c.classList.add('hidden');updateBookState();return}c.classList.remove('hidden');updateBookState();const s=active.server;const r=active.request;const rejected=s?.status==='REJECTED';const stage=s?.stage||'–';const message=friendlyServerMessage(s);const sourceCount=requestSources(r).length;$('recoveryTitle').textContent=rejected?'⛔ Abgelehnter Vorgang – nichts gebucht':'⚠ Offener / unklarer Vorgang';$('recoveryText').innerHTML=`<b>clientOperationId:</b> ${esc(r.clientOperationId)}<br>${r.retryOfClientOperationId?`<b>Wiederholung von:</b> ${esc(r.retryOfClientOperationId)}<br>`:''}<b>Status:</b> ${esc(s?.status||active.syncStatus)}<br><b>Stage:</b> ${esc(stage)}<br><b>Beleg:</b> ${esc(s?.documentNo||'noch nicht bestätigt')}<br><b>Neue Mix-Charge:</b> ${esc(r.targetBatch)}<br><b>Nachfüllchargen:</b> ${sourceCount}<br><br><b>Meldung:</b><br>${esc(message)}<br><br>${rejected?'<b>Nach Behebung der Ursache kann mit exakt denselben Buchungsdaten ein neuer, verknüpfter Versuch gestartet werden.</b>':'<b>Nicht erneut buchen.</b> Zuerst den angezeigten Status klären.'}`;$('reconcileBtn').classList.toggle('hidden',rejected);$('retryRejectedBtn').classList.toggle('hidden',!rejected);$('reconcileBtn').textContent=s?.documentNo?'Status in oxaion prüfen':'Backend-Status aktualisieren'}
async function clearLocal(){if(!active)return;if(!confirm('Nur die lokale Recovery-Anzeige löschen? Das ändert NICHTS in oxaion. Nur verwenden, wenn der ERP-Zustand manuell geprüft wurde oder ein REJECTED-Vorgang nicht nochmals versucht werden soll.'))return;const typed=prompt('Zur Bestätigung clientOperationId eingeben:\n'+active.request.clientOperationId);if(typed!==active.request.clientOperationId)return;await dbClear();active=null;renderRecovery();await refreshMachineStock().catch(()=>{});await refreshSourceWarehouses().catch(()=>{})}

async function init(){
  $('productionDate').value=today();$('bookingDate').value=today();$('targetBatch').value=newBatch();addSource();
  document.addEventListener('input',e=>{renderSummary();if(!active&&['article','articleText','oldMixWarehouse','oldMixWarehouseText'].includes(e.target?.id))markStockStale();if(!active&&e.target?.id==='article'){sourceWarehouses=[];sourceCards().forEach(c=>populateWarehouseSelect(c));}});
  $('article').addEventListener('change',()=>{if(!active)refreshSourceWarehouses().catch(()=>{})});
  $('newBatchBtn').onclick=()=>{if(active){alert('Offenen Vorgang zuerst klären.');return}$('targetBatch').value=newBatch();renderSummary()};
  $('addSourceBtn').onclick=addAnotherSource;$('bookBtn').onclick=submit;$('healthBtn').onclick=oxaionHealth;$('stockRefreshBtn').onclick=()=>refreshMachineStock();$('reconcileBtn').onclick=reconcile;$('retryRejectedBtn').onclick=retryRejected;$('clearLocalBtn').onclick=clearLocal;
  active=await dbGet();renderRecovery();renumberSources();renderSummary();const ready=await health();if(active)refreshActiveStatus().catch(()=>{});else if(ready){await Promise.all([refreshMachineStock(),refreshSourceWarehouses()])}if('serviceWorker'in navigator)navigator.serviceWorker.register('/sw.js').catch(()=>{});
}
init();
