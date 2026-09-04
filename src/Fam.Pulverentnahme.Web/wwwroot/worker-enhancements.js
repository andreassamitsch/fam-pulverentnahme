'use strict';

// Worker-facing refinements layered on top of worker-ui.js. This file changes presentation only;
// backend validation, transaction state, idempotency and Oxaion recovery remain unchanged.
(function(){
  let bookingResultPending=null;
  let bookingResultResolve=null;
  let bookingResultOpen=false;

  function currentAmountInput(){
    const e=document.activeElement;
    return e&&e.matches?.('[data-field="amountKg"]')?e:null;
  }

  // Never pull focus/scroll away while the operator is still typing a quantity such as 1,012.
  const baseFocusWorkerElement=typeof focusWorkerElement==='function'?focusWorkerElement:null;
  if(baseFocusWorkerElement){
    focusWorkerElement=function(element){
      if(!element)return;
      if(typeof setCurrentAction==='function')setCurrentAction(element,true);
      const amount=currentAmountInput();
      if(amount&&element!==amount)return;
      setTimeout(()=>{try{element.scrollIntoView({behavior:'smooth',block:'center'})}catch{}},30);
    };
  }

  // Keep the SOLL and IST color squares visible on every scan comparison. For a correct article,
  // IST uses the same Oxaion EFA01/EFA02 characteristics. A wrong article is never assigned the
  // target colors; until a separate characteristic read is available it is shown as unknown.
  function targetColors(){
    const result=machineStock?.recognitionColors||null;
    const normalize=v=>{
      if(typeof recognitionHex==='function')return recognitionHex(v);
      const h=String(v||'').trim().replace(/^#/,'').toUpperCase();
      return /^[0-9A-F]{6}$/.test(h)?h:null;
    };
    return {left:normalize(result?.color1?.hex),right:normalize(result?.color2?.hex)};
  }
  function colorBox(colors,label){
    const left=colors?.left?`background:#${colors.left}`:'';
    const right=colors?.right?`background:#${colors.right}`:'';
    const missingLeft=colors?.left?'':' recognitionColorMissing';
    const missingRight=colors?.right?'':' recognitionColorMissing';
    return `<div class="scanMiniSwatch" aria-label="${esc(label)}"><span class="${missingLeft.trim()}" style="${left}"></span><span class="${missingRight.trim()}" style="${right}"></span></div>`;
  }
  function enhancedScannedHtml(scan,state='ok'){
    if(!scan){
      return `<div class="scanCompareSide scanCompareActual pending"><span class="scanCompareLabel">IST / SCAN</span><div class="scanCompareContent">${colorBox(null,'Ist-Erkennungsfarben noch nicht bekannt')}<div class="scanPending">QR-Code noch nicht erkannt</div></div></div>`;
    }
    const sameArticle=String(scan.article||'').toUpperCase()===String($('article')?.value||'').toUpperCase();
    const colors=sameArticle?targetColors():null;
    const icon=state==='ok'?'✓':'!';
    return `<div class="scanCompareSide scanCompareActual ${state}"><span class="scanCompareLabel">IST / GESCANNT</span><div class="scanCompareContent">${colorBox(colors,sameArticle?'Ist-Erkennungsfarben':'Ist-Erkennungsfarben des falschen Artikels nicht übernommen')}<div class="scanActualText"><span class="scanActualIcon">${icon}</span><b>${esc(scan.article||'Unbekannt')}</b><span>Charge ${esc(scan.batch||'–')}</span></div></div></div>`;
  }
  if(typeof workerScannedHtml==='function')workerScannedHtml=enhancedScannedHtml;

  // Step 4 must show the physical source position, not only batch + quantity.
  if(typeof renderWorkerBookSummary==='function'){
    renderWorkerBookSummary=function(){
      const e=$('workerBookSummary');if(!e)return;
      const machine=$('oldMixWarehouse')?.value,article=$('article')?.value,articleText=$('articleText')?.value,cards=sourceCards();
      if(!machine||!article){e.textContent='Zuerst Maschinentank und Nachfüllcharge erfassen.';return}
      if(!cards.length){e.innerHTML=`<b>${esc(machine)} · ${esc(article)} ${esc(articleText||'')}</b><br>Noch keine Nachfüllcharge gescannt.`;return}
      const lines=cards.map((c,i)=>{
        const batch=c._scan?.batch||'–';
        const amount=String(sf(c,'amountKg')?.value||'').trim();
        const place=c._selected?`${c._selected.warehouse}${c._selected.storageBin?' / '+c._selected.storageBin:''}`:'Entnahmeort noch offen';
        return `<div class="workerBookLine"><div>${i+1}. <b>${esc(batch)}</b> · ${amount?esc(amount)+' kg':'Menge fehlt'}</div><div class="workerBookLocation">↳ ${esc(place)}</div></div>`;
      });
      e.innerHTML=`<b>${esc(machine)} · ${esc(article)} ${esc(articleText||'')}</b><div class="workerBookLines">${lines.join('')}</div>`;
    };
  }

  function ensureResultUi(){
    if(!$('bookingResultModal')){
      const modal=document.createElement('div');
      modal.id='bookingResultModal';modal.className='bookingResultModal hidden';
      modal.setAttribute('role','alertdialog');modal.setAttribute('aria-modal','true');
      modal.setAttribute('aria-labelledby','bookingResultTitle');modal.setAttribute('aria-describedby','bookingResultMessage');
      modal.innerHTML='<div id="bookingResultDialog" class="bookingResultDialog"><div id="bookingResultIcon" class="bookingResultIcon">✓</div><h2 id="bookingResultTitle">Buchung abgeschlossen</h2><div id="bookingResultMessage" class="bookingResultMessage"></div><div id="bookingResultAction" class="bookingResultAction"></div><div id="bookingResultDocument" class="bookingResultDocument devOnly hidden"></div><button id="bookingResultAcknowledge" class="bookingResultAcknowledge" type="button">Verstanden</button></div>';
      document.body.appendChild(modal);
    }
    if(!document.getElementById('workerEnhancementStyles')){
      const style=document.createElement('style');style.id='workerEnhancementStyles';
      style.textContent=`
.scanMiniSwatch .recognitionColorMissing{background:repeating-linear-gradient(135deg,#edf1f4 0,#edf1f4 7px,#d8e0e5 7px,#d8e0e5 14px)!important}.scanActualText{min-width:0}.workerBookLines{margin-top:7px}.workerBookLine{margin:7px 0}.workerBookLocation{margin:2px 0 0 18px;font-size:12px;font-weight:750;color:#526573}.bookingResultModal{position:fixed;inset:0;z-index:1100;background:rgba(7,18,27,.86);display:flex;align-items:center;justify-content:center;padding:16px}.bookingResultModal.hidden{display:none}.bookingResultDialog{width:min(620px,100%);max-height:calc(100vh - 32px);overflow:auto;background:#fff;border-radius:18px;padding:24px;box-shadow:0 18px 64px rgba(0,0,0,.42);border-top:9px solid #23875f}.bookingResultDialog.warning{border-top-color:#c27b00}.bookingResultDialog.error{border-top-color:#c6372e}.bookingResultIcon{display:grid;place-items:center;width:72px;height:72px;border-radius:50%;margin:0 auto 12px;background:#23875f;color:#fff;font-size:44px;font-weight:950}.bookingResultDialog.warning .bookingResultIcon{background:#c27b00}.bookingResultDialog.error .bookingResultIcon{background:#c6372e}.bookingResultDialog h2{text-align:center;font-size:25px;margin:0 0 14px;color:#166b49}.bookingResultDialog.warning h2{color:#895800}.bookingResultDialog.error h2{color:#9c241d}.bookingResultMessage{font-size:18px;font-weight:800;line-height:1.45;text-align:center;color:#17212b;margin:10px 0 16px}.bookingResultAction{padding:13px 14px;border-radius:11px;background:#e6f4ed;border:1px solid #9fd2b8;color:#146c47;font-size:16px;font-weight:850;line-height:1.45}.bookingResultDialog.warning .bookingResultAction{background:#fff5d8;border-color:#e8c660;color:#745000}.bookingResultDialog.error .bookingResultAction{background:#fff0ee;border-color:#e3a09b;color:#8f2b24}.bookingResultDocument{margin-top:10px;text-align:center;font-size:12px;color:#5a6d78}.bookingResultAcknowledge{display:block;width:100%;margin-top:18px;min-height:58px;font-size:18px;font-weight:900;background:#23875f;color:#fff}.bookingResultDialog.warning .bookingResultAcknowledge{background:#c27b00}.bookingResultDialog.error .bookingResultAcknowledge{background:#c6372e}@media(max-width:680px){.bookingResultModal{padding:10px}.bookingResultDialog{padding:19px;border-radius:16px}.bookingResultDialog h2{font-size:23px}.bookingResultMessage{font-size:16px}.bookingResultAction{font-size:15px}}
`;
      document.head.appendChild(style);
    }
  }

  function resultPresentation(ok,title,data){
    const status=String(data?.status||'').toUpperCase();
    const message=String(data?.message||data?.detail||'');
    const transportUnknown=!ok&&(title?.includes('Verbindung')||/Nicht erneut buchen/i.test(message));
    if(ok||status==='SUCCESS')return{tone:'success',icon:'✓',title:'Buchung erfolgreich',message:'Die Pulvernachfüllung wurde in oxaion vollständig gebucht, verifiziert und abgeschlossen.',action:'Nach „Verstanden“ wird dieser Vorgang geleert. Du bleibst angemeldet und kannst den nächsten Maschinentank scannen.',button:'Verstanden'};
    if(status==='UNCERTAIN'||status==='MANUAL_REVIEW_REQUIRED'||transportUnknown)return{tone:'warning',icon:'!',title:'Buchungsergebnis unklar',message:message||'Der Ausgang der Buchung ist nicht eindeutig bestätigt.',action:'NICHT erneut buchen. Produktionsleitung informieren und zuerst den Status in oxaion über die Recovery-Funktion prüfen.',button:'Verstanden – Status prüfen'};
    if(status==='REJECTED')return{tone:'error',icon:'!',title:'Buchung abgelehnt',message:message||friendly(data),action:'Oxaion hat eindeutig abgelehnt. Ursache beheben und erst danach den dokumentierten bewussten Neuversuch verwenden.',button:'Verstanden – Ursache beheben'};
    if(status==='AUTH_REQUIRED'||status==='AUTH_CONFLICT')return{tone:'warning',icon:'!',title:'Anmeldung prüfen',message:message||friendly(data),action:'Erneut anmelden. Einen bestehenden Vorgang nicht blind erneut buchen; zuerst dessen Status klären.',button:'Verstanden'};
    return{tone:'error',icon:'!',title:(title||'Buchung nicht durchgeführt').replace(/^[✓⚠⛔]\s*/,''),message:message||friendly(data),action:'Die angezeigte Ursache beheben. Wenn der Buchungsausgang nicht eindeutig ist, Produktionsleitung informieren und nicht erneut buchen.',button:'Verstanden'};
  }

  function closeResultDialog(){
    const modal=$('bookingResultModal');if(modal)modal.classList.add('hidden');
    document.body.classList.remove('scanModalOpen');bookingResultOpen=false;
    const resolve=bookingResultResolve;bookingResultResolve=null;if(resolve)resolve();
  }
  function showResultDialog(ok,title,data){
    ensureResultUi();
    const modal=$('bookingResultModal'),dialog=$('bookingResultDialog'),icon=$('bookingResultIcon'),titleEl=$('bookingResultTitle'),message=$('bookingResultMessage'),action=$('bookingResultAction'),button=$('bookingResultAcknowledge'),doc=$('bookingResultDocument');
    const p=resultPresentation(ok,title,data);bookingResultOpen=true;
    dialog.className=`bookingResultDialog ${p.tone}`;icon.textContent=p.icon;titleEl.textContent=p.title;message.textContent=p.message;action.textContent=p.action;button.textContent=p.button;
    if(doc){doc.textContent=data?.documentNo?`Lagerbeleg ${data.documentNo}`:'';doc.classList.toggle('hidden',!data?.documentNo)}
    modal.classList.remove('hidden');document.body.classList.add('scanModalOpen');
    return new Promise(resolve=>{
      bookingResultResolve=resolve;
      button.onclick=()=>{button.onclick=null;closeResultDialog();const status=String(data?.status||'').toUpperCase();if(status==='UNCERTAIN'||status==='MANUAL_REVIEW_REQUIRED'||status==='REJECTED'||status==='AUTH_REQUIRED'||status==='AUTH_CONFLICT')setTimeout(()=>$('recovery')?.scrollIntoView({behavior:'smooth',block:'start'}),30)};
      setTimeout(()=>button.focus(),20);
    });
  }

  const baseShowResult=typeof showResult==='function'?showResult:null;
  if(baseShowResult){
    showResult=function(ok,title,data){
      baseShowResult(ok,title,data);
      bookingResultPending=showResultDialog(ok,title,data);
    };
  }

  async function resetSuccessfulOrPrewriteOperation(){
    await dbClear();active=null;renderRecovery();
    if($('oldMixWarehouse'))$('oldMixWarehouse').value='';
    if(typeof clearMachineInfo==='function')clearMachineInfo();
    for(const id of ['targetWarehouse','targetWarehouseText','targetStorageBin','productionDate','bookingDate','bookingText'])if($(id))$(id).value='';
    if($('machineScanValue'))$('machineScanValue').textContent='Noch nicht gescannt';
    if($('stockStatus')){$('stockStatus').className='status neutral';$('stockStatus').textContent='Bitte Maschinentank scannen.'}
    $('stockRefreshBtn')?.classList.add('hidden');$('result')?.classList.add('hidden');
    updateBookState();renderSummary();if(typeof refreshWorkerFlow==='function')refreshWorkerFlow();
  }
  if(typeof afterTerminal==='function'){
    afterTerminal=async function(){
      if(bookingResultPending){try{await bookingResultPending}finally{bookingResultPending=null}}
      await resetSuccessfulOrPrewriteOperation();
    };
  }

  function init(){
    ensureResultUi();
    document.addEventListener('focusout',e=>{if(e.target?.matches?.('[data-field="amountKg"]'))setTimeout(()=>{if(typeof refreshWorkerFlow==='function')refreshWorkerFlow()},40)});
    if(typeof renderWorkerBookSummary==='function')renderWorkerBookSummary();
    if(typeof refreshWorkerFlow==='function')refreshWorkerFlow();
  }
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',()=>setTimeout(init,0));else setTimeout(init,0);
})();
