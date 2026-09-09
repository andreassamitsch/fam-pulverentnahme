'use strict';

// Lightweight STAGING UI diagnostics. This logger deliberately records only technical UI state:
// no passwords, auth tokens, request bodies, connection strings or personnel names/numbers.
(function(){
  const VERSION='20260909-ui-diag-1';
  const STORAGE_KEY='fam-pulver-ui-diag-v1';
  const MAX_ENTRIES=180;
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

  function snapshot(){
    let auth=false,selected=false,authenticated=false,stockStatus='';
    try{auth=typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection()}catch{}
    try{selected=typeof selectedPersonnel!=='undefined'&&Boolean(selectedPersonnel)}catch{}
    try{authenticated=typeof authenticatedPersonnel!=='undefined'&&Boolean(authenticatedPersonnel)}catch{}
    try{stockStatus=String(typeof machineStock!=='undefined'&&machineStock?.status||'')}catch{}
    const processModeScripts=[...document.scripts].map(x=>x.src||'').filter(x=>x.includes('/process-mode.js')).map(x=>x.replace(location.origin,''));
    const ids=['loginStep','processChoiceStep','machineStep','sourcesSection','bookingStep','result','tankOutProcess','fillNewProcess','faConsumptionProcess','inventoryProcess'];
    const visibleIds=ids.filter(id=>visible(el(id)));
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
      visibility:document.visibilityState
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

  async function copy(){
    const value=exportText();
    try{
      if(navigator.clipboard?.writeText){await navigator.clipboard.writeText(value);showCopyStatus('✓ Diagnose wurde in die Zwischenablage kopiert.','ok');return true}
    }catch{}
    const area=ensureManualCopy();area.value=value;area.classList.remove('hidden');area.focus();area.select();
    try{if(document.execCommand('copy')){showCopyStatus('✓ Diagnose wurde in die Zwischenablage kopiert.','ok');return true}}catch{}
    showCopyStatus('Automatisches Kopieren nicht möglich. Text ist markiert und kann manuell kopiert werden.','warn');
    return false;
  }

  function showCopyStatus(message,kind='neutral'){
    const status=el('diagnosticCopyStatus');if(!status)return;
    status.className=`status ${kind}`;status.textContent=message;
  }
  function ensureManualCopy(){
    let area=el('diagnosticManualCopy');if(area)return area;
    area=document.createElement('textarea');area.id='diagnosticManualCopy';area.className='diagnosticManualCopy hidden';area.readOnly=true;
    el('diagnosticFallback')?.appendChild(area);return area;
  }

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
    if(!el('diagnosticDevCopy')){
      const button=document.createElement('button');button.id='diagnosticDevCopy';button.className='secondary compact devOnly';button.type='button';button.textContent='Diagnose kopieren';button.onclick=()=>copy().catch(()=>{});
      const health=el('healthBtn')?.closest('.actions');if(health)health.appendChild(button);
    }
    if(!el('uiDiagnosticStyles')){
      const style=document.createElement('style');style.id='uiDiagnosticStyles';style.textContent='.diagnosticFallback{border:2px solid #d48b16;background:#fff9e8}.diagnosticFallback h2{color:#8a5900}.diagnosticText{font-weight:750;line-height:1.45}.diagnosticManualCopy{width:100%;min-height:220px;margin-top:12px;box-sizing:border-box;font:12px/1.35 monospace}.diagnosticManualCopy.hidden{display:none}.diagnosticFallback.hidden{display:none!important}';document.head.appendChild(style);
    }
  }

  function modalBlocking(){return visible(el('qrScannerModal'))||visible(el('processModal'))||visible(el('bookingResultModal'))||visible(el('bookingConfirmModal'))||visible(el('bookingBusyOverlay'))}
  function hasVisibleProcessContent(state){
    if(state.shell!=='process')return true;
    return state.visibleIds.some(id=>['machineStep','sourcesSection','bookingStep','result','tankOutProcess','fillNewProcess','faConsumptionProcess','inventoryProcess'].includes(id));
  }
  function checkBlank(state){
    const blank=state.shell==='process'&&!modalBlocking()&&!hasVisibleProcessContent(state);
    if(!blank){blankSince=0;blankReported=false;el('diagnosticFallback')?.classList.add('hidden');return}
    if(!blankSince)blankSince=Date.now();
    if(Date.now()-blankSince<600)return;
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
  window.FamDiag={log,snapshot,exportText,copy,version:VERSION};
  window.addEventListener('error',event=>log('WINDOW_ERROR',{message:safeError(event.error||event.message),file:String(event.filename||'').split('/').pop(),line:event.lineno||0,column:event.colno||0}));
  window.addEventListener('unhandledrejection',event=>log('UNHANDLED_REJECTION',{message:safeError(event.reason)}));
  window.addEventListener('online',()=>log('NETWORK_ONLINE',snapshot()));
  window.addEventListener('offline',()=>log('NETWORK_OFFLINE',snapshot()));
  document.addEventListener('visibilitychange',()=>log('VISIBILITY_CHANGE',{visibility:document.visibilityState}));
  document.addEventListener('click',event=>{
    const choice=event.target?.closest?.('.processChoice');
    const action=choice?`process:${choice.dataset?.mode||''}`:event.target?.closest?.('button')?.id||'';
    if(action&&['machineScanBtn','qrScannerScanButton','qrScannerClose','addSourceBtn','bookBtn','processHomeBtn','outTankScan','fillTankScan','faTankScan'].some(x=>action===x)||action.startsWith('process:'))log('CLICK',{action});
  },true);
  log('DIAGNOSTICS_LOADED',{version:VERSION});
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',()=>{ensureUi();poll();setInterval(poll,250)});else{ensureUi();poll();setInterval(poll,250)}
})();
