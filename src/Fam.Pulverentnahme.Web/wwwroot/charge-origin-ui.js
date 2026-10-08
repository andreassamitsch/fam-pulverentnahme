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
    const fields=[['supplier','Lieferant'],['purchaseOrder','Bestellung'],['deliveryNote','Lieferschein'],['goodsReceipt','Wareneingang'],['productionOrder','Fertigungsauftrag']];
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
      render('ok',response.body.baseBatches.length+
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

  function closeOriginModal(){
    ++requestVersion;
    const previous=modalBack;
    modalBack=null;
    el('processModal')?.classList.add('hidden');
    document.body.classList.remove('scanModalOpen');
    if(typeof previous==='function'&&!el('inventoryProcess')?.classList.contains('hidden'))previous();
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
    el('processModalActions').innerHTML='<button id="chargeOriginClose" class="primary" type="button">Zurück zu Details</button>';
    el('chargeOriginClose').onclick=closeOriginModal;
    modal.classList.remove('hidden');
    document.body.classList.add('scanModalOpen');
    if(!query){
      modalRender('bad','Artikel oder Charge ist nicht gültig. Bitte Lagerübersicht erneut laden.','');
      return;
    }
    loadOrigin(query.article,query.batch,modalRender).catch(()=>{});
  }

  function bind(){
    if(!el('chargeOriginSearch'))return;
    el('chargeOriginSearch').onclick=search;
    el('chargeOriginScan').onclick=()=>scan().catch(()=>{});
    for(const id of ['chargeOriginArticle','chargeOriginBatch']){
      el(id).addEventListener('keydown',event=>{
        if(event.key==='Enter'){event.preventDefault();event.currentTarget.blur();search()}
      });
    }
    const style=document.createElement('style');
    style.textContent='.originHeading{padding:12px;border:1px solid #cbd9e1;border-radius:11px;margin:12px 0;background:#edf4f8}.originHeading b,.originHeading span{display:block;overflow-wrap:anywhere}.originHeading span{margin-top:3px;color:#465e6e}.originResultTitle{font-size:19px;margin:15px 0 10px}.originBatchList{display:grid;gap:10px;margin-top:10px}.originBatch{border:1px solid #cbd9e1;border-radius:12px;padding:13px;background:white;overflow-wrap:anywhere}.originBatch>b{display:block;font-size:18px}.originBatch>span{display:block;color:#4d6574;margin-top:2px}.originMetadataList{border-top:1px solid #dbe6eb;margin-top:10px;padding-top:7px;display:grid;gap:5px}.originMetadata{display:flex;justify-content:space-between;gap:8px;flex-wrap:wrap;font-size:14px}.originMetadata span{color:#526a79}.originMetadata b{font-weight:700}';
    document.head.appendChild(style);
  }

  window.FamChargeOriginUi={show,reset};
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',bind);
  else bind();
})();