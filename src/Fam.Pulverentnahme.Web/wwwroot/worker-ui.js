'use strict';

const DEV_MODE_KEY='fam-pulver-dev-mode';
let workerLocationLoadToken=0;
let rejectedScanDialogOpen=false;

function isWorkerAuthenticated(){return typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection()}
function setStepState(id,state){const e=$(id);if(!e)return;e.classList.remove('currentStep','completeStep','lockedStep');if(state)e.classList.add(state)}
function setCurrentAction(element,on=true){if(element)element.classList.toggle('currentAction',Boolean(on))}
function clearCurrentActions(){document.querySelectorAll('.currentAction').forEach(e=>e.classList.remove('currentAction'))}
function setWorkerInstruction(text){const e=$('workerNextInstruction');if(e)e.textContent=text}
function applyDevMode(on){const allowed=window.FamUiConfig?.developerToolsEnabled===true;const enabled=allowed&&Boolean(on);document.body.classList.toggle('dev-mode',enabled);const toggle=$('devModeToggle');if(toggle){toggle.checked=enabled;toggle.disabled=!allowed}try{sessionStorage.setItem(DEV_MODE_KEY,enabled?'1':'0')}catch{}}
function focusWorkerElement(element){if(!element)return;setCurrentAction(element,true);setTimeout(()=>{try{element.scrollIntoView({behavior:'smooth',block:'center'})}catch{}},30)}
function workerDelay(ms){return new Promise(resolve=>setTimeout(resolve,ms))}
function workerSourceKey(p){return `${String(p?.warehouse||'').trim().toUpperCase()}\u001f${String(p?.storageBin||'').trim()}\u001f${String(p?.batch||'').trim()}`}
function workerUsedSourceKeys(excludeCard=null){return new Set(sourceCards().filter(c=>c!==excludeCard&&c._selected).map(c=>workerSourceKey(c._selected)))}
function workerPositionAlreadyUsed(position,excludeCard=null){return workerUsedSourceKeys(excludeCard).has(workerSourceKey(position))}

