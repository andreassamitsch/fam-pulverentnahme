'use strict';

// 2026-09-09 worker UX refinements. This layer only changes navigation/presentation and prepared
// input values. Backend authorization, Oxaion pre-write validation, transaction IDs, recovery and
// no-blind-retry rules remain authoritative.
(function(){
  let installed=false;
  let bookingProcessing=false;
  let historyInitialized=false;
  let historySyncing=false;
  let lastShellPage='home';
  let faSuggestionApplying=false;

  const el=id=>document.getElementById(id);
  const activeMode=()=>document.querySelector('.processChoice.active')?.dataset?.mode||'';
  const onProcessPage=()=>document.body.classList.contains('processShellProcess');
  const shortTitles={
    replenish:'Nachfüllen',
    'tank-out':'Auslagern',
    'fill-new':'Tank befüllen',
    'fa-consumption':'Fertigungsauftrag',
    inventory:'Lagerbestand'
  };
  const escapeHtml=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[c]));

  function setText(node,text){if(node&&node.textContent!==text)node.textContent=text}
  function setHtml(node,html){if(node&&node.innerHTML!==html)node.innerHTML=html}
  function setStatus(node,kind,text){
    if(!node)return;
    const cls=`status ${kind}`;
    if(node.className!==cls)node.className=cls;
    setText(node,text);
  }

  function stripStepNumbers(root=document){
    for(const h of root.querySelectorAll?.('.stepCard > h2')||[]){
      const cleaned=String(h.textContent||'').replace(/^\s*\d+\s*·\s*/,'');
      if(cleaned!==h.textContent)h.textContent=cleaned;
    }
  }

  function layoutHeader(){
    const header=document.querySelector('.workerHeader');
    if(!header)return;
    const title=header.querySelector('.title');
    const desired=onProcessPage()?(shortTitles[activeMode()]||'Pulververwaltung'):'Pulververwaltung';
    setText(title,desired);
    const user=el('headerPersonnelName');
    const dev=header.querySelector('.devSwitch');
    if(user&&user.parentElement!==header){
      if(dev)header.insertBefore(user,dev);else header.appendChild(user);
    }
    el('processHomeBtn')?.classList.add('hidden');
    const hint=el('headerPersonnelMenuHint');
    if(hint&&onProcessPage())setText(hint,'Zum Abmelden zuerst mit Zurück zur Vorgangsübersicht wechseln.');
  }

  function updateHeaderOffset(){
    const header=document.querySelector('header');
    if(!header)return;
    const value=`${Math.ceil(header.getBoundingClientRect().height)+4}px`;
    if(document.documentElement.style.getPropertyValue('--fam-header-height')!==value)
      document.documentElement.style.setProperty('--fam-header-height',value);
  }

  function initHistory(){
    if(historyInitialized)return;
    historyInitialized=true;
    if(!history.state?.famPulverPage)history.replaceState({famPulverPage:'home'},'');
    lastShellPage=onProcessPage()?'process':'home';

    window.addEventListener('popstate',()=>{
      if(historySyncing){historySyncing=false;return}
      if(!onProcessPage())return;
      if(bookingProcessing||document.body.classList.contains('scanModalOpen')){
        history.pushState({famPulverPage:'process'},'');
        return;
      }
      const home=el('processHomeBtn');
      if(!home)return;
      // Android Back is the intentional navigation gesture. The old visible header button asked
      // for a second confirmation; Back now performs that same pre-booking discard directly.
      const originalConfirm=window.confirm;
      try{
        window.confirm=()=>true;
        home.click();
      }finally{
        window.confirm=originalConfirm;
      }
      setTimeout(()=>{
        // If process-shell refused to leave because a server-side transaction is open, restore
        // the process history entry. Its own safety alert remains authoritative.
        if(onProcessPage())history.pushState({famPulverPage:'process'},'');
      },100);
    });
  }

  function syncHistory(){
    if(!historyInitialized)return;
    const now=onProcessPage()?'process':'home';
    if(now===lastShellPage)return;
    if(now==='process'){
      if(history.state?.famPulverPage!=='process')history.pushState({famPulverPage:'process'},'');
    }else if(history.state?.famPulverPage==='process'){
      historySyncing=true;
      history.back();
    }
    lastShellPage=now;
  }

  function kgValues(text){
    const values=[];
    const re=/(-?\d+(?:[.,]\d+)?)\s*kg/gi;
    let match;
    while((match=re.exec(String(text||''))))values.push(match[1]);
    return values;
  }
  function lastKgText(text){const values=kgValues(text);return values.length?values[values.length-1]:''}
  function num(text){const n=Number(String(text||'').trim().replace(',','.'));return Number.isFinite(n)?n:NaN}
  function formatKgNumber(value){return Number(value).toFixed(3).replace('.',',')}

  function prefillFillAmounts(){
    const box=el('fillSources');if(!box)return;
    for(const card of box.querySelectorAll('.processSource')){
      const input=card.querySelector('[data-amount]');if(!input)continue;
      if(!input.dataset.suggestionBound){
        input.dataset.suggestionBound='1';
        input.addEventListener('input',event=>{if(event.isTrusted)input.dataset.autoSuggested='false'});
      }
      if(input.dataset.autoSuggested==='false')continue;
      if(String(input.value||'').trim()&&input.dataset.autoSuggested!=='true')continue;
      let available='';
      const select=card.querySelector('[data-pos]');
      if(select){
        if(select.value==='')continue;
        available=lastKgText(select.options[select.selectedIndex]?.textContent||'');
      }else{
        available=lastKgText(card.textContent||'');
      }
      if(!available)continue;
      input.dataset.autoSuggested='true';
      if(input.value!==available){
        input.value=available;
        input.dispatchEvent(new Event('input',{bubbles:true}));
      }
    }
  }

  function fillArticle(){return (el('fillArticlePanel')?.textContent||'').match(/RP\.[A-Z0-9._-]+/i)?.[0]||''}
  function sourceBatch(card){
    const text=card?.querySelector('b')?.textContent||'';
    return text.replace(/^\s*Charge\s+\d+\s*:\s*/i,'').trim();
  }
  function isStoredMix(article,batch){
    const prefix=String(article||'').replace(/[^A-Za-z0-9]/g,'')+'MIX_';
    return prefix.length>4&&String(batch||'').toUpperCase().startsWith(prefix.toUpperCase());
  }
  function preserveSingleMix(){
    const cards=[...(el('fillSources')?.querySelectorAll('.processSource')||[])];
    return cards.length===1&&isStoredMix(fillArticle(),sourceBatch(cards[0]));
  }
  function singleMixBatch(){const card=el('fillSources')?.querySelector('.processSource');return card?sourceBatch(card):''}

  function findMixLabel(panel){
    if(!panel)return null;
    const walker=document.createTreeWalker(panel,NodeFilter.SHOW_TEXT);
    let node;
    while((node=walker.nextNode())){
      if(/(?:Neue Mix-Charge|Mix-Charge bleibt)\s*:/i.test(node.data||'')){
        let next=node.nextSibling;
        while(next&&next.nodeType===Node.TEXT_NODE&&!String(next.textContent||'').trim())next=next.nextSibling;
        return {text:node,value:next?.nodeType===Node.ELEMENT_NODE&&next.tagName==='B'?next:null};
      }
    }
    return null;
  }

  function syncFillMixPresentation(){
    const panel=el('fillArticlePanel');
    const part=findMixLabel(panel);
    if(part?.value){
      if(!panel.dataset.generatedMixBatch&&/Neue Mix-Charge/i.test(part.text.data||''))panel.dataset.generatedMixBatch=part.value.textContent.trim();
      if(preserveSingleMix()){
        if(part.text.data!=='Mix-Charge bleibt: ')part.text.data='Mix-Charge bleibt: ';
        setText(part.value,singleMixBatch());
      }else{
        if(part.text.data!=='Neue Mix-Charge: ')part.text.data='Neue Mix-Charge: ';
        if(panel.dataset.generatedMixBatch)setText(part.value,panel.dataset.generatedMixBatch);
      }
    }

    if(preserveSingleMix()){
      const summary=el('fillSummary');
      if(summary&&/Neue Mix-Charge:/i.test(summary.innerHTML)){
        const corrected=summary.innerHTML.replace(/Neue Mix-Charge:\s*<b>.*?<\/b>/i,`Mix-Charge bleibt: <b>${escapeHtml(singleMixBatch())}</b>`);
        setHtml(summary,corrected);
      }
    }
    setText(document.querySelector('.processChoice[data-mode="fill-new"] span'),'Leeren Tank mit Charge(n) befüllen; Mix-Charge wird aus der finalen Quellwahl bestimmt');
  }

  function detailValue(label){
    for(const row of el('faOrderData')?.querySelectorAll('.processDetailGrid>div')||[]){
      if((row.querySelector('span')?.textContent||'').trim()===label)return (row.querySelector('b')?.textContent||'').trim();
    }
    return '';
  }

  function syncFaUi(){
    const input=el('faConsumptionAmount'),data=el('faOrderData');
    if(!input||!data||data.classList.contains('hidden'))return;
    const required=num(lastKgText(detailValue('Soll laut Stückliste'))||detailValue('Soll laut Stückliste'));
    const already=num(lastKgText(detailValue('Bereits tatsächlich gebucht'))||detailValue('Bereits tatsächlich gebucht'));
    // Important: faTankData begins with an RP article number. The last value explicitly followed
    // by "kg" is the actual Oxaion tank quantity; parsing the first number reproduced the 10 kg bug.
    const tankQty=num(lastKgText(el('faTankData')?.textContent||''));
    if(!Number.isFinite(required)||!Number.isFinite(already)||!Number.isFinite(tankQty))return;

    if(!String(input.value||'').trim()&&!faSuggestionApplying){
      faSuggestionApplying=true;
      input.value=formatKgNumber(required);
      input.dataset.sollSuggested='true';
      input.dispatchEvent(new Event('input',{bubbles:true}));
      faSuggestionApplying=false;
    }
    if(!input.dataset.actualListenerBound){
      input.dataset.actualListenerBound='1';
      input.addEventListener('input',event=>{if(event.isTrusted)input.dataset.sollSuggested='false'});
    }

    const actual=num(input.value);
    const delta=actual-already;
    const allowed=!el('faAmountStep')?.classList.contains('lockedStep')||Boolean(detailValue('Fertigungsauftrag'));
    const valid=allowed&&Number.isFinite(actual)&&actual>already+.0005&&delta<=tankQty+.0005;
    const status=el('faAmountStatus');
    if(!Number.isFinite(actual)||actual<=0)setStatus(status,'bad','⛔ Ist-Verbrauch muss größer als 0 kg sein.');
    else if(actual<=already+.0005)setStatus(status,'bad',`⛔ Ist-Verbrauch muss größer als bereits gebucht ${formatKgNumber(already)} kg sein. Soll-Vorschlag bitte mit dem tatsächlichen Verbrauch prüfen.`);
    else if(delta>tankQty+.0005)setStatus(status,'bad',`⛔ Neu zu buchende Differenz ${formatKgNumber(delta)} kg ist größer als der Tankbestand ${formatKgNumber(tankQty)} kg.`);
    else if(input.dataset.sollSuggested==='true')setStatus(status,'warn',`Vorschlag aus Soll: ${formatKgNumber(required)} kg. Bitte mit dem tatsächlichen Ist-Verbrauch prüfen und bei Abweichung korrigieren. Tankbestand: ${formatKgNumber(tankQty)} kg.`);
    else setStatus(status,'ok',`✓ Ist-Verbrauch ${formatKgNumber(actual)} kg. Neu zu buchen: ${formatKgNumber(delta)} kg. Tankbestand: ${formatKgNumber(tankQty)} kg.`);

    const orderNo=detailValue('Fertigungsauftrag'),position=detailValue('Materialposition');
    const summaryHtml=`<b>Fertigungsauftrag ${escapeHtml(orderNo)} · Materialposition ${escapeHtml(position)}</b><br>Soll: ${formatKgNumber(required)} kg · bereits gebucht: <b>${formatKgNumber(already)} kg</b><br>Ist-Verbrauch: <b>${Number.isFinite(actual)?formatKgNumber(actual)+' kg':'fehlt'}</b>${Number.isFinite(delta)?`<br>Neu zu buchen: <b>${formatKgNumber(delta)} kg</b>`:''}<br>Tankbestand: <b>${formatKgNumber(tankQty)} kg</b>`;
    setHtml(el('faSummary'),summaryHtml);
    const button=el('faBookBtn');if(button)button.disabled=!valid;
    el('faBookStep')?.classList.toggle('lockedStep',!valid);
    if(onProcessPage()&&activeMode()==='fa-consumption')setText(el('workerNextInstruction'),valid?'Pulververbrauch prüfen und buchen.':'Pulververbrauch prüfen und korrigieren.');
  }

  function normalizeConfirmations(){
    const title=el('processModalTitle'),body=el('processModalBody');
    if(!title||!body)return;
    if(/FA-Verbrauch bestätigen|Pulververbrauch auf Fertigungsauftrag bestätigen/.test(title.textContent||'')){
      setText(title,'Pulververbrauch auf Fertigungsauftrag bestätigen');
      const corrected=body.innerHTML
        .replace(/\bFA\s+(?=<b>)/g,'Fertigungsauftrag ')
        .replace(/Materialpos\./g,'Materialposition')
        .replace(/Zusätzlich jetzt:/g,'Ist-Verbrauch:')
        .replace(/Zusätzlicher Verbrauch/g,'Ist-Verbrauch')
        .replace(/Zusätzlichen Verbrauch/g,'Ist-Verbrauch');
      setHtml(body,corrected);
    }
    if(title.textContent.trim()==='Neue Befüllung bestätigen'&&preserveSingleMix()&&/Neue Mix-Charge:/i.test(body.innerHTML)){
      const corrected=body.innerHTML.replace(/Neue Mix-Charge:\s*<b>.*?<\/b>/i,`Mix-Charge bleibt: <b>${escapeHtml(singleMixBatch())}</b>`);
      setHtml(body,corrected);
    }
  }

  function localizeSuccess(){
    const modal=el('processModal'),title=el('processModalTitle'),body=el('processModalBody');
    if(!modal||modal.classList.contains('hidden')||!title||title.textContent.trim()!=='Buchung erfolgreich'||!body)return;
    const mode=activeMode();
    const text={
      'tank-out':'Die Auslagerung wurde in Oxaion erfolgreich gebucht und bestätigt.',
      'fill-new':preserveSingleMix()?'Die Tankbefüllung wurde in Oxaion erfolgreich gebucht und die vorhandene Mix-Charge beibehalten.':'Die Tankbefüllung wurde in Oxaion erfolgreich gebucht und bestätigt.',
      'fa-consumption':'Der Pulververbrauch auf den Fertigungsauftrag wurde in Oxaion erfolgreich gebucht und bestätigt.'
    }[mode];
    if(!text||body.dataset.localizedSuccess===mode)return;
    const original=body.textContent||'';
    const doc=original.match(/Beleg:\s*([^\s]+)/i)?.[1]||'';
    body.dataset.localizedSuccess=mode;
    setHtml(body,`<div class="status ok">✓ ${escapeHtml(text)}${doc?`<br>Beleg: <b>${escapeHtml(doc)}</b>`:''}</div>`);
  }

  function ensureBusyOverlay(){
    if(el('bookingBusyOverlay'))return;
    const overlay=document.createElement('div');
    overlay.id='bookingBusyOverlay';overlay.className='bookingBusyOverlay hidden';
    overlay.innerHTML='<div class="bookingBusyCard"><div class="bookingSpinner" aria-hidden="true"></div><b>Buchung wird verarbeitet …</b><span id="bookingBusyText">Bitte warten und nicht erneut auf Buchen drücken.</span></div>';
    document.body.appendChild(overlay);
  }
  function showBusy(){
    ensureBusyOverlay();bookingProcessing=true;
    setText(el('bookingBusyText'),'Bitte warten und nicht erneut auf Buchen drücken.');
    el('bookingBusyOverlay').classList.remove('hidden');document.body.classList.add('scanModalOpen');
    setTimeout(()=>{if(bookingProcessing)setText(el('bookingBusyText'),'Die Buchung dauert länger. Bitte weiter warten; nicht erneut buchen.')},30000);
  }
  function hideBusy(){
    bookingProcessing=false;el('bookingBusyOverlay')?.classList.add('hidden');
    const processResultVisible=el('processModal')&&!el('processModal').classList.contains('hidden');
    const legacyResultVisible=el('bookingResultModal')&&!el('bookingResultModal').classList.contains('hidden');
    if(!processResultVisible&&!legacyResultVisible)document.body.classList.remove('scanModalOpen');
  }
  function syncBusyResult(){
    if(!bookingProcessing)return;
    const processModal=el('processModal');
    const processTitle=el('processModalTitle')?.textContent||'';
    const legacy=el('bookingResultModal');
    if((processModal&&!processModal.classList.contains('hidden')&&!/bestätigen/i.test(processTitle))||(legacy&&!legacy.classList.contains('hidden')))hideBusy();
  }

  function configureLookupKeyboard(inputId,label){
    const input=el(inputId);if(!input||input.dataset.keyboardToggleBound)return;
    input.dataset.keyboardToggleBound='1';input.readOnly=true;input.inputMode='none';
    const wrap=document.createElement('div');wrap.className='keyboardLookupWrap';
    input.parentNode.insertBefore(wrap,input);wrap.appendChild(input);
    const button=document.createElement('button');button.type='button';button.className='keyboardToggle';button.setAttribute('aria-label',`${label} mit Tastatur suchen`);button.title='Tastatur öffnen';button.textContent='⌨';wrap.appendChild(button);
    const lock=()=>{input.readOnly=true;input.inputMode='none';button.classList.remove('active')};
    button.addEventListener('pointerdown',event=>event.preventDefault());
    button.addEventListener('click',()=>{
      input.readOnly=false;input.inputMode='text';button.classList.add('active');input.blur();
      setTimeout(()=>{input.focus();try{input.setSelectionRange(input.value.length,input.value.length)}catch{}},20);
    });
    input.addEventListener('blur',()=>setTimeout(lock,260));
    const canonical=inputId==='outWarehouseLookup'?el('outWarehouse'):el('outStorageBin');
    canonical?.addEventListener('input',()=>{if(canonical.value)lock()});
  }
  function configureTargetLocation(){configureLookupKeyboard('outWarehouseLookup','Lagerort');configureLookupKeyboard('outStorageBinLookup','Lagerplatz')}

  function syncAll(){
    stripStepNumbers();layoutHeader();updateHeaderOffset();configureTargetLocation();prefillFillAmounts();syncFillMixPresentation();syncFaUi();normalizeConfirmations();localizeSuccess();syncBusyResult();syncHistory();
  }

  function install(){
    if(installed)return;installed=true;
    initHistory();ensureBusyOverlay();
    const style=document.createElement('style');style.id='processUxOptimizationStyles';style.textContent=`
.workerHeader{flex-wrap:nowrap!important;min-height:46px}.workerHeader>div:first-child{min-width:0;flex:1}.workerHeader .title{font-size:18px!important;line-height:1.15;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}.workerHeader .sub{display:none!important}.workerHeader #headerPersonnelName{margin:0!important;max-width:38vw!important;white-space:nowrap;overflow:hidden;text-overflow:ellipsis;flex:0 1 auto}.workerHeader .devSwitch{flex:0 0 auto}.shellHomeButton{display:none!important}.workerNext{top:var(--fam-header-height,58px)!important}.keyboardLookupWrap{position:relative}.keyboardLookupWrap .importantInput{padding-right:52px}.keyboardToggle{position:absolute;right:5px;top:10px;z-index:3;width:40px;min-width:40px;min-height:36px;padding:5px;border-radius:8px;background:#e7edf2;color:#314451;font-size:20px;line-height:1}.keyboardToggle.active{background:#1769aa;color:#fff}.bookingBusyOverlay{position:fixed;inset:0;z-index:20000;display:flex;align-items:center;justify-content:center;padding:20px;background:rgba(7,18,27,.82)}.bookingBusyOverlay.hidden{display:none!important}.bookingBusyCard{width:min(430px,100%);padding:26px 20px;border-radius:18px;background:#fff;text-align:center;box-shadow:0 18px 60px rgba(0,0,0,.4)}.bookingBusyCard b{display:block;margin-top:14px;font-size:20px;color:#173f5b}.bookingBusyCard span{display:block;margin-top:8px;font-size:14px;line-height:1.4;color:#526573}.bookingSpinner{width:58px;height:58px;margin:0 auto;border:6px solid #dbe8f1;border-top-color:#1769aa;border-radius:50%;animation:famSpin .85s linear infinite}@keyframes famSpin{to{transform:rotate(360deg)}}@media(max-width:680px){.workerHeader{gap:6px!important}.workerHeader .title{font-size:16px!important}.workerHeader #headerPersonnelName{max-width:33vw!important;font-size:11px!important;padding:3px 5px!important}.workerHeader .devSwitch{font-size:9px!important}.workerNext{top:var(--fam-header-height,54px)!important}}
`;
    document.head.appendChild(style);

    const header=document.querySelector('header');if(header&&typeof ResizeObserver!=='undefined')new ResizeObserver(updateHeaderOffset).observe(header);
    let pending=false;
    new MutationObserver(()=>{
      if(pending)return;pending=true;
      setTimeout(()=>{pending=false;syncAll()},0);
    }).observe(document.body,{childList:true,subtree:true,characterData:true,attributes:true,attributeFilter:['class']});

    document.addEventListener('click',event=>{
      if(event.target?.closest?.('.processChoice'))setTimeout(()=>{syncHistory();layoutHeader()},20);
      if(event.target?.closest?.('#processConfirm'))showBusy();
      if(event.target?.closest?.('#bookingConfirmStart'))showBusy();
    },true);
    document.addEventListener('change',event=>{
      if(event.target?.matches?.('#fillSources [data-pos]')){
        const input=event.target.closest('.processSource')?.querySelector('[data-amount]');
        if(input&&input.dataset.autoSuggested==='true'){input.value='';input.dataset.autoSuggested='true'}
        setTimeout(prefillFillAmounts,0);
      }
    },true);

    window.addEventListener('unhandledrejection',()=>hideBusy());
    window.addEventListener('error',()=>hideBusy());
    setText(document.querySelector('.processChoice[data-mode="fa-consumption"] span'),'Tank, Fertigungsauftrag und Ist-Verbrauch erfassen');
    syncAll();
    setInterval(syncAll,800);
  }

  function start(){
    let attempts=0;
    const timer=setInterval(()=>{
      attempts++;
      if(el('processChoiceStep')&&el('headerPersonnelName')&&el('fillSources')){clearInterval(timer);install()}
      else if(attempts>160)clearInterval(timer);
    },30);
  }

  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',start);else start();
})();
