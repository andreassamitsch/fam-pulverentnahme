'use strict';

// Guards the legacy replenishment cards against transient router/auth/startup races. This layer is
// presentation-only: it does not create/send a booking, does not alter clientOperationId and does
// not bypass backend/session/Oxaion validation.
(function(){
  const MODE_KEY='fam-pulver-last-process-mode';
  const LEGACY_IDS=['machineStep','sourcesSection','mixSection','bookingStep','result'];
  let recovering=false;
  let lastRecoveryAt=0;

  const el=id=>document.getElementById(id);
  const auth=()=>{
    try{return typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection()}catch{return false}
  };
  const shellProcess=()=>document.body.classList.contains('processShellProcess');
  const scannerOrDialogOpen=()=>document.body.classList.contains('scanModalOpen');
  const activeMode=()=>document.querySelector('.processChoice.active')?.dataset?.mode||'';
  const visible=node=>Boolean(node&&getComputedStyle(node).display!=='none'&&getComputedStyle(node).visibility!=='hidden');
  const legacyVisible=()=>['machineStep','sourcesSection','bookingStep','result'].some(id=>visible(el(id)));
  const instruction=()=>String(el('workerNextInstruction')?.textContent||'').trim();
  const log=(event,details={})=>window.FamDiag?.log?.(event,details);

  function remember(mode){
    if(!mode)return;
    try{sessionStorage.setItem(MODE_KEY,mode)}catch{}
  }
  function remembered(){try{return sessionStorage.getItem(MODE_KEY)||''}catch{return ''}}

  function replenishIsExpected(){
    const active=activeMode();
    if(active)return active==='replenish';
    return shellProcess()&&remembered()==='replenish';
  }

  function enforceLegacyVisibility(reason='guard'){
    if(!replenishIsExpected())return false;
    let changed=false;
    for(const id of LEGACY_IDS){
      const node=el(id);
      if(node?.classList.contains('processModeHidden')){
        node.classList.remove('processModeHidden');
        changed=true;
      }
    }
    if(changed)log('REPLENISH_VISIBILITY_RESTORED',{reason,instruction:instruction(),activeMode:activeMode(),shellProcess:shellProcess()});
    return changed;
  }

  function recover(reason='automatic'){
    if(recovering||!auth()||!replenishIsExpected())return false;
    const button=document.querySelector('.processChoice[data-mode="replenish"]');
    if(!button)return false;
    const now=Date.now();if(now-lastRecoveryAt<250)return false;
    lastRecoveryAt=now;recovering=true;remember('replenish');enforceLegacyVisibility('before-router-recovery');
    log('REPLENISH_ROUTER_RECOVERY_START',{reason,instruction:instruction(),activeMode:activeMode(),legacyVisible:legacyVisible(),shellProcess:shellProcess()});
    try{
      // Re-selecting replenish restores process-mode.js' private mode. The legacy tank/source state
      // lives outside that router and is deliberately not cleared by this operation.
      button.click();
      setTimeout(()=>{
        enforceLegacyVisibility('after-router-recovery');
        try{if(typeof window.refreshWorkerFlow==='function')window.refreshWorkerFlow()}catch(error){log('REPLENISH_ROUTER_RECOVERY_REFRESH_ERROR',{message:String(error?.message||error)})}
        enforceLegacyVisibility('after-worker-refresh');
        log('REPLENISH_ROUTER_RECOVERY_DONE',{instruction:instruction(),activeMode:activeMode(),legacyVisible:legacyVisible(),shellProcess:shellProcess()});
        recovering=false;
      },80);
      return true;
    }catch(error){
      recovering=false;log('REPLENISH_ROUTER_RECOVERY_ERROR',{message:String(error?.message||error)});return false;
    }
  }

  function check(){
    const active=activeMode();if(active)remember(active);
    if(!replenishIsExpected())return;

    // The active Replenish choice is the strongest UI signal that these legacy cards belong to the
    // current process. Never let a transient auth/router refresh hide all of them. Authentication-
    // aware code still disables actions, and process-shell returns home after a persistent auth loss.
    enforceLegacyVisibility('periodic-invariant');

    const lostInstruction=instruction()==='Vorgang auswählen.';
    const hiddenCards=!legacyVisible();
    if(!auth()){
      if(lostInstruction||hiddenCards)log('REPLENISH_AUTH_TRANSIENT_UI_LOSS',{instruction:instruction(),activeMode:active,legacyVisible:!hiddenCards,shellProcess:shellProcess()});
      return;
    }
    if(scannerOrDialogOpen())return;
    if(lostInstruction||hiddenCards)recover(lostInstruction?'instruction-reset':'legacy-cards-hidden');
  }

  document.addEventListener('click',event=>{
    const choice=event.target?.closest?.('.processChoice');
    if(choice?.dataset?.mode)remember(choice.dataset.mode);
    if(event.target?.closest?.('#processHomeBtn')){try{sessionStorage.removeItem(MODE_KEY)}catch{}}
  },true);
  window.FamReplenishGuard={recover,check,remembered,enforceLegacyVisibility};
  const install=()=>{
    new MutationObserver(()=>queueMicrotask(check)).observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['class']});
    setInterval(check,120);setTimeout(check,0);
  };
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',install);else install();
})();