function workerRecognitionColors(){
  const result=machineStock?.recognitionColors||null;
  const normalize=value=>{
    if(typeof recognitionHex==='function')return recognitionHex(value);
    const hex=String(value||'').trim().replace(/^#/,'').toUpperCase();return /^[0-9A-F]{6}$/.test(hex)?hex:null;
  };
  return{left:normalize(result?.color1?.hex),right:normalize(result?.color2?.hex)};
}
function workerColorSwatchHtml(extraClass=''){
  const colors=workerRecognitionColors();
  const left=colors.left?`background:#${colors.left}`:'';
  const right=colors.right?`background:#${colors.right}`:'';
  return `<div class="scanMiniSwatch ${extraClass}" aria-label="Soll-Erkennungsfarben"><span class="${colors.left?'':'recognitionColorMissing'}" style="${left}"></span><span class="${colors.right?'':'recognitionColorMissing'}" style="${right}"></span></div>`;
}
function workerTargetHtml(){
  const article=$('article')?.value||'',text=$('articleText')?.value||'';
  return `<div class="scanCompareSide scanCompareTarget"><span class="scanCompareLabel">SOLL</span><div class="scanCompareContent">${workerColorSwatchHtml()}<div><b>${esc(article)}</b><span>${esc(text)}</span></div></div></div>`;
}
function workerScannedHtml(scan,state='ok'){
  if(!scan)return `<div class="scanCompareSide scanCompareActual pending"><span class="scanCompareLabel">SCAN</span><div class="scanPending">QR-Code noch nicht erkannt</div></div>`;
  const icon=state==='ok'?'✓':'!';
  return `<div class="scanCompareSide scanCompareActual ${state}"><span class="scanCompareLabel">GESCANNT</span><div class="scanActualValue"><span class="scanActualIcon">${icon}</span><div><b>${esc(scan.article||'Unbekannt')}</b><span>Charge ${esc(scan.batch||'–')}</span></div></div></div>`;
}
function workerComparisonHtml(scan=null,state='pending'){return `${workerTargetHtml()}${workerScannedHtml(scan,state)}`}
function setScannerTargetGuide(scan=null,state='pending'){
  const guide=$('qrTargetGuide');if(!guide)return;
  guide.innerHTML=`<div class="scanComparison scannerComparison">${workerComparisonHtml(scan,state)}</div>`;
  guide.classList.remove('hidden');
}
function hideScannerTargetGuide(){const guide=$('qrTargetGuide');if(guide){guide.classList.add('hidden');guide.innerHTML=''}}

async function recordRejectedScan(reason,scan){
  const audit=$('rejectedScanAudit');if(audit){audit.className='scanAuditNote';audit.textContent='Fehlscan wird dokumentiert …'}
  try{
    const response=await api('/api/scan-events/rejected-charge',{
      method:'POST',
      body:JSON.stringify({
        reason,
        machineWarehouse:$('oldMixWarehouse')?.value||'',
        expectedArticle:$('article')?.value||'',
        scannedArticle:scan?.article||'',
        scannedBatch:scan?.batch||''
      })
    });
    if(!response.ok)throw new Error(response.body?.error||response.body?.message||'Protokollierung fehlgeschlagen.');
    if(audit){audit.className='scanAuditNote ok';audit.textContent='Fehlscan wurde dokumentiert.'}
    return true;
  }catch(error){
    console.error('Rejected scan audit failed',error);
    if(audit){audit.className='scanAuditNote bad';audit.textContent='Fehlscan konnte nicht protokolliert werden. Bitte Produktionsleitung informieren.'}
    return false;
  }
}
function showRejectedScanDialog({title,message,scan,reason}){
  const modal=$('rejectedScanModal'),titleEl=$('rejectedScanTitle'),messageEl=$('rejectedScanMessage'),comparison=$('rejectedScanComparison'),button=$('rejectedScanAcknowledge');
  if(!modal||!button)return Promise.resolve();
  rejectedScanDialogOpen=true;
  titleEl.textContent=title;
  messageEl.innerHTML=message;
  comparison.innerHTML=workerComparisonHtml(scan,'bad');
  modal.classList.remove('hidden');document.body.classList.add('scanModalOpen');
  recordRejectedScan(reason,scan).catch(()=>{});
  return new Promise(resolve=>{
    const close=()=>{button.onclick=null;modal.classList.add('hidden');document.body.classList.remove('scanModalOpen');rejectedScanDialogOpen=false;resolve()};
    button.onclick=close;setTimeout(()=>button.focus(),20);
  });
}
async function rejectWrongArticle(scan){
  const expected=$('article')?.value||'';
  await showRejectedScanDialog({
    title:'Falsches Pulver',
    message:`<b>Diese Charge darf NICHT in den Maschinentank eingefüllt werden.</b><br>Der Tank benötigt Artikel <strong>${esc(expected)}</strong>, gescannt wurde <strong>${esc(scan.article)}</strong>, Charge <strong>${esc(scan.batch)}</strong>.`,
    scan,
    reason:'WRONG_ARTICLE'
  });
  $('sourceScanStatus').className='status bad';$('sourceScanStatus').textContent='⛔ Fehlscan verworfen. Bitte eine Charge des richtigen Artikels scannen.';
}
async function rejectUnavailableCharge(scan,reason){
  const alreadyUsed=reason==='SOURCE_ALREADY_USED';
  await showRejectedScanDialog({
    title:alreadyUsed?'Entnahmeort bereits verwendet':'Charge nicht freigegeben',
    message:alreadyUsed
      ?`<b>Diese gescannte Charge kann aus keiner weiteren Bestandsposition übernommen werden.</b><br>Alle aktuell gefundenen Oxaion-Positionen für Charge <strong>${esc(scan.batch)}</strong> sind in diesem Vorgang bereits gewählt. Falls dieselbe Charge physisch an einem weiteren Lagerplatz liegt, muss dieser zuerst in Oxaion mit positivem Bestand vorhanden sein.`
      :`<b>Diese Charge darf NICHT in den Maschinentank eingefüllt werden.</b><br>Für Artikel <strong>${esc(scan.article)}</strong>, Charge <strong>${esc(scan.batch)}</strong> wurde kein zulässiger positiver Oxaion-Bestand gefunden.`,
    scan,
    reason
  });
  $('sourceScanStatus').className='status bad';$('sourceScanStatus').textContent='⛔ Scan verworfen. Bitte Lagerplatz und Charge prüfen und danach erneut scannen.';
}
async function rejectInvalidQr(){
  await showRejectedScanDialog({
    title:'Ungültiger Chargen-QR',
    message:'<b>Dieser QR-Code darf für die Nachfüllung nicht verwendet werden.</b><br>Erwartet wird ein Chargen-QR im Format <strong>Artikel+++Charge</strong>.',
    scan:null,
    reason:'INVALID_QR_FORMAT'
  });
  $('sourceScanStatus').className='status bad';$('sourceScanStatus').textContent='⛔ Ungültiger QR-Code verworfen. Bitte den Chargen-QR scannen.';
}

function renderWorkerBookSummary(){
  const e=$('workerBookSummary');if(!e)return;
  const machine=$('oldMixWarehouse')?.value,article=$('article')?.value,articleText=$('articleText')?.value,cards=sourceCards();
  if(!machine||!article){e.textContent='Zuerst Maschinentank und Nachfüllcharge erfassen.';return}
  if(!cards.length){e.innerHTML=`<b>${esc(machine)} · ${esc(article)} ${esc(articleText||'')}</b><br>Noch keine Nachfüllcharge gescannt.`;return}
  const lines=cards.map((c,i)=>{
    const scan=c._scan,batch=scan?.batch||'–',amount=String(sf(c,'amountKg')?.value||'').trim(),place=c._selected?`${c._selected.warehouse}${c._selected.storageBin?' / '+c._selected.storageBin:''}`:'Entnahmeort noch offen';
    return `${i+1}. <b>${esc(batch)}</b> · ${amount?esc(amount)+' kg':'Menge fehlt'} <span class="devOnly">· ${esc(place)}</span>`;
  });
  e.innerHTML=`<b>${esc(machine)} · ${esc(article)} ${esc(articleText||'')}</b><br>${lines.join('<br>')}`;
}

function refreshWorkerFlow(){
  clearCurrentActions();renderWorkerBookSummary();
  const authOk=isWorkerAuthenticated();
  const machineReady=machineStock?.status==='UNIQUE'&&machineStock?.rows?.length===1&&Boolean($('article')?.value);
  const cards=typeof sourceCards==='function'?sourceCards():[];
  const unresolvedLocation=cards.find(c=>c._positions?.length>1&&!c._selected);
  const missingQty=cards.find(c=>c._selected&&!sourceReady(c));
  const sourcesReady=cards.length>0&&cards.every(c=>sourceReady(c));
  const busy=Boolean(active)||stockLoading||sourceLoading>0||rejectedScanDialogOpen;

  setStepState('loginStep',authOk?'completeStep':'currentStep');
  setStepState('machineStep',!authOk?'lockedStep':machineReady?'completeStep':'currentStep');
  setStepState('sourcesSection',!machineReady?'lockedStep':sourcesReady?'completeStep':'currentStep');
  setStepState('bookingStep',!sourcesReady?'lockedStep':'currentStep');

  const machineBtn=$('machineScanBtn');if(machineBtn)machineBtn.disabled=busy||!authOk;
  const sourceBtn=$('addSourceBtn');if(sourceBtn){sourceBtn.disabled=busy||!authOk||!machineReady||(cards.length>0&&!sourcesReady);sourceBtn.textContent=cards.length?'Weitere Nachfüllcharge scannen':'Nachfüllcharge scannen'}

  if(!authOk){setWorkerInstruction('1. Mit Personalchip anmelden. Falls NFC nicht möglich ist: Personalnummer und Passwort verwenden.');focusWorkerElement($('nfcScanBtn'));return}
  if(!machineReady){setWorkerInstruction('2. QR-Code am Maschinentank scannen.');focusWorkerElement(machineBtn);return}
  if(unresolvedLocation){setWorkerInstruction('3. Die Charge liegt an mehreren Orten. Tatsächlichen Entnahmeort auswählen.');focusWorkerElement(sf(unresolvedLocation,'position'));return}
  if(missingQty){setWorkerInstruction(`3. Menge für ${missingQty._scan?.batch||'die gescannte Charge'} eingeben.`);focusWorkerElement(sf(missingQty,'amountKg'));return}
  if(cards.length===0){setWorkerInstruction('3. Passende Nachfüllcharge am Lagerplatz finden und deren QR-Code scannen.');focusWorkerElement(sourceBtn);return}
  if(sourcesReady){setWorkerInstruction('4. Mengen prüfen. Bei Bedarf weitere Charge scannen – sonst Buchung starten.');focusWorkerElement($('bookBtn'))}
}
window.refreshWorkerFlow=refreshWorkerFlow;

async function loadWorkerStockLocations(){
  const panel=$('matchingLocationsPanel'),list=$('matchingLocationsList'),status=$('matchingLocationsStatus');
  if(!panel||!list||!status)return;
  const article=$('article')?.value,machine=$('oldMixWarehouse')?.value;
  if(!article||!machine||!sourceWarehouses?.length){panel.classList.add('hidden');return}
  const token=++workerLocationLoadToken;panel.classList.remove('hidden');status.textContent='Passende Lagerplätze werden aus oxaion gelesen …';list.innerHTML='';
  const positions=[];let failures=0;
  for(const w of sourceWarehouses){
    if(token!==workerLocationLoadToken)return;
    try{
      const r=await api('/api/source-stock/positions?'+new URLSearchParams({article,warehouse:w.warehouse}));if(!r.ok){failures++;continue}
      for(const p of Array.isArray(r.body)?r.body:[]){if(p.warehouse!==machine)positions.push(p)}
    }catch{failures++}
  }
  if(token!==workerLocationLoadToken)return;
  const grouped=new Map();
  for(const p of positions){const key=`${p.warehouse}\u001f${p.storageBin||''}`;if(!grouped.has(key))grouped.set(key,{warehouse:p.warehouse,warehouseText:p.warehouseText||p.warehouse,storageBin:p.storageBin||'',positions:[]});grouped.get(key).positions.push(p)}
  const rows=[...grouped.values()];
  if(!rows.length){list.innerHTML='<div class="locationEmpty">Keine passenden positiven Lagerplätze gefunden.</div>';status.textContent=failures?'Lagerplätze konnten nicht vollständig gelesen werden.':'Keine passenden Lagerplätze außerhalb des Tanks gefunden.';return}
  list.innerHTML=rows.map(x=>`<div class="locationHint"><div class="locationMain"><b>${esc(x.warehouseText)}</b>${x.storageBin?`<span>Lagerplatz ${esc(x.storageBin)}</span>`:'<span>ohne Lagerplatz</span>'}</div><div class="devOnly locationDev">${esc(x.warehouse)} · ${x.positions.map(p=>`${esc(p.batch)} (${formatQty(p.quantityKg)} kg)`).join(', ')}</div></div>`).join('');
  status.textContent=failures?`${rows.length} mögliche Entnahmeorte gefunden; einzelne Lagerorte konnten nicht gelesen werden.`:`${rows.length} mögliche Entnahmeorte mit passendem Pulver.`;
}
window.loadWorkerStockLocations=loadWorkerStockLocations;

const workerBaseAddSource=addSource;
addSource=function(scan){
  const card=workerBaseAddSource(scan);
  const header=card.querySelector('.sourceHeader');
  if(header){const comparison=document.createElement('div');comparison.className='scanComparison sourceScanComparison';comparison.innerHTML=workerComparisonHtml(scan,'ok');header.after(comparison)}
  const firstPanel=card.querySelector('.systemPanel');if(firstPanel)firstPanel.classList.add('devOnly');
  const amount=sf(card,'amountKg');if(amount){amount.value='';amount.placeholder='Menge in kg';amount.setAttribute('autocomplete','off')}
  updateBookState();refreshWorkerFlow();return card;
};

const workerBaseApplySourcePosition=applySourcePosition;
applySourcePosition=function(card){
  const raw=sf(card,'position')?.value;
  const candidate=raw!==''&&card?._positions?card._positions[Number(raw)]:null;
  if(candidate&&workerPositionAlreadyUsed(candidate,card)){
    sf(card,'position').value='';card._selected=null;sw(card,'details')?.classList.add('hidden');
    setSourceStatus(card,'⛔ Dieser Entnahmeort wurde bereits für eine andere Nachfüllcharge verwendet. Bitte einen anderen Oxaion-Bestand wählen.','bad');
    updateBookState();renderSummary();refreshWorkerFlow();return;
  }
  workerBaseApplySourcePosition(card);
  if(card?._selected){const amount=sf(card,'amountKg');if(amount&&!String(amount.value||'').trim())setTimeout(()=>{amount.focus();refreshWorkerFlow()},20)}
  refreshWorkerFlow();
};

const workerBaseResetSources=resetSources;
resetSources=function(){workerLocationLoadToken++;workerBaseResetSources();$('matchingLocationsPanel')?.classList.add('hidden');refreshWorkerFlow()};

const workerBaseRefreshSourceWarehouses=refreshSourceWarehouses;
refreshSourceWarehouses=async function(){const ok=await workerBaseRefreshSourceWarehouses();if(ok)loadWorkerStockLocations().catch(()=>{});refreshWorkerFlow();return ok};

const workerBaseRenderSummary=renderSummary;
renderSummary=function(){workerBaseRenderSummary();renderWorkerBookSummary();if(typeof window.refreshWorkerFlow==='function')setTimeout(refreshWorkerFlow,0)};

const workerBaseUpdateBookState=updateBookState;
updateBookState=function(){
  workerBaseUpdateBookState();
  const authOk=isWorkerAuthenticated(),machineReady=machineStock?.status==='UNIQUE'&&machineStock?.rows?.length===1;
  const cards=sourceCards(),sourcesReady=cards.length>0&&cards.every(c=>sourceReady(c));
  if($('machineScanBtn'))$('machineScanBtn').disabled=Boolean(active)||stockLoading||!authOk||rejectedScanDialogOpen;
  if($('addSourceBtn'))$('addSourceBtn').disabled=Boolean(active)||sourceLoading>0||!authOk||!machineReady||rejectedScanDialogOpen||(cards.length>0&&!sourcesReady);
  refreshWorkerFlow();
};

async function workerFindAvailablePositions(scan){
  const expected=$('article').value,machine=$('oldMixWarehouse').value;
  if(!sourceWarehouses.length)throw new Error('Keine zulässigen Quell-Lagerorte verfügbar. Maschinentank bzw. Oxaion-Bestand erneut prüfen.');
  sourceLoading++;updateBookState();
  $('sourceScanStatus').className='status neutral';$('sourceScanStatus').textContent=`Charge ${scan.batch} wird in oxaion gesucht …`;
  try{
    const matches=[];
    for(let i=0;i<sourceWarehouses.length;i++){
      const w=sourceWarehouses[i];
      $('sourceScanStatus').textContent=`Charge ${scan.batch} wird gesucht … ${i+1}/${sourceWarehouses.length}: ${w.warehouse}`;
      const r=await api('/api/source-stock/positions?'+new URLSearchParams({article:expected,warehouse:w.warehouse}));
      if(!r.ok)throw new Error(`${w.warehouse}: ${r.body?.detail||r.body?.error||'Bestandspositionen konnten nicht gelesen werden.'}`);
      for(const p of Array.isArray(r.body)?r.body:[]){if(String(p.batch||'')===scan.batch&&String(p.warehouse||'').toUpperCase()!==String(machine).toUpperCase())matches.push(p)}
    }
    const unique=[...new Map(matches.map(p=>[workerSourceKey(p),p])).values()];
    const used=workerUsedSourceKeys();
    return{all:unique,available:unique.filter(p=>!used.has(workerSourceKey(p)))};
  }finally{sourceLoading--;updateBookState();renderSummary()}
}
function workerPopulateSourceCard(card,positions){
  card._positions=positions;
  const select=sf(card,'position');
  select.innerHTML='<option value="">Entnahmeort auswählen …</option>'+positions.map((p,i)=>`<option value="${i}">${esc(p.warehouse)}${p.storageBin?' · Platz '+esc(p.storageBin):' · ohne Lagerplatz'} · ${formatQty(p.quantityKg)} kg</option>`).join('');
  if(positions.length===1){select.value='0';applySourcePosition(card);return}
  sw(card,'position').classList.remove('hidden');
  setSourceStatus(card,`${positions.length} noch nicht verwendete Entnahmeorte für Charge ${card._scan.batch} gefunden. Bitte den tatsächlichen Lagerort${positions.some(p=>p.storageBin)?' / Lagerplatz':''} bestätigen.`,'neutral');
  updateBookState();renderSummary();
}
async function workerScanPreview(raw){
  try{
    const scan=parseChargeQr(raw),expected=$('article')?.value||'';
    setScannerTargetGuide(scan,scan.article.toUpperCase()===expected.toUpperCase()?'ok':'bad');
  }catch{setScannerTargetGuide({article:'Ungültiger QR',batch:'–'},'bad')}
  await workerDelay(450);
}
async function workerScanChargeQr(){
  if(active||stockLoading||sourceLoading||rejectedScanDialogOpen)return;
  if(machineStock?.status!=='UNIQUE'||!$('article').value){$('sourceScanStatus').className='status bad';$('sourceScanStatus').textContent='⛔ Zuerst Maschinentank scannen und eindeutigen Tankbestand laden.';return}
  setScannerTargetGuide();
  try{
    const raw=await scanQrCode({title:'Nachfüllcharge scannen',help:'Soll-Farbe beachten. Chargen-QR scannen: Artikel+++Charge',onDetected:workerScanPreview});
    let scan;
    try{scan=parseChargeQr(raw)}catch{await rejectInvalidQr();return}
    const expected=$('article').value;
    if(scan.article.toUpperCase()!==expected.toUpperCase()){await rejectWrongArticle(scan);return}

    let resolved;
    try{resolved=await workerFindAvailablePositions(scan)}catch(error){$('sourceScanStatus').className='status bad';$('sourceScanStatus').textContent='⛔ '+(error?.message||'Bestandspositionen konnten nicht gelesen werden.');return}
    if(resolved.all.length===0){await rejectUnavailableCharge(scan,'CHARGE_NOT_FOUND');return}
    if(resolved.available.length===0){await rejectUnavailableCharge(scan,'SOURCE_ALREADY_USED');return}

    const card=addSource(scan);
    workerPopulateSourceCard(card,resolved.available);
    $('sourceScanStatus').className='status ok';
    $('sourceScanStatus').textContent=resolved.all.length>resolved.available.length
      ?`✓ Charge ${scan.batch} erneut erkannt. Bereits verwendete Position(en) wurden ausgeblendet.`
      :`✓ Charge ${scan.batch} erkannt. Entnahmeort aus aktuellem Oxaion-Bestand ermittelt.`;
  }catch(error){
    if(error?.name==='AbortError')return;
    $('sourceScanStatus').className='status bad';$('sourceScanStatus').textContent='⛔ '+(error?.message||'Chargen-QR konnte nicht verarbeitet werden.');
  }finally{hideScannerTargetGuide();updateBookState();refreshWorkerFlow()}
}
window.workerScanChargeQr=workerScanChargeQr;

function workerResolutionInstruction(ok,data){
  if(ok)return 'Buchung wurde eindeutig bestätigt. Der Vorgang ist abgeschlossen.';
  const status=String(data?.status||'').toUpperCase();
  if(status==='UNCERTAIN'||status==='MANUAL_REVIEW_REQUIRED')return 'Nicht erneut buchen. Produktionsleitung informieren und den Status in oxaion prüfen.';
  if(status==='AUTH_REQUIRED'||status==='AUTH_CONFLICT')return 'Erneut anmelden und den Vorgang danach nochmals prüfen.';
  if(status==='REJECTED')return 'Die angezeigte Ursache beheben. Erst danach bewusst einen neuen Versuch starten.';
  if(status==='CONFLICT')return 'Physische Situation und aktuellen oxaion-Bestand prüfen. Danach den betroffenen Schritt erneut durchführen.';
  return 'Hinweis oben beachten. Wenn die Ursache nicht eindeutig behebbar ist, Produktionsleitung informieren.';
}
const workerBaseShowResult=showResult;
showResult=function(ok,title,data){workerBaseShowResult(ok,title,data);const message=$('workerResultMessage'),action=$('workerResultAction');if(message)message.textContent=data?.message||data?.detail||friendly(data);if(action)action.textContent=workerResolutionInstruction(ok,data);const result=$('result');if(result){result.classList.toggle('workerSuccess',Boolean(ok));result.classList.toggle('workerFailure',!ok)}setTimeout(()=>result?.scrollIntoView({behavior:'smooth',block:'center'}),20)};

function initWorkerUi(){
  const toggle=$('devModeToggle');
  applyDevMode(false);
  const applyConfiguredDevMode=()=>{let initial=false;if(window.FamUiConfig?.developerToolsEnabled===true){try{initial=sessionStorage.getItem(DEV_MODE_KEY)==='1'}catch{}}applyDevMode(initial)};
  if(window.FamUiConfigReady?.then)window.FamUiConfigReady.then(applyConfiguredDevMode);else applyConfiguredDevMode();
  toggle?.addEventListener('change',()=>applyDevMode(toggle.checked));
  const manual=$('manualLoginFallback');if(manual&&(!window.isSecureContext||!('NDEFReader'in window)))manual.open=true;
  const sourceButton=$('addSourceBtn');if(sourceButton)sourceButton.onclick=workerScanChargeQr;
  try{scanChargeQr=workerScanChargeQr}catch{}window.scanChargeQr=workerScanChargeQr;
  document.addEventListener('input',e=>{if(e.target?.matches?.('[data-field="amountKg"],#personnelPassword,#personnelNo'))refreshWorkerFlow()});
  document.addEventListener('change',e=>{if(e.target?.matches?.('[data-field="position"]'))refreshWorkerFlow()});
  refreshWorkerFlow();
}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',initWorkerUi);else initWorkerUi();
