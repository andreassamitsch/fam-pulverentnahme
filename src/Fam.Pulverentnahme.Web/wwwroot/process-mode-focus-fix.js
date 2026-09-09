'use strict';

// Stabilizes the process selector around the legacy replenishment worker flow.
// process-mode.js owns the process state and button handlers; this layer only owns
// refresh/focus routing after all process-mode initialization has completed.
(function(){
  const baseWorkerRefresh=typeof window.refreshWorkerFlow==='function'?window.refreshWorkerFlow:null;
  let modeOwnerKey='';
  let retainedMode='';

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
    const active=document.querySelector('.processChoice.active')?.dataset?.mode||'';
    if(active){retainedMode=active;return active}
    if(document.body.classList.contains('processShellProcess'))return retainedMode||null;
    retainedMode='';
    return null;
  }
  function restoreActiveMarker(mode){
    if(!mode||!document.body.classList.contains('processShellProcess'))return;
    const button=[...document.querySelectorAll('.processChoice')].find(x=>x.dataset?.mode===mode);
    if(button&&!button.classList.contains('active'))button.classList.add('active');
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
    retainedMode='';
  }
  function showRetainedPanel(selected){
    restoreActiveMarker(selected);
    el('processSelected')?.classList.remove('hidden');
    hideProcessPanels();
    if(selected==='replenish'){
      hideLegacy(false);
      return;
    }
    hideLegacy(true);
    const panel=panelByMode[selected];
    if(panel)el(panel)?.classList.remove('hidden');
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

    // A short-lived client-side auth/session refresh must never turn a selected process into an
    // empty page. While the process shell still owns a retained mode we keep its cards visible;
    // the underlying controls remain disabled by the authentication-aware worker/process logic.
    if(!authenticated){
      const selected=activeMode();
      if(selected&&document.body.classList.contains('processShellProcess')){
        showRetainedPanel(selected);
        setInstruction('Anmeldung wird geprüft. Der aktuelle Vorgang bleibt erhalten.');
      }else{
        hideLegacy(true);
        hideProcessPanels();
        el('processSelected')?.classList.add('hidden');
        setInstruction('Bitte anmelden.');
      }
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

    // On Android the worker refresh can run while another UI layer is briefly mutating the
    // process-choice classes. Retain the process while the shell is still on the process page;
    // otherwise a replenishment scan can leave the header on "Nachfüllen" but hide every card.
    showRetainedPanel(selected);
    if(!modeOwnerKey)modeOwnerKey=currentOwner;

    if(selected==='replenish'){
      // Exactly one worker-flow refresh. The old process-mode wrapper invoked this twice,
      // which repeatedly pulled focus/scroll while the operator was working.
      if(baseWorkerRefresh)baseWorkerRefresh();
    }
  }

  function loadTargetLocationSelector(){
    if(document.querySelector('script[data-target-location-selector]'))return;
    const script=document.createElement('script');
    script.src='/target-location.js?v=20260909-pcl-targets-1';
    script.dataset.targetLocationSelector='true';
    document.head.appendChild(script);
  }

  function loadHeaderUserMenu(){
    if(document.querySelector('script[data-header-user-menu]'))return;
    const script=document.createElement('script');
    script.src='/header-user-menu.js?v=20260909-header-user-2';
    script.dataset.headerUserMenu='true';
    document.head.appendChild(script);
  }

  function install(){
    document.querySelectorAll('.processChoice').forEach(button=>{
      button.addEventListener('click',()=>{
        retainedMode=button.dataset?.mode||retainedMode;
        if(auth())modeOwnerKey=personKey();
      });
    });

    // process-mode.js installs its refresh wrapper during DOMContentLoaded. Replace it
    // afterwards with the stable router while retaining the original worker refresh
    // captured above.
    window.refreshWorkerFlow=stableRefresh;
    stableRefresh();
    loadTargetLocationSelector();
    loadHeaderUserMenu();
  }

  if(document.readyState==='loading'){
    document.addEventListener('DOMContentLoaded',()=>setTimeout(install,10));
  }else{
    setTimeout(install,10);
  }
})();
