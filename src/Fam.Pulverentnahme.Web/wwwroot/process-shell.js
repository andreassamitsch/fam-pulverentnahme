'use strict';

// UI shell around the individual powder processes. It does not change backend authorization,
// Oxaion validation, transaction status or retry rules.
(function(){
  let page='home';
  let fillScanContext=false;
  let installed=false;

  const el=id=>document.getElementById(id);
  const auth=()=>typeof personnelSessionMatchesSelection==='function'&&personnelSessionMatchesSelection();
  const escapeHtml=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));
  const parseNumber=text=>{
    const match=String(text||'').replace(/\s/g,'').match(/-?\d+(?:[.,]\d+)?/);
    return match?Number(match[0].replace(',','.')):NaN;
  };
  const formatKg=value=>Number(value).toFixed(3).replace('.',',')+' kg';

  function currentFullName(){
    if(auth()&&typeof selectedPersonnel!=='undefined'&&selectedPersonnel?.fullName)return selectedPersonnel.fullName;
    const text=el('personnelSelectedText')?.textContent||'';
    const pos=text.indexOf(' - ');
    return pos>=0?text.slice(pos+3).trim():text.trim();
  }

  function ensureHeader(){
    const header=document.querySelector('.workerHeader');
    if(!header)return false;
    const titleBox=header.firstElementChild;
    if(titleBox&&!el('headerPersonnelName')){
      const name=document.createElement('div');
      name.id='headerPersonnelName';name.className='shellPersonnel hidden';
      titleBox.appendChild(name);
    }
    if(!el('processHomeBtn')){
      const button=document.createElement('button');
      button.id='processHomeBtn';button.type='button';button.className='secondary compact shellHomeButton hidden';button.textContent='Vorgänge';
      const dev=header.querySelector('.devSwitch');
      if(dev)header.insertBefore(button,dev);else header.appendChild(button);
      button.addEventListener('click',()=>{
        if(document.body.classList.contains('scanModalOpen'))return;
        if(typeof active!=='undefined'&&active){alert('Dieser Vorgang ist noch offen. Zuerst Buchungsstatus klären.');return}
        if(!confirm('Aktuellen Vorgang verlassen und die sichtbaren Eingaben verwerfen?'))return;
        clearProcessDisplay(true);
        showHome();
      });
    }
    return true;
  }

  function syncHeader(){
    if(!ensureHeader())return;
    const ok=auth(),name=currentFullName();
    const label=el('headerPersonnelName');
    if(label){label.textContent=ok&&name?name:'';label.classList.toggle('hidden',!ok||!name)}
    el('processHomeBtn')?.classList.toggle('hidden',!ok||page!=='process');
  }

  function applyPage(){
    if(!auth())page='home';
    document.body.classList.toggle('processShellHome',page==='home');
    document.body.classList.toggle('processShellProcess',page==='process'&&auth());
    syncHeader();
    if(page==='home'){
      const instruction=el('workerNextInstruction');
      if(instruction)instruction.textContent=auth()?'Vorgang auswählen.':'Bitte anmelden.';
    }
  }

  function showHome(){
    page='home';
    document.querySelectorAll('.processChoice.active').forEach(button=>button.classList.remove('active'));
    el('processSelected')?.classList.add('hidden');
    applyPage();
    setTimeout(()=>{try{(auth()?el('processChoiceStep'):el('loginStep'))?.scrollIntoView({behavior:'smooth',block:'start'})}catch{}},30);
  }

  function showProcess(){page='process';applyPage()}

  function clearProcessDisplay(clearLegacy=false){
    for(const id of ['outWarehouse','outStorageBin','outWarehouseLookup','outStorageBinLookup','faConsumptionAmount'])if(el(id))el(id).value='';
    for(const id of ['outTankData','fillTankData','fillArticlePanel','faTankData','faOrderData']){
      const node=el(id);if(node){node.innerHTML='';node.classList.add('hidden')}
    }
    if(el('fillSources'))el('fillSources').innerHTML='';
    if(el('outSummary'))el('outSummary').innerHTML='';
    if(el('fillSummary'))el('fillSummary').innerHTML='';
    if(el('faSummary'))el('faSummary').innerHTML='';
    for(const id of ['outDestinationStep','outBookStep','fillSourcesStep','fillBookStep','faOrderStep','faAmountStep','faBookStep'])el(id)?.classList.add('lockedStep');
    if(el('outBookBtn'))el('outBookBtn').disabled=true;
    if(el('fillChargeScan'))el('fillChargeScan').disabled=true;
    if(el('fillBookBtn'))el('fillBookBtn').disabled=true;
    if(el('faOrderScan'))el('faOrderScan').disabled=true;
    if(el('faBookBtn'))el('faBookBtn').disabled=true;
    setNeutral('outTankStatus','Tank-QR enthält nur den Tanklagerort.');
    setNeutral('fillTankStatus','Tank muss laut Oxaion eindeutig leer sein.');
    setNeutral('fillSourceStatus','Zuerst leeren Tank scannen.');
    setNeutral('faTankStatus','Tank-QR enthält nur den Tanklagerort.');
    setNeutral('faOrderStatus','');setNeutral('faAmountStatus','');
    if(clearLegacy&&typeof clearMachineInfo==='function'){
      if(el('oldMixWarehouse'))el('oldMixWarehouse').value='';
      clearMachineInfo();
      if(el('machineScanValue'))el('machineScanValue').textContent='Noch nicht gescannt';
      if(el('stockStatus')){el('stockStatus').className='status neutral';el('stockStatus').textContent='Bitte Maschinentank scannen.'}
      el('result')?.classList.add('hidden');
    }
  }

  function setNeutral(id,text){const node=el(id);if(node){node.className='status neutral';node.textContent=text}}

  function expectedFillArticle(){
    const panel=el('fillArticlePanel');
    if(!panel||panel.classList.contains('hidden'))return '';
    const text=panel.querySelector('b')?.textContent||panel.textContent||'';
    return text.match(/RP\.[A-Z0-9._-]+/i)?.[0]||'';
  }

  function fillTankWarehouse(){
    const text=el('fillTankData')?.querySelector('b')?.textContent||'';
    return text.split('·')[0].trim();
  }

  async function scanColors(article){
    try{
      if(typeof api!=='function')return null;
      const response=await api('/api/article-recognition-colors?'+new URLSearchParams({article}));
      return response.ok?response.body:null;
    }catch{return null}
  }

  function swatchFromColors(colors){
    const norm=v=>String(v||'').trim().replace(/^#/,'');
    const a=norm(colors?.color1?.hex),b=norm(colors?.color2?.hex);
    const style=v=>/^[0-9A-Fa-f]{6}$/.test(v)?`background:#${v}`:'';
    return `<div class="scanMiniSwatch"><span style="${style(a)}"></span><span style="${style(b)}"></span></div>`;
  }

  async function auditFillWrongArticle(expected,scan){
    const audit=el('rejectedScanAudit');
    if(audit){audit.className='scanAuditNote';audit.textContent='Fehlscan wird dokumentiert …'}
    try{
      const response=await api('/api/scan-events/rejected-charge',{method:'POST',body:JSON.stringify({
        reason:'WRONG_ARTICLE',machineWarehouse:fillTankWarehouse(),expectedArticle:expected,
        scannedArticle:scan.article,scannedBatch:scan.batch
      })});
      if(!response.ok)throw new Error();
      if(audit){audit.className='scanAuditNote ok';audit.textContent='Fehlscan wurde dokumentiert.'}
    }catch{
      if(audit){audit.className='scanAuditNote bad';audit.textContent='Fehlscan konnte nicht protokolliert werden. Bitte Produktionsleitung informieren.'}
    }
  }

  async function showFillWrongArticle(expected,scan){
    const modal=el('rejectedScanModal'),button=el('rejectedScanAcknowledge');
    if(!modal||!button)return;
    const targetSwatch=el('fillArticlePanel')?.querySelector('.processSwatch')?.outerHTML||swatchFromColors(null);
    const actualColors=await scanColors(scan.article);
    el('rejectedScanTitle').textContent='Falsches Pulver';
    el('rejectedScanMessage').innerHTML=`<b>Diese Charge darf NICHT in den Maschinentank eingefüllt werden.</b><br>Für die neue Tankbefüllung ist Artikel <strong>${escapeHtml(expected)}</strong> festgelegt. Gescannt wurde <strong>${escapeHtml(scan.article)}</strong>, Charge <strong>${escapeHtml(scan.batch)}</strong>.`;
    el('rejectedScanComparison').innerHTML=`<div class="scanCompareSide scanCompareTarget"><span class="scanCompareLabel">SOLL</span><div class="scanCompareContent">${targetSwatch}<div><b>${escapeHtml(expected)}</b></div></div></div><div class="scanCompareSide scanCompareActual bad"><span class="scanCompareLabel">IST / GESCANNT</span><div class="scanCompareContent">${swatchFromColors(actualColors)}<div><b>${escapeHtml(scan.article)}</b><span>Charge ${escapeHtml(scan.batch)}</span></div></div></div>`;
    modal.classList.remove('hidden');document.body.classList.add('scanModalOpen');
    auditFillWrongArticle(expected,scan).catch(()=>{});
    await new Promise(resolve=>{
      button.onclick=()=>{button.onclick=null;modal.classList.add('hidden');document.body.classList.remove('scanModalOpen');resolve()};
      setTimeout(()=>button.focus(),20);
    });
    const status=el('fillSourceStatus');
    if(status){status.className='status bad';status.textContent='⛔ Fehlscan verworfen. Bitte eine Charge des richtigen Artikels scannen.'}
  }

  function wrapFillScanner(){
    if(typeof scanQrCode!=='function'||scanQrCode.__fillSafetyWrapped)return;
    const base=scanQrCode;
    const wrapped=async function(options){
      const isFill=fillScanContext;
      fillScanContext=false;
      const raw=await base(options);
      if(!isFill)return raw;
      const expected=expectedFillArticle();
      if(!expected)return raw;
      const parts=String(raw||'').trim().split('+++').map(x=>x.trim());
      if(parts.length===2&&parts[0]&&parts[1]&&parts[0].toUpperCase()!==expected.toUpperCase()){
        await showFillWrongArticle(expected,{article:parts[0],batch:parts[1]});
        const error=new Error('Falscher Artikel wurde blockiert.');error.name='AbortError';throw error;
      }
      return raw;
    };
    wrapped.__fillSafetyWrapped=true;
    scanQrCode=wrapped;
  }

  function detailValue(label){
    for(const row of el('faOrderData')?.querySelectorAll('.processDetailGrid>div')||[]){
      if((row.querySelector('span')?.textContent||'').trim()===label)return (row.querySelector('b')?.textContent||'').trim();
    }
    return '';
  }

  function syncFaActualUi(){
    const heading=el('faAmountStep')?.querySelector('h2');if(heading)heading.textContent='4 · Pulver Verbrauch eingeben';
    const choice=document.querySelector('.processChoice[data-mode="fa-consumption"] span');if(choice)choice.textContent='Tank, Fertigungsauftrag und Ist-Verbrauch erfassen';
    const input=el('faConsumptionAmount'),material=el('faOrderData');
    if(!input||!material||material.classList.contains('hidden'))return;
    const actualText=String(input.value||'').trim();
    const actual=actualText?Number(actualText.replace(',','.')):NaN;
    const already=parseNumber(detailValue('Bereits tatsächlich gebucht'));
    const required=parseNumber(detailValue('Soll laut Stückliste'));
    const orderNo=detailValue('Fertigungsauftrag');
    const position=detailValue('Materialposition');
    const tankQty=parseNumber(el('faTankData')?.textContent||'');
    const allowed=!el('faAmountStep')?.classList.contains('lockedStep');
    const delta=Number.isFinite(actual)&&Number.isFinite(already)?actual-already:NaN;
    const valid=allowed&&Number.isFinite(actual)&&actual>already+0.0005&&Number.isFinite(delta)&&delta<=tankQty+0.0005;
    const status=el('faAmountStatus');
    if(status){
      if(!actualText){status.className='status neutral';status.textContent=`Ist-Verbrauch eingeben. Bereits gebucht ${formatKg(already)} · Tankbestand ${formatKg(tankQty)}.`}
      else if(!Number.isFinite(actual)||actual<=0){status.className='status bad';status.textContent='⛔ Ist-Verbrauch muss größer als 0 kg sein.'}
      else if(actual<=already+0.0005){status.className='status bad';status.textContent=`⛔ Ist-Verbrauch muss größer als bereits gebucht ${formatKg(already)} sein.`}
      else if(delta>tankQty+0.0005){status.className='status bad';status.textContent=`⛔ Neu zu buchende Differenz ${formatKg(delta)} ist größer als Tankbestand ${formatKg(tankQty)}.`}
      else{status.className='status ok';status.textContent=`✓ Ist-Verbrauch ${formatKg(actual)}. Neu zu buchende Differenz: ${formatKg(delta)}.`}
    }
    const summary=el('faSummary');
    if(summary)summary.innerHTML=`<b>${escapeHtml(orderNo)} · Pos. ${escapeHtml(position)}</b><br>Soll: ${formatKg(required)} · bereits gebucht: <b>${formatKg(already)}</b><br>Ist-Verbrauch: <b>${Number.isFinite(actual)?formatKg(actual):'fehlt'}</b>${Number.isFinite(delta)?`<br>Neu zu buchen: <b>${formatKg(delta)}</b>`:''}`;
    const button=el('faBookBtn');if(button)button.disabled=!valid;
    el('faBookStep')?.classList.toggle('lockedStep',!valid);
    const instruction=el('workerNextInstruction');if(instruction&&page==='process')instruction.textContent=valid?'Ist-Verbrauch prüfen und buchen.':'Pulver Verbrauch eingeben.';
  }

  function normalizeFaConfirm(){
    const title=el('processModalTitle'),body=el('processModalBody');
    if(!title||!body||title.textContent.trim()!=='FA-Verbrauch bestätigen')return;
    body.innerHTML=body.innerHTML
      .replace(/Zusätzlich jetzt:/g,'Ist-Verbrauch:')
      .replace(/Zusätzlicher Verbrauch/g,'Ist-Verbrauch')
      .replace(/Zusätzlichen Verbrauch/g,'Ist-Verbrauch');
  }

  function install(){
    if(installed)return;installed=true;
    ensureHeader();
    const style=document.createElement('style');style.id='processShellStyles';style.textContent=`
.shellPersonnel{margin-top:3px;font-size:14px;font-weight:850;color:#d9edf9;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;max-width:56vw}.shellPersonnel.hidden,.shellHomeButton.hidden{display:none!important}.shellHomeButton{margin-left:auto;min-height:40px;padding:7px 12px}.processShellHome #machineStep,.processShellHome #sourcesSection,.processShellHome #mixSection,.processShellHome #bookingStep,.processShellHome #result,.processShellHome .processPanel{display:none!important}.processShellProcess #loginStep,.processShellProcess #processChoiceStep{display:none!important}@media(max-width:620px){.workerHeader{gap:8px}.shellPersonnel{max-width:44vw;font-size:12px}.shellHomeButton{font-size:13px;padding:6px 9px}.devSwitch{font-size:12px}}
`;
    document.head.appendChild(style);

    document.addEventListener('click',event=>{
      const choice=event.target?.closest?.('.processChoice');
      if(choice&&auth())setTimeout(()=>{showProcess();syncFaActualUi()},0);
      if(event.target?.closest?.('#fillChargeScan'))fillScanContext=true;
      if(event.target?.closest?.('#processClose')&&el('processModalTitle')?.textContent.trim()==='Buchung erfolgreich'){
        setTimeout(()=>{clearProcessDisplay(false);showHome()},40);
      }
      if(event.target?.closest?.('#bookingResultAcknowledge')&&el('bookingResultTitle')?.textContent.trim()==='Buchung erfolgreich'){
        setTimeout(()=>showHome(),80);
      }
    },true);

    el('faConsumptionAmount')?.addEventListener('input',()=>setTimeout(syncFaActualUi,0));
    const faOrder=el('faOrderData');if(faOrder)new MutationObserver(()=>setTimeout(syncFaActualUi,0)).observe(faOrder,{childList:true,subtree:true});
    const processModal=el('processModal');if(processModal)new MutationObserver(()=>normalizeFaConfirm()).observe(processModal,{childList:true,subtree:true,characterData:true});
    const personnel=el('personnelSelectedText');if(personnel)new MutationObserver(()=>{if(!auth())page='home';syncHeader();applyPage()}).observe(personnel,{childList:true,subtree:true,characterData:true});

    wrapFillScanner();
    syncFaActualUi();
    applyPage();
    setInterval(()=>{syncHeader();if(!auth()&&page!=='home'){page='home';applyPage()}},750);
  }

  function start(){
    let attempts=0;
    const timer=setInterval(()=>{
      attempts++;
      if(el('processChoiceStep')&&el('fillChargeScan')&&el('faConsumptionAmount')){clearInterval(timer);install()}
      else if(attempts>100)clearInterval(timer);
    },30);
  }

  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
