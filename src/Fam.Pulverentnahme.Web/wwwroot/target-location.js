'use strict';

// Worker UI helper for tank-out target selection. Visible text is only a search term; the
// authoritative hidden values are set exclusively after choosing a backend-supplied Oxaion key.
// The backend still performs the confirmed US16601R/LB13210R F4 validation before any write.
(function(){
  let initialized=false,warehouseTimer=null,binTimer=null,warehouseToken=0,binToken=0;
  let warehouseMatches=new Map(),binMatches=new Map();
  const el=id=>document.getElementById(id);
  const escText=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot',"'":'&#039;'}[c]));

  function canonicalInput(original,value){
    original.value=value||'';
    original.dispatchEvent(new Event('input',{bubbles:true}));
  }

  function resultPanel(input,id){
    const panel=document.createElement('div');
    panel.id=id;panel.className='lookupResults targetLookupResults hidden';
    input.insertAdjacentElement('afterend',panel);
    return panel;
  }

  function statusAfter(panel,id,text){
    const status=document.createElement('div');
    status.id=id;status.className='targetLookupStatus';status.textContent=text;
    panel.insertAdjacentElement('afterend',status);
    return status;
  }

  function setStatus(id,text,kind='neutral'){
    const status=el(id);if(!status)return;
    status.className=`targetLookupStatus ${kind}`;status.textContent=text;
  }

  function hidePanel(id){el(id)?.classList.add('hidden')}

  function renderWarehouseResults(rows){
    const panel=el('outWarehouseResults');if(!panel)return;
    warehouseMatches=new Map();
    for(const row of rows||[]){
      const code=String(row?.warehouse||'').trim();if(code)warehouseMatches.set(code.toUpperCase(),row);
    }
    panel.innerHTML=[...warehouseMatches.values()].map(row=>
      `<button class="lookupOption targetLookupOption" type="button" data-warehouse="${escText(row.warehouse)}"><b>${escText(row.warehouse)}</b></button>`).join('');
    panel.classList.toggle('hidden',warehouseMatches.size===0);
    panel.querySelectorAll('[data-warehouse]').forEach(button=>{
      button.addEventListener('pointerdown',event=>event.preventDefault());
      button.addEventListener('click',()=>acceptWarehouse(button.dataset.warehouse||''));
    });
  }

  function renderBinResults(rows){
    const panel=el('outStorageBinResults');if(!panel)return;
    binMatches=new Map();
    for(const row of rows||[]){
      const code=String(row?.storageBin||'').trim();if(code)binMatches.set(code.toUpperCase(),row);
    }
    panel.innerHTML=[...binMatches.values()].map(row=>
      `<button class="lookupOption targetLookupOption" type="button" data-bin="${escText(row.storageBin)}"><b>${escText(row.storageBin)}</b></button>`).join('');
    panel.classList.toggle('hidden',binMatches.size===0);
    panel.querySelectorAll('[data-bin]').forEach(button=>{
      button.addEventListener('pointerdown',event=>event.preventDefault());
      button.addEventListener('click',()=>acceptBin(button.dataset.bin||''));
    });
  }

  async function loadWarehouses(search=''){
    const original=el('outWarehouse'),visible=el('outWarehouseLookup');if(!original||!visible)return;
    const token=++warehouseToken;
    setStatus('outWarehouseLookupStatus','Lagerorte werden geladen …');
    try{
      const query=new URLSearchParams({q:String(search||'').trim(),excludeWarehouse:String(el('oldMixWarehouse')?.value||'')});
      const response=await api('/api/target-locations/warehouses?'+query);
      if(token!==warehouseToken)return;
      if(!response.ok)throw new Error(response.body?.detail||response.body?.error||'Lagerorte konnten nicht geladen werden.');
      const rows=Array.isArray(response.body)?response.body:[];
      renderWarehouseResults(rows);
      setStatus('outWarehouseLookupStatus',rows.length?'Lagerort aus der Trefferliste auswählen.':'Kein passender lagerplatzgeführter Lagerort gefunden.',rows.length?'neutral':'bad');
    }catch(error){
      if(token!==warehouseToken)return;
      renderWarehouseResults([]);
      setStatus('outWarehouseLookupStatus','⛔ '+(error?.message||'Lagerorte konnten nicht geladen werden.'),'bad');
    }
  }

  async function loadBins(search=''){
    const warehouse=String(el('outWarehouse')?.value||'').trim();
    if(!warehouse){renderBinResults([]);setStatus('outStorageBinLookupStatus','Zuerst Lagerort auswählen.');return}
    const token=++binToken;
    setStatus('outStorageBinLookupStatus','Lagerplätze werden geladen …');
    try{
      const searchText=String(search||'').trim();
      const query=new URLSearchParams({warehouse,q:searchText});
      const response=await api('/api/target-locations/storage-bins?'+query);
      if(token!==binToken)return;
      if(!response.ok)throw new Error(response.body?.detail||response.body?.error||'Lagerplätze konnten nicht geladen werden.');
      const rows=Array.isArray(response.body)?response.body:[];
      renderBinResults(rows);
      const values=[...binMatches.values()];
      const last=values.length?String(values[values.length-1]?.storageBin||'').trim():'';
      if(values.length){
        const scope=searchText?` für „${searchText}“`:'';
        const lastInfo=last?` · letzter geladener Treffer: ${last}`:'';
        setStatus('outStorageBinLookupStatus',`${values.length} Lagerplätze${scope} aus Oxaion geladen${lastInfo}. Lagerplatz auswählen.`,'neutral');
      }else{
        setStatus('outStorageBinLookupStatus','Kein passender Lagerplatz gefunden.','bad');
      }
    }catch(error){
      if(token!==binToken)return;
      renderBinResults([]);
      setStatus('outStorageBinLookupStatus','⛔ '+(error?.message||'Lagerplätze konnten nicht geladen werden.'),'bad');
    }
  }

  function clearBinSelection(clearVisible=true){
    const original=el('outStorageBin'),visible=el('outStorageBinLookup');
    if(original)canonicalInput(original,'');
    if(visible&&clearVisible)visible.value='';
    renderBinResults([]);
    setStatus('outStorageBinLookupStatus','Zuerst Lagerort auswählen.');
  }

  function acceptWarehouse(code){
    const original=el('outWarehouse'),visible=el('outWarehouseLookup');if(!original||!visible)return;
    const match=warehouseMatches.get(String(code||'').trim().toUpperCase());if(!match)return;
    visible.value=match.warehouse;
    canonicalInput(original,match.warehouse);
    clearBinSelection(true);
    hidePanel('outWarehouseResults');
    setStatus('outWarehouseLookupStatus',`✓ Lagerort ${match.warehouse} ausgewählt.`,'ok');
    el('outStorageBinLookup')?.focus();
    loadBins('').catch(()=>{});
  }

  function acceptBin(code){
    const original=el('outStorageBin'),visible=el('outStorageBinLookup');if(!original||!visible)return;
    const match=binMatches.get(String(code||'').trim().toUpperCase());if(!match)return;
    visible.value=match.storageBin;
    canonicalInput(original,match.storageBin);
    hidePanel('outStorageBinResults');
    setStatus('outStorageBinLookupStatus',`✓ Lagerplatz ${match.storageBin} ausgewählt.`,'ok');
    visible.blur();
  }

  function resetVisible(){
    const w=el('outWarehouseLookup'),b=el('outStorageBinLookup');
    if(w)w.value='';if(b)b.value='';
    warehouseMatches=new Map();binMatches=new Map();
    hidePanel('outWarehouseResults');hidePanel('outStorageBinResults');
    setStatus('outWarehouseLookupStatus','Lagerort suchen und aus der Trefferliste auswählen.');
    setStatus('outStorageBinLookupStatus','Zuerst Lagerort auswählen.');
  }

  function init(){
    if(initialized)return true;
    const originalWarehouse=el('outWarehouse'),originalBin=el('outStorageBin');
    if(!originalWarehouse||!originalBin)return false;
    initialized=true;

    const warehouseLookup=document.createElement('input');
    warehouseLookup.id='outWarehouseLookup';warehouseLookup.className='importantInput';warehouseLookup.autocomplete='off';warehouseLookup.placeholder='Lagerort suchen …';
    const binLookup=document.createElement('input');
    binLookup.id='outStorageBinLookup';binLookup.className='importantInput';binLookup.autocomplete='off';binLookup.placeholder='Lagerplatz suchen …';

    originalWarehouse.type='hidden';originalWarehouse.classList.remove('importantInput');
    originalBin.type='hidden';originalBin.classList.remove('importantInput');
    originalWarehouse.insertAdjacentElement('afterend',warehouseLookup);
    originalBin.insertAdjacentElement('afterend',binLookup);
    const warehousePanel=resultPanel(warehouseLookup,'outWarehouseResults');
    statusAfter(warehousePanel,'outWarehouseLookupStatus','Lagerort suchen und aus der Trefferliste auswählen.');
    const binPanel=resultPanel(binLookup,'outStorageBinResults');
    statusAfter(binPanel,'outStorageBinLookupStatus','Zuerst Lagerort auswählen.');

    // process-mode.js calls focus() on the original destination field after a successful tank scan.
    // Redirect that focus to the visible AJAX search field.
    originalWarehouse.focus=()=>warehouseLookup.focus();
    originalBin.focus=()=>binLookup.focus();

    warehouseLookup.addEventListener('focus',()=>loadWarehouses(warehouseLookup.value).catch(()=>{}));
    warehouseLookup.addEventListener('input',()=>{
      canonicalInput(originalWarehouse,'');clearBinSelection(true);
      clearTimeout(warehouseTimer);warehouseTimer=setTimeout(()=>loadWarehouses(warehouseLookup.value).catch(()=>{}),180);
    });
    warehouseLookup.addEventListener('keydown',event=>{
      if(event.key!=='Enter')return;
      const match=warehouseMatches.get(warehouseLookup.value.trim().toUpperCase());
      if(match){event.preventDefault();acceptWarehouse(match.warehouse)}
    });
    warehouseLookup.addEventListener('blur',()=>setTimeout(()=>hidePanel('outWarehouseResults'),160));

    binLookup.addEventListener('focus',()=>loadBins(binLookup.value).catch(()=>{}));
    binLookup.addEventListener('input',()=>{
      canonicalInput(originalBin,'');
      clearTimeout(binTimer);binTimer=setTimeout(()=>loadBins(binLookup.value).catch(()=>{}),180);
    });
    binLookup.addEventListener('keydown',event=>{
      if(event.key!=='Enter')return;
      const match=binMatches.get(binLookup.value.trim().toUpperCase());
      if(match){event.preventDefault();acceptBin(match.storageBin)}
    });
    binLookup.addEventListener('blur',()=>setTimeout(()=>hidePanel('outStorageBinResults'),160));

    document.addEventListener('click',event=>{
      const processChoice=event.target?.closest?.('.processChoice');
      if(processChoice)setTimeout(resetVisible,0);
      if(event.target?.id==='processClose'&&!el('outWarehouse')?.value)setTimeout(resetVisible,0);
    },true);

    const style=document.createElement('style');style.id='targetLocationLookupStyles';style.textContent=`
.targetLookupResults{max-height:260px;overflow:auto;margin-top:6px;border:1px solid #c7d8e2;border-radius:12px;background:#fff;box-shadow:0 12px 28px rgba(18,46,63,.14);position:relative;z-index:20}.targetLookupResults.hidden{display:none}.targetLookupOption{display:block;width:100%;padding:13px 14px;text-align:left;border:0;border-bottom:1px solid #e4ebef;background:#fff;color:#172c3a;font-size:17px}.targetLookupOption:last-child{border-bottom:0}.targetLookupOption:active{background:#e9f4fb}.targetLookupStatus{margin:7px 2px 0;font-size:13px;line-height:1.35;color:#60737f}.targetLookupStatus.ok{color:#16724f;font-weight:750}.targetLookupStatus.bad{color:#a32c25;font-weight:750}`;
    document.head.appendChild(style);
    return true;
  }

  function start(){
    if(init())return;
    let attempts=0;
    const timer=setInterval(()=>{attempts++;if(init()||attempts>80)clearInterval(timer)},50);
  }
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
