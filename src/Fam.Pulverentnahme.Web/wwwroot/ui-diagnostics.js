'use strict';

// Lightweight STAGING UI diagnostics. This logger deliberately records only technical UI state:
// no passwords, auth tokens, request bodies, connection strings or personnel names/numbers.
(function(){
  const VERSION='20260910-ui-diag-2';
  const STORAGE_KEY='fam-pulver-ui-diag-v1';
  const FIRST_START_RELOAD_KEY='fam-pulver-first-controlled-start';
  const MAX_ENTRIES=220;
  let entries=[];
  let lastStateHash='';
  let blankSince=0;
  let blankReported=false;

  const el=id=>document.getElementById(id);
  const text=id=>String(el(id)?.textContent||'').trim().replace(/\s+/g,' ').slice(0,220);
  const visible=node=>Boolean(node&&getComputedStyle(node).display!=='none'&&getComputedStyle(node).visibility!=='hidden');
  const safeError=value=>String(value?.message||value||'Unbekannter Fehler').replace(/(password|token|authorization|cookie|connectionstring)\s*[:=]\s*[^\s,;]+/ig,'$1=<redacted>').slice(0,700);
  const now=()=>new Date().toISOString();

  function load(){
    try{
      const stored=JSON.parse(sessionStorage.getItem(STORAGE_KEY)||'[]');
      if(Array.isArray(stored))entries=stored.slice(-MAX_ENTRIES);
    }catch{entries=[]}
  }
  function save(){
    try{sessionStorage.setItem(STORAGE_KEY,JSON.stringify(entries.slice(-MAX_ENTRIES)))}catch{}
  }
  function log(event,details={}){
    entries.push({ts:now(),event:String(event||'EVENT'),details});
    if(entries.length>MAX_ENTRIES)entries=entries.slice(-MAX_ENTRIES);
    save();
  }

  function refreshFunctionLabel(){
    try{
      if(typeof window.refreshWorkerFlow!=='function')return 'none';
      const source=Function.prototype.toString.call(window.refreshWorkerFlow);
      let hash=0;for(let i=0;i<source.length;i++)hash=((hash<<5)-hash+source.charCodeAt(i))|0;
      return `${window.refreshWorkerFlow.name||'anonymous'}#${Math.abs(hash)}`;
    }catch{return 'unknown'}
  }

  function firstControlledReload(){try{return sessionStorage.getItem(FIRST_START_RELOAD_KEY)==='1'}catch{return false}}
  function navigationType(){try{return performance.getEntriesByType('navigation')?.[0]?.type||''}catch{return ''}}

  function snapshot(){
    let auth=false,selected=false,authenticated=false,stockStatus='';
    try{auth=typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection()}catch{}
    try{selected=typeof selectedPersonnel!=='undefined'&&Boolean(selectedPersonnel)}catch{}
    try{authenticated=typeof authenticatedPersonnel!=='undefined'&&Boolean(authenticatedPersonnel)}catch{}
    try{stockStatus=String(typeof machineStock!=='undefined'&&machineStock?.status||'')}catch{}
    const processModeScripts=[...document.scripts].map(x=>x.src||'').filter(x=>x.includes('/process-mode.js')).map(x=>x.replace(location.origin,''));
    const diagnosticScripts=[...document.scripts].map(x=>x.src||'').filter(x=>/ui-diagnostics|replenish-router-guard|process-mode-focus-fix|process-shell/.test(x)).map(x=>x.replace(location.origin,''));
    const ids=['loginStep','processChoiceStep','machineStep','sourcesSection','bookingStep','result','tankOutProcess','fillNewProcess','faConsumptionProcess','inventoryProcess'];
    const visibleIds=ids.filter(id=>visible(el(id)));
    const swController=navigator.serviceWorker?.controller||null;
    return {
      version:VERSION,
      shell:document.body.classList.contains('processShellProcess')?'process':document.body.classList.contains('processShellHome')?'home':'unknown',
      bodyClasses:document.body.className,
      activeMode:document.querySelector('.processChoice.active')?.dataset?.mode||'',
      rememberedMode:(()=>{try{return sessionStorage.getItem('fam-pulver-last-process-mode')||''}catch{return ''}})(),
      instruction:text('workerNextInstruction'),
      auth,selected,authenticated,
      refreshWorkerFlow:refreshFunctionLabel(),
      processModeScripts,
      diagnosticScripts,
      machineWarehouse:String(el('oldMixWarehouse')?.value||''),
      machineStockStatus:stockStatus,
      article:String(el('article')?.value||''),
      stockLoading:typeof stockLoading!=='undefined'?Boolean(stockLoading):null,
      sourceLoading:typeof sourceLoading!=='undefined'?Number(sourceLoading):null,
      visibleIds,
      scannerOpen:visible(el('qrScannerModal')),
      scanModalOpen:document.body.classList.contains('scanModalOpen'),
      processModalOpen:visible(el('processModal')),
      bookingBusy:visible(el('bookingBusyOverlay')),
      online:navigator.onLine,
      visibility:document.visibilityState,
      documentReadyState:document.readyState,
      navigationType:navigationType(),
      serviceWorkerControlled:Boolean(swController),
      serviceWorkerController:swController?.scriptURL?swController.scriptURL.replace(location.origin,''):'',
      firstStartWasUncontrolled:Boolean(window.FamFirstStart?.wasUncontrolled),
      firstControlledReload:firstControlledReload()
    };
  }

  function exportText(){
    const head=[
      `FAM Pulver Diagnose ${VERSION}`,
      `Export: ${now()}`,
      `URL: ${location.origin}${location.pathname}`,
      `UserAgent: ${navigator.userAgent}`,
      `Aktueller Zustand: ${JSON.stringify(snapshot())}`,
      '--- Ereignisse ---'
    ];
    return head.concat(entries.map(x=>`${x.ts} | ${x.event} | ${JSON.stringify(x.details)}`)).join('\n');
  }

  function updateManualAreas(value){
    for(const id of ['diagnosticManualCopy','diagnosticModalText']){
      const area=el(id);if(area)area.value=value;
    }
  }

  async function copy(){
    const value=exportText();updateManualAreas(value);
    try{
      if(navigator.clipboard?.writeText){await navigator.clipboard.writeText(value);showCopyStatus('✓ Diagnose wurde in die Zwischenablage kopiert.','ok');return true}
    }catch{}
    const area=ensureManualCopy();area.value=value;area.classList.remove('hidden');area.focus();area.select();
    try{if(document.execCommand('copy')){showCopyStatus('✓ Diagnose wurde in die Zwischenablage kopiert.','ok');return true}}catch{}
    showCopyStatus('Automatisches Kopieren nicht möglich. Text ist markiert und kann manuell kopiert werden.','warn');
    return false;
  }

  function showCopyStatus(message,kind='neutral'){
    for(const id of ['diagnosticCopyStatus','diagnosticModalStatus']){
      const status=el(id);if(!status)continue;
      status.className=`status ${kind}`;status.textContent=message;
    }
  }
  function ensureManualCopy(){
    let area=el('diagnosticManualCopy');if(area)return area;
    area=document.createElement('textarea');area.id='diagnosticManualCopy';area.className='diagnosticManualCopy hidden';area.readOnly=true;
    el('diagnosticFallback')?.appendChild(area);return area;
  }

  function openDiagnostic(){
    ensureUi();
    const modal=el('diagnosticModal');if(!modal)return;
    log('DIAGNOSTIC_OPENED',snapshot());
    const area=el('diagnosticModalText');if(area)area.value=exportText();
    modal.classList.remove('hidden');
  }
  function closeDiagnostic(){el('diagnosticModal')?.classList.add('hidden')}

  function ensureUi(){
    const main=document.querySelector('main');if(!main)return;
    if(!el('diagnosticFallback')){
      const card=document.createElement('section');
      card.id='diagnosticFallback';card.className='card diagnosticFallback hidden';
      card.innerHTML='<h2>Anzeigeproblem erkannt</h2><div class="diagnosticText">Die App hat einen inkonsistenten UI-Zustand erkannt. Es wurde dadurch keine Buchung ausgelöst. Bitte Diagnose kopieren, falls die Anzeige nicht automatisch wiederhergestellt wird.</div><div class="actions"><button id="diagnosticRecoverBtn" class="primary" type="button">Anzeige wiederherstellen</button><button id="diagnosticCopyBtn" class="secondary" type="button">Diagnose kopieren</button></div><div id="diagnosticCopyStatus" class="status neutral">Das Diagnoseprotokoll enthält keine Passwörter oder Zugangsdaten.</div>';
      main.insertBefore(card,main.firstChild);
      el('diagnosticCopyBtn').onclick=()=>copy().catch(()=>{});
      el('diagnosticRecoverBtn').onclick=()=>{
        log('MANUAL_RECOVERY_REQUESTED',snapshot());
        if(window.FamReplenishGuard?.recover)window.FamReplenishGuard.recover('manual-diagnostic-card');
        else if(typeof window.refreshWorkerFlow==='function')window.refreshWorkerFlow();
      };
    }
    if(!el('diagnosticHeaderBtn')){
      const header=document.querySelector('.workerHeader');
      if(header){
        const button=document.createElement('button');button.id='diagnosticHeaderBtn';button.className='secondary compact diagnosticHeaderBtn';button.type='button';button.textContent='Diagnose';button.title='Diagnoseprotokoll anzeigen und kopieren';button.onclick=openDiagnostic;
        const dev=header.querySelector('.devSwitch');if(dev)header.insertBefore(button,dev);else header.appendChild(button);
      }
    }
    if(!el('diagnosticDevCopy')){
      const button=document.createElement('button');button.id='diagnosticDevCopy';button.className='secondary compact devOnly';button.type='button';button.textContent='Diagnose kopieren';button.onclick=()=>copy().catch(()=>{});
      const health=el('healthBtn')?.closest('.actions');if(health)health.appendChild(button);
    }
    if(!el('diagnosticModal')){
      const modal=document.createElement('div');modal.id='diagnosticModal';modal.className='diagnosticModal hidden';modal.innerHTML='<div class="diagnosticModalDialog"><div class="diagnosticModalHeader"><h2>Diagnose</h2><button id="diagnosticModalClose" class="secondary compact" type="button">Schließen</button></div><div class="diagnosticText">Diesen Text nach Auftreten des Fehlers kopieren und in den Projektchat einfügen.</div><textarea id="diagnosticModalText" class="diagnosticModalText" readonly></textarea><div class="actions"><button id="diagnosticModalCopy" class="primary" type="button">Diagnose kopieren</button></div><div id="diagnosticModalStatus" class="status neutral">Keine Passwörter, Tokens, Connection-Strings oder Mitarbeiterdaten werden protokolliert.</div></div>';document.body.appendChild(modal);
      el('diagnosticModalClose').onclick=closeDiagnostic;el('diagnosticModalCopy').onclick=()=>copy().catch(()=>{});
    }
    if(!el('uiDiagnosticStyles')){
      const style=document.createElement('style');style.id='uiDiagnosticStyles';style.textContent='.diagnosticHeaderBtn{white-space:nowrap;min-height:38px;padding:6px 9px;font-size:12px}.diagnosticFallback{border:2px solid #d48b16;background:#fff9e8}.diagnosticFallback h2{color:#8a5900}.diagnosticText{font-weight:750;line-height:1.45}.diagnosticManualCopy{width:100%;min-height:220px;margin-top:12px;box-sizing:border-box;font:12px/1.35 monospace}.diagnosticManualCopy.hidden{display:none}.diagnosticFallback.hidden{display:none!important}.diagnosticModal{position:fixed;z-index:12000;inset:0;background:rgba(4,18,28,.78);display:flex;align-items:center;justify-content:center;padding:12px}.diagnosticModal.hidden{display:none!important}.diagnosticModalDialog{width:min(760px,100%);max-height:92vh;overflow:auto;background:#fff;border-radius:16px;padding:16px}.diagnosticModalHeader{display:flex;align-items:center;gap:10px;justify-content:space-between}.diagnosticModalHeader h2{margin:0}.diagnosticModalText{width:100%;height:48vh;min-height:260px;box-sizing:border-box;margin-top:12px;font:11px/1.35 monospace;white-space:pre;overflow:auto}@media(max-width:620px){.diagnosticHeaderBtn{padding:5px 7px;font-size:11px}}';document.head.appendChild(style);
    }
  }

  function modalBlocking(){return visible(el('qrScannerModal'))||visible(el('processModal'))||visible(el('bookingResultModal'))||visible(el('bookingConfirmModal'))||visible(el('bookingBusyOverlay'))||visible(el('diagnosticModal'))}
  function hasVisibleProcessContent(state){
    return state.visibleIds.some(id=>['machineStep','sourcesSection','bookingStep','result','tankOutProcess','fillNewProcess','faConsumptionProcess','inventoryProcess'].includes(id));
  }
  function replenishmentExpected(state){return state.activeMode==='replenish'||(state.shell==='process'&&state.rememberedMode==='replenish')}
  function checkBlank(state){
    const processExpected=state.shell==='process'||replenishmentExpected(state);
    const blank=processExpected&&!modalBlocking()&&!hasVisibleProcessContent(state);
    if(!blank){blankSince=0;blankReported=false;el('diagnosticFallback')?.classList.add('hidden');return}
    if(!blankSince)blankSince=Date.now();
    if(Date.now()-blankSince<500)return;
    if(!blankReported){blankReported=true;log('BLANK_PROCESS_DETECTED',state)}
    el('diagnosticFallback')?.classList.remove('hidden');
  }

  function poll(){
    ensureUi();
    const state=snapshot();
    const hash=JSON.stringify(state);
    if(hash!==lastStateHash){lastStateHash=hash;log('STATE',state)}
    checkBlank(state);
  }

  load();
  window.FamDiag={log,snapshot,exportText,copy,open:openDiagnostic,version:VERSION};
  window.addEventListener('error',event=>log('WINDOW_ERROR',{message:safeError(event.error||event.message),file:String(event.filename||'').split('/').pop(),line:event.lineno||0,column:event.colno||0}));
  window.addEventListener('unhandledrejection',event=>log('UNHANDLED_REJECTION',{message:safeError(event.reason)}));
  window.addEventListener('online',()=>log('NETWORK_ONLINE',snapshot()));
  window.addEventListener('offline',()=>log('NETWORK_OFFLINE',snapshot()));
  navigator.serviceWorker?.addEventListener?.('controllerchange',()=>log('SERVICE_WORKER_CONTROLLER_CHANGE',snapshot()));
  document.addEventListener('visibilitychange',()=>log('VISIBILITY_CHANGE',{visibility:document.visibilityState}));
  document.addEventListener('click',event=>{
    const choice=event.target?.closest?.('.processChoice');
    const action=choice?`process:${choice.dataset?.mode||''}`:event.target?.closest?.('button')?.id||'';
    if((action&&['machineScanBtn','qrScannerScanButton','qrScannerClose','addSourceBtn','bookBtn','processHomeBtn','outTankScan','fillTankScan','faTankScan','diagnosticHeaderBtn'].some(x=>action===x))||action.startsWith('process:'))log('CLICK',{action});
  },true);
  log('DIAGNOSTICS_LOADED',{version:VERSION,firstControlledReload:firstControlledReload(),serviceWorkerControlled:Boolean(navigator.serviceWorker?.controller)});
  if(firstControlledReload())log('FIRST_CONTROLLED_START_AFTER_INSTALL',snapshot());
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',()=>{ensureUi();poll();setInterval(poll,200)});else{ensureUi();poll();setInterval(poll,200)}
})();
