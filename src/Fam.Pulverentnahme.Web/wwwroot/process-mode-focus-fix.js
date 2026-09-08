'use strict';

// Stabilizes the process selector around the legacy replenishment worker flow.
// process-mode.js owns the process state and button handlers; this layer only owns
// refresh/focus routing after all process-mode initialization has completed.
(function(){
  const baseWorkerRefresh=typeof window.refreshWorkerFlow==='function'?window.refreshWorkerFlow:null;
  let modeOwnerKey='';

  const el=id=>document.getElementById(id);
  const auth=()=>typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection();
  const personKey=()=>{
    if(!auth()||!selectedPersonnel)return '';
    return `${selectedPersonnel.personnelNo||''}\u001f${selectedPersonnel.fullName||''}`;
  };
  const panelByMode={
    'tank-out':'tankOutProcess',
    'fill-new':'fillNewProcess',
    'fa-consumption':'faConsumptionProcess',
    inventory:'inventoryProcess'
  };
  const processPanels=Object.values(panelByMode);
  const legacyIds=['machineStep','sourcesSection','mixSection','bookingStep','result'];

  function hideLegacy(hidden){
    legacyIds.forEach(id=>el(id)?.classList.toggle('processModeHidden',hidden));
  }
  function hideProcessPanels(){
    processPanels.forEach(id=>el(id)?.classList.add('hidden'));
  }
  function activeMode(){
    return document.querySelector('.processChoice.active')?.dataset?.mode||null;
  }
  function setInstruction(text){
    const instruction=el('workerNextInstruction');
    if(instruction)instruction.textContent=text;
  }
  function clearForeignMode(){
    document.querySelectorAll('.processChoice.active').forEach(button=>button.classList.remove('active'));
    el('processSelected')?.classList.add('hidden');
    hideProcessPanels();
    hideLegacy(true);
    modeOwnerKey='';
  }

  function stableRefresh(){
    const authenticated=auth();
    const choice=el('processChoiceStep');
    if(!choice){
      if(authenticated&&baseWorkerRefresh)baseWorkerRefresh();
      return;
    }

    choice.classList.toggle('lockedStep',!authenticated);
    document.querySelectorAll('.processChoice').forEach(button=>button.disabled=!authenticated);

    // A short-lived auth/session refresh must never throw away the selected process.
    // We only hide process content until the same authenticated person is confirmed again.
    if(!authenticated){
      hideLegacy(true);
      hideProcessPanels();
      el('processSelected')?.classList.add('hidden');
      setInstruction('Bitte anmelden.');
      return;
    }

    const currentOwner=personKey();
    let selected=activeMode();
    if(selected&&modeOwnerKey&&currentOwner&&modeOwnerKey!==currentOwner){
      clearForeignMode();
      selected=null;
    }

    if(!selected){
      hideLegacy(true);
      hideProcessPanels();
      el('processSelected')?.classList.add('hidden');
      setInstruction('Vorgang auswählen.');
      return;
    }

    if(!modeOwnerKey)modeOwnerKey=currentOwner;
    el('processSelected')?.classList.remove('hidden');
    hideProcessPanels();

    if(selected==='replenish'){
      hideLegacy(false);
      // Exactly one worker-flow refresh. The old process-mode wrapper invoked this twice,
      // which repeatedly pulled focus/scroll while the operator was working.
      if(baseWorkerRefresh)baseWorkerRefresh();
      return;
    }

    hideLegacy(true);
    const panel=panelByMode[selected];
    if(panel)el(panel)?.classList.remove('hidden');
  }

  function loadTargetLocationSelector(){
    if(document.querySelector('script[data-target-location-selector]'))return;
    const script=document.createElement('script');
    script.src='/target-location.js?v=20260908-location-ajax-1';
    script.dataset.targetLocationSelector='true';
    document.head.appendChild(script);
  }

  function install(){
    document.querySelectorAll('.processChoice').forEach(button=>{
      button.addEventListener('click',()=>{
        if(auth())modeOwnerKey=personKey();
      });
    });

    // process-mode.js installs its refresh wrapper during DOMContentLoaded. Replace it
    // afterwards with the stable router while retaining the original worker refresh
    // captured above.
    window.refreshWorkerFlow=stableRefresh;
    stableRefresh();
    loadTargetLocationSelector();
  }

  if(document.readyState==='loading'){
    document.addEventListener('DOMContentLoaded',()=>setTimeout(install,10));
  }else{
    setTimeout(install,10);
  }
})();
