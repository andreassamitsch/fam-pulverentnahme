'use strict';

const DEV_MODE_KEY='fam-pulver-dev-mode';
let workerLocationLoadToken=0;

function isWorkerAuthenticated(){return typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection()}
function setStepState(id,state){const e=$(id);if(!e)return;e.classList.remove('currentStep','completeStep','lockedStep');if(state)e.classList.add(state)}
function setCurrentAction(element,on=true){if(element)element.classList.toggle('currentAction',Boolean(on))}
function clearCurrentActions(){document.querySelectorAll('.currentAction').forEach(e=>e.classList.remove('currentAction'))}
function setWorkerInstruction(text){const e=$('workerNextInstruction');if(e)e.textContent=text}
function applyDevMode(on){document.body.classList.toggle('dev-mode',Boolean(on));const toggle=$('devModeToggle');if(toggle)toggle.checked=Boolean(on);try{sessionStorage.setItem(DEV_MODE_KEY,on?'1':'0')}catch{}}
function focusWorkerElement(element){if(!element)return;setCurrentAction(element,true);setTimeout(()=>{try{element.scrollIntoView({behavior:'smooth',block:'center'})}catch{}},30)}

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
  const busy=Boolean(active)||stockLoading||sourceLoading>0;

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
  const firstPanel=card.querySelector('.systemPanel');if(firstPanel?.children?.[0])firstPanel.children[0].classList.add('devOnly');
  const amount=sf(card,'amountKg');if(amount){amount.value='';amount.placeholder='Menge in kg';amount.setAttribute('autocomplete','off')}
  updateBookState();refreshWorkerFlow();return card;
};

const workerBaseApplySourcePosition=applySourcePosition;
applySourcePosition=function(card){workerBaseApplySourcePosition(card);if(card?._selected){const amount=sf(card,'amountKg');if(amount&&!String(amount.value||'').trim())setTimeout(()=>{amount.focus();refreshWorkerFlow()},20)}refreshWorkerFlow()};

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
  if($('machineScanBtn'))$('machineScanBtn').disabled=Boolean(active)||stockLoading||!authOk;
  if($('addSourceBtn'))$('addSourceBtn').disabled=Boolean(active)||sourceLoading>0||!authOk||!machineReady||(cards.length>0&&!sourcesReady);
  refreshWorkerFlow();
};

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
  const toggle=$('devModeToggle');let initial=false;try{initial=sessionStorage.getItem(DEV_MODE_KEY)==='1'}catch{}
  applyDevMode(initial);toggle?.addEventListener('change',()=>applyDevMode(toggle.checked));
  const manual=$('manualLoginFallback');if(manual&&(!window.isSecureContext||!('NDEFReader'in window)))manual.open=true;
  document.addEventListener('input',e=>{if(e.target?.matches?.('[data-field="amountKg"],#personnelPassword,#personnelNo'))refreshWorkerFlow()});
  document.addEventListener('change',e=>{if(e.target?.matches?.('[data-field="position"]'))refreshWorkerFlow()});
  refreshWorkerFlow();
}
if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',initWorkerUi);else initWorkerUi();
