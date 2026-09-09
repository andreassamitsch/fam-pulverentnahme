'use strict';

// Targeted Android/STAGING corrections confirmed after the 2026-09-09 shop-floor test.
// This layer changes only presentation/navigation of already confirmed backend data. Canonical
// warehouse/storage-bin keys still come from the read-only backend lookup and are revalidated by
// Oxaion immediately before an effective booking.
(function(){
  const el=id=>document.getElementById(id);
  let destinationWasReady=false;

  const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));
  const activeMode=()=>document.querySelector('.processChoice.active')?.dataset?.mode||'';

  function setLookupStatus(id,text,kind='neutral'){
    const node=el(id);if(!node)return;
    node.className=`targetLookupStatus ${kind}`;
    node.textContent=text;
  }

  async function getJson(url){
    const response=await api(url);
    if(!response.ok)throw new Error(response.body?.detail||response.body?.error||'Auswahl konnte nicht geladen werden.');
    return Array.isArray(response.body)?response.body:[];
  }

  function canonicalInput(id,value){
    const node=el(id);if(!node)return;
    node.value=value||'';
    node.dispatchEvent(new Event('input',{bubbles:true}));
  }

  function clearBinSelection(){
    const visible=el('outStorageBinLookup');if(visible)visible.value='';
    canonicalInput('outStorageBin','');
    const panel=el('outStorageBinResults');if(panel){panel.innerHTML='';panel.classList.add('hidden')}
    setLookupStatus('outStorageBinLookupStatus','Lagerplatz auswählen.');
  }

  function renderAllWarehouses(rows){
    const panel=el('outWarehouseResults');if(!panel)return;
    const unique=new Map();
    for(const row of rows){const code=String(row?.warehouse||'').trim();if(code)unique.set(code.toUpperCase(),row)}
    panel.innerHTML=[...unique.values()].map(row=>`<button class="lookupOption targetLookupOption" type="button" data-hotfix-warehouse="${esc(row.warehouse)}"><b>${esc(row.warehouse)}</b></button>`).join('');
    panel.classList.toggle('hidden',unique.size===0);
    panel.querySelectorAll('[data-hotfix-warehouse]').forEach(button=>{
      button.addEventListener('pointerdown',event=>event.preventDefault());
      button.addEventListener('click',()=>{
        const code=String(button.dataset.hotfixWarehouse||'').trim();
        const visible=el('outWarehouseLookup');if(visible)visible.value=code;
        canonicalInput('outWarehouse',code);
        clearBinSelection();
        panel.classList.add('hidden');
        setLookupStatus('outWarehouseLookupStatus',`✓ Lagerort ${code} ausgewählt.`,'ok');
        setTimeout(()=>openAllBins(),20);
      });
    });
  }

  function renderAllBins(rows){
    const panel=el('outStorageBinResults');if(!panel)return;
    const unique=new Map();
    for(const row of rows){const code=String(row?.storageBin||'').trim();if(code)unique.set(code.toUpperCase(),row)}
    panel.innerHTML=[...unique.values()].map(row=>`<button class="lookupOption targetLookupOption" type="button" data-hotfix-bin="${esc(row.storageBin)}"><b>${esc(row.storageBin)}</b></button>`).join('');
    panel.classList.toggle('hidden',unique.size===0);
    panel.querySelectorAll('[data-hotfix-bin]').forEach(button=>{
      button.addEventListener('pointerdown',event=>event.preventDefault());
      button.addEventListener('click',()=>{
        const code=String(button.dataset.hotfixBin||'').trim();
        const visible=el('outStorageBinLookup');if(visible)visible.value=code;
        canonicalInput('outStorageBin',code);
        panel.classList.add('hidden');
        setLookupStatus('outStorageBinLookupStatus',`✓ Lagerplatz ${code} ausgewählt.`,'ok');
        visible?.blur();
      });
    });
  }

  async function openAllWarehouses(){
    const panel=el('outWarehouseResults');if(!panel)return;
    setLookupStatus('outWarehouseLookupStatus','Lagerorte werden geladen …');
    try{
      const query=new URLSearchParams({q:'',excludeWarehouse:String(el('oldMixWarehouse')?.value||'')});
      const rows=await getJson('/api/target-locations/warehouses?'+query);
      renderAllWarehouses(rows);
      setLookupStatus('outWarehouseLookupStatus',rows.length?'Lagerort aus der vollständigen Liste auswählen.':'Kein passender lagerplatzgeführter Lagerort gefunden.',rows.length?'neutral':'bad');
    }catch(error){
      panel.innerHTML='';panel.classList.add('hidden');
      setLookupStatus('outWarehouseLookupStatus','⛔ '+(error?.message||'Lagerorte konnten nicht geladen werden.'),'bad');
    }
  }

  async function openAllBins(){
    const warehouse=String(el('outWarehouse')?.value||'').trim();
    if(!warehouse){setLookupStatus('outStorageBinLookupStatus','Zuerst Lagerort auswählen.');return}
    const panel=el('outStorageBinResults');if(!panel)return;
    setLookupStatus('outStorageBinLookupStatus','Lagerplätze werden geladen …');
    try{
      const query=new URLSearchParams({warehouse,q:''});
      const rows=await getJson('/api/target-locations/storage-bins?'+query);
      renderAllBins(rows);
      setLookupStatus('outStorageBinLookupStatus',rows.length?'Lagerplatz aus der vollständigen Liste auswählen.':'Kein passender Lagerplatz gefunden.',rows.length?'neutral':'bad');
    }catch(error){
      panel.innerHTML='';panel.classList.add('hidden');
      setLookupStatus('outStorageBinLookupStatus','⛔ '+(error?.message||'Lagerplätze konnten nicht geladen werden.'),'bad');
    }
  }

  function handleSelectedLookupTap(event){
    const input=event.target;
    if(input?.id==='outWarehouseLookup'&&String(el('outWarehouse')?.value||'').trim()){
      event.preventDefault();
      input.blur();
      openAllWarehouses();
      return;
    }
    if(input?.id==='outStorageBinLookup'&&String(el('outStorageBin')?.value||'').trim()){
      event.preventDefault();
      input.blur();
      openAllBins();
    }
  }

  function openKeyboard(button,event){
    const input=button.closest('.keyboardLookupWrap')?.querySelector('input');
    if(!input)return;
    event.preventDefault();event.stopImmediatePropagation();
    input.dataset.hotfixKeyboard='arming';
    input.readOnly=false;input.inputMode='text';button.classList.add('active');
    if(document.activeElement===input)input.blur();
    setTimeout(()=>{
      input.readOnly=false;input.inputMode='text';
      try{input.focus({preventScroll:true})}catch{input.focus()}
      try{input.setSelectionRange(0,input.value.length)}catch{}
      input.dataset.hotfixKeyboard='open';
    },35);
  }

  function protectForcedKeyboardBlur(event){
    const input=event.target;
    if(input?.dataset?.hotfixKeyboard==='arming'){
      // Prevent the older 260 ms relock timer from being armed during our intentional
      // blur/refocus cycle. A later real user blur is allowed through and relocks normally.
      event.stopImmediatePropagation();
    }else if(input?.dataset?.hotfixKeyboard==='open'){
      input.dataset.hotfixKeyboard='';
    }
  }

  function focusDestinationAfterTankScan(){
    const step=el('outDestinationStep');
    const tankData=el('outTankData');
    const ready=activeMode()==='tank-out'&&step&&!step.classList.contains('lockedStep')&&tankData&&!tankData.classList.contains('hidden');
    if(!ready){destinationWasReady=false;return}
    if(destinationWasReady)return;
    destinationWasReady=true;
    setTimeout(()=>{
      try{step.scrollIntoView({behavior:'smooth',block:'start'})}catch{}
      setTimeout(()=>{
        const input=el('outWarehouseLookup');
        try{input?.focus({preventScroll:true})}catch{input?.focus()}
      },220);
    },80);
  }

  function clearerTankAmbiguity(){
    const body=el('processModalBody');
    if(!body||body.dataset.tankAmbiguityClarified==='1')return;
    const text=body.textContent||'';
    if(!/(mehrere\s+Bestände|Tankbestand.*nicht.*eindeutig|Maschinentank.*nicht.*eindeutig|Artikel und Mix-Charge sind nicht eindeutig)/i.test(text))return;
    const context=[text,el('outTankData')?.textContent,el('fillTankData')?.textContent,el('faTankData')?.textContent,el('machineScanValue')?.textContent].join(' ');
    const warehouse=context.match(/\bEOS\d+\b/i)?.[0]?.toUpperCase()||'Maschinen';
    body.dataset.tankAmbiguityClarified='1';
    body.innerHTML=`<div class="status bad">Im ${esc(warehouse)} Tank sind laut System mehr als eine Charge bzw. Bestandsposition vorhanden. Daher kann die Buchung nicht durchgeführt werden.<br><b>Bitte nichts mehr buchen und die Produktionsleitung informieren.</b></div>`;
  }

  function sync(){focusDestinationAfterTankScan();clearerTankAmbiguity()}

  function install(){
    document.addEventListener('pointerdown',event=>{
      if(event.target?.closest?.('.keyboardToggle'))return;
      handleSelectedLookupTap(event);
    },true);
    document.addEventListener('click',event=>{
      const button=event.target?.closest?.('.keyboardToggle');
      if(button)openKeyboard(button,event);
    },true);
    document.addEventListener('blur',protectForcedKeyboardBlur,true);

    const style=document.createElement('style');
    style.id='processHotfix20260909Styles';
    style.textContent=`#outDestinationStep{scroll-margin-top:calc(var(--fam-header-height,54px) + 78px)}.targetLookupResults{z-index:60!important}`;
    document.head.appendChild(style);

    new MutationObserver(sync).observe(document.body,{subtree:true,childList:true,attributes:true,attributeFilter:['class'],characterData:true});
    sync();
  }

  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',install);else install();
})();
