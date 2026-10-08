'use strict';

// Read-only Oxaion charge origin: both the dedicated process and inventory details use
// the same API. No local fallback/cache may be interpreted as ERP charge provenance.
(function(){
  const el=id=>document.getElementById(id);
  const escapeHtml=value=>String(value??'').replace(/[&<>"']/g,char=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[char]));
  let requestVersion=0;
  let modalBack=null;

  function validate(article,batch){
    const normalizedArticle=String(article||'').trim().toUpperCase();
    const normalizedBatch=String(batch||'').trim();
    if(!/^(RP|PB)\.[A-Z0-9._-]+$/.test(normalizedArticle)){
      throw new Error('Eine gültige Pulverartikelnummer eingeben (RP.* oder PB.*).');
    }
    if(!normalizedBatch)throw new Error('Bitte eine Chargennummer eingeben.');
    if(normalizedArticle.length>40||normalizedBatch.length>100){
      throw new Error('Artikelnummer oder Chargennummer ist zu lang.');
    }
    return {article:normalizedArticle,batch:normalizedBatch};
  }

  function parseLabel(raw){
    if(typeof parseChargeQr==='function')return parseChargeQr(raw);
    const fields=String(raw||'').trim().split('+++').map(value=>value.trim());
    if(fields.length!==2||!fields[0]||!fields[1]){
      throw new Error('Ungültiges Chargenetikett. Erwartet: Artikel+++Charge.');
    }
    return {article:fields[0],batch:fields[1]};
  }

  function resultHtml(data,query){
    if(!data||!Array.isArray(data.baseBatches)){
      throw new Error('Oxaion hat keine gültige Chargenherkunft-Antwort geliefert.');
    }
    const list=data.baseBatches;
    const header=`<div class="originHeading"><b>${escapeHtml(data.article||query.article)}</b><span>Charge ${escapeHtml(data.batch||query.batch)}</span></div>`;
    if(!list.length){
      return header+'<div class="status warn">Keine Grundchargen ermittelt. Bitte die Chargenherkunft in Oxaion prüfen. Es wurden keine Materialbuchungen durchgeführt.</div>';
    }
    const fields=[['supplier','Lieferant'],['purchaseOrder','Bestellung'],['deliveryNote','Lieferschein'],['goodsReceipt','Wareneingang']];
    return header+`<h3 class="originResultTitle">${list.length} eindeutige Grundcharge${list.length===1?'':'n'}</h3>`+
      '<div class="originBatchList">'+list.map(item=>{
        const details=fields.filter(([name])=>String(item?.[name]||'').trim())
          .map(([name,label])=>`<div class="originMetadata"><span>${label}</span><b>${escapeHtml(item[name])}</b></div>`).join('');
        return `<article class="originBatch"><b>${escapeHtml(item?.batch||'—')}</b><span>Artikel ${escapeHtml(item?.article||'—')}</span>${details?`<div class="originMetadataList">${details}</div>`:''}</article>`;
      }).join('')+'</div>';
  }

  async function loadOrigin(article,batch,render){
    const query=validate(article,batch),ticket=++requestVersion;
    render('loading','Chargenherkunft wird online aus Oxaion gelesen …','');
    try{
      // The existing API helper returns {ok,status,body}; never retain/cache this response.
      const response=await api('/api/charge-origin?article='+encodeURIComponent(query.article)+'&batch='+encodeURIComponent(query.batch));
      if(ticket!==requestVersion)return;
      if(!response.ok){
        throw new Error(response.body?.message||response.body?.detail||
          'Oxaion konnte die Chargenherkunft nicht lesen. Bitte Verbindung prüfen und erneut versuchen.');
      }
      const body=resultHtml(response.body,query);
      render(response.body.baseBatches.length?'ok':'warn',response.body.baseBatches.length+
        ' Grundcharge(n) aus Oxaion ermittelt.',body);
    }catch(error){
      if(ticket===requestVersion)render('bad',
        'Chargenherkunft konnte nicht ermittelt werden: '+String(error?.message||error)+
        ' Bitte Artikel/Charge und die Oxaion-Verbindung prüfen.','');
    }
  }

  function panelRender(kind,status,content){
    const s=el('chargeOriginStatus'),result=el('chargeOriginResult');
    if(s){s.className='status '+(kind==='loading'?'neutral':kind);s.textContent=status}
    if(result)result.innerHTML=content;
    if(el('chargeOriginSearch'))el('chargeOriginSearch').disabled=kind==='loading';
    if(el('chargeOriginScan'))el('chargeOriginScan').disabled=kind==='loading';
  }

  function search(){
    const article=el('chargeOriginArticle')?.value||'',batch=el('chargeOriginBatch')?.value||'';
    try{
      const valid=validate(article,batch);
      el('chargeOriginArticle').value=valid.article;
      el('chargeOriginBatch').value=valid.batch;
      loadOrigin(valid.article,valid.batch,panelRender).catch(()=>{});
    }catch(e){++requestVersion;panelRender('bad',e.message,'')}
  }

  async function scan(){
    try{
      if(typeof scanQrCode!=='function')throw new Error('QR-Scanner ist nicht verfügbar.');
      const raw=await scanQrCode({title:'Chargenetikett scannen',help:'Artikel+++Charge auf dem Pulveretikett scannen.'});
      const data=parseLabel(raw),valid=validate(data.article,data.batch);
      el('chargeOriginArticle').value=valid.article;
      el('chargeOriginBatch').value=valid.batch;
      search();
    }catch(e){
      if(e?.name==='AbortError')return;
      ++requestVersion;
      panelRender('bad',String(e?.message||e),'');
    }
  }

  function reset(){
    ++requestVersion;
    for(const id of ['chargeOriginArticle','chargeOriginBatch'])if(el(id))el(id).value='';
    panelRender('neutral','Artikel und Charge eingeben oder Etikett scannen.','');
  }

  function modalRender(kind,status,content){
    const body=el('processModalBody');
    if(!body||el('processModal')?.classList.contains('hidden'))return;
    body.innerHTML=`<div class="status ${kind==='loading'?'neutral':kind}" role="status">${escapeHtml(status)}</div>`+content;
  }

  function closeOriginModal(returnToDetails=false){
    ++requestVersion;
    const previous=modalBack;
    modalBack=null;
    el('processModal')?.classList.add('hidden');
    document.body.classList.remove('scanModalOpen');
    if(returnToDetails&&typeof previous==='function'&&!el('inventoryProcess')?.classList.contains('hidden'))previous();
  }

  function show(article,batch,onBack=null){
    let query;
    try{query=validate(article,batch)}
    catch(e){
      ++requestVersion;
      query=null;
    }
    modalBack=onBack;
    const modal=el('processModal');
    if(!modal)return;
    el('processModalTitle').textContent='Chargenherkunft';
    el('processModalBody').innerHTML='';
    el('processModalActions').innerHTML='<button id="chargeOriginBack" class="secondary" type="button">Zurück zu Details</button><button id="chargeOriginClose" class="originCloseButton" type="button">Schließen</button>';
    el('chargeOriginBack').onclick=()=>closeOriginModal(true);
    el('chargeOriginClose').onclick=()=>closeOriginModal(false);
    modal.classList.remove('hidden');
    document.body.classList.add('scanModalOpen');
    if(!query){
      modalRender('bad','Artikel oder Charge ist nicht gültig. Bitte Lagerübersicht erneut laden.','');
      return;
    }
    loadOrigin(query.article,query.batch,modalRender).catch(()=>{});
  }

  function ensureStyle(){
    if(el('famChargeOriginStyles'))return;
    const style=document.createElement('style');
    style.id='famChargeOriginStyles';
    style.textContent=`
      .originHeading{display:grid;gap:4px;padding:13px 14px;border:1px solid #c7d6df;border-radius:12px;margin:12px 0 18px;background:#edf4f8}
      .originHeading b{display:block;font-size:18px;line-height:1.25;overflow-wrap:anywhere}
      .originHeading span{display:block;color:#465e6e;font-size:14px;line-height:1.35;overflow-wrap:anywhere}
      .originResultTitle{font-size:19px;margin:16px 0 11px;line-height:1.3}
      .originBatchList{display:grid;gap:12px;margin:10px 0 16px}
      .originBatch{display:block;border:1px solid #cbd9e1;border-left:5px solid #2376a7;border-radius:12px;padding:13px 14px;background:#fff;overflow-wrap:anywhere}
      .originBatch>b{display:block;font-size:21px;line-height:1.2;color:#182b38}
      .originBatch>span{display:block;color:#526a79;margin-top:5px;font-size:14px;line-height:1.3}
      .originMetadataList{border-top:1px solid #dbe6eb;margin-top:11px;padding-top:9px;display:grid;gap:9px}
      .originMetadata{display:grid;grid-template-columns:minmax(90px,35%) minmax(0,1fr);gap:9px;align-items:start;font-size:15px;line-height:1.35}
      .originMetadata span{display:block;color:#526a79}
      .originMetadata b{display:block;font-weight:700;overflow-wrap:anywhere}
      #processModalActions:has(.originCloseButton){display:flex;gap:10px;flex-wrap:wrap}
      .originCloseButton{background:#bc3029!important;border-color:#bc3029!important;color:#fff!important;font-weight:750;cursor:pointer}
      .originCloseButton:focus-visible{outline:3px solid #f1a7a2;outline-offset:2px}
      .originCloseButton:active{background:#92221c!important}
      @media(max-width:620px){.originBatch{padding:12px}.originMetadata{grid-template-columns:minmax(80px,38%) minmax(0,1fr);font-size:14px}}
    `;
    document.head.appendChild(style);
  }

  // process-mode creates the form asynchronously after DOMContentLoaded. Binding at
  // DOMContentLoaded alone silently missed both buttons in the first 0.1.11 build.
  // process-mode explicitly invokes bind() after ensureUi() creates the panel.
  // Safe to call repeatedly after navigation, and styles are loaded independently.
  function bind(){
    ensureStyle();
    const searchButton=el('chargeOriginSearch'),scanButton=el('chargeOriginScan');
    if(!searchButton||!scanButton||searchButton.dataset.originBound==='true')return;
    searchButton.dataset.originBound='true';
    searchButton.onclick=search;
    scanButton.onclick=()=>scan().catch(()=>{});
    for(const id of ['chargeOriginArticle','chargeOriginBatch']){
      el(id).addEventListener('keydown',event=>{
        if(event.key==='Enter'){event.preventDefault();event.currentTarget.blur();search()}
      });
    }
  }

  window.FamChargeOriginUi={show,reset,bind};
  ensureStyle();
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',bind);
  else bind();
})();