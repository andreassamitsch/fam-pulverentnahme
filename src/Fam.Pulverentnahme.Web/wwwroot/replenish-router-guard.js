'use strict';

// Guards the legacy replenishment cards against the exact Android/STAGING state observed on
// 2026-09-09: process-mode.js can transiently set its private mode to null while the visible
// .processChoice.active marker and process shell still say "replenish". Re-selecting replenish is
// presentation-only here: it does not create/send a booking, does not alter clientOperationId and
// does not bypass backend/session/Oxaion validation.
(function(){
  const MODE_KEY='fam-pulver-last-process-mode';
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

  function recover(reason='automatic'){
    if(recovering||!shellProcess()||!auth())return false;
    const selected=activeMode()||remembered();
    if(selected!=='replenish')return false;
    const button=document.querySelector('.processChoice[data-mode="replenish"]');
    if(!button)return false;
    const now=Date.now();if(now-lastRecoveryAt<250)return false;
    lastRecoveryAt=now;recovering=true;remember('replenish');
    log('REPLENISH_ROUTER_RECOVERY_START',{reason,instruction:instruction(),activeMode:activeMode(),legacyVisible:legacyVisible()});
    try{
      // selectMode('replenish') resets only the separate-process state objects. It deliberately
      // leaves the legacy replenishment tank/source state untouched, so this restores the private
      // router mode without discarding the already scanned tank.
      button.click();
      setTimeout(()=>{
        try{if(typeof window.refreshWorkerFlow==='function')window.refreshWorkerFlow()}catch(error){log('REPLENISH_ROUTER_RECOVERY_REFRESH_ERROR',{message:String(error?.message||error)})}
        log('REPLENISH_ROUTER_RECOVERY_DONE',{instruction:instruction(),activeMode:activeMode(),legacyVisible:legacyVisible()});
        recovering=false;
      },80);
      return true;
    }catch(error){
      recovering=false;log('REPLENISH_ROUTER_RECOVERY_ERROR',{message:String(error?.message||error)});return false;
    }
  }

  function check(){
    if(!shellProcess())return;
    const active=activeMode();if(active)remember(active);
    if((active||remembered())!=='replenish')return;
    const lostInstruction=instruction()==='Vorgang auswählen.';
    const hiddenCards=!legacyVisible();
    if(!auth()){
      if(lostInstruction||hiddenCards)log('REPLENISH_AUTH_TRANSIENT_UI_LOSS',{instruction:instruction(),activeMode:active,legacyVisible:!hiddenCards});
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
  window.FamReplenishGuard={recover,check,remembered};
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',()=>{
    new MutationObserver(()=>queueMicrotask(check)).observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['class']});
    setInterval(check,150);setTimeout(check,0);
  });
  else{
    new MutationObserver(()=>queueMicrotask(check)).observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:['class']});
    setInterval(check,150);setTimeout(check,0);
  }
})();
