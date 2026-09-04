'use strict';

// Separate worker processes. The additional processes intentionally stop at the last safe
// preparation boundary until their write-capable Oxaion sequences are confirmed from real data.
(function(){
  const MODES={REPLENISH:'replenish',TANK_OUT:'tank-out',FILL_NEW:'fill-new',FA_CONSUMPTION:'fa-consumption'};
  let mode=null;
  let outState=null;
  let fillState={tank:null,article:'',articleText:'',colors:null,sources:[],targetBatch:''};
  let faState={tank:null,row:null,colors:null,order:null,amount:''};
  let baseRefresh=null;

  const el=id=>document.getElementById(id);
  const hideLegacy=hidden=>['machineStep','sourcesSection','mixSection','bookingStep','result'].forEach(id=>el(id)?.classList.toggle('processModeHidden',hidden));
  const authenticated=()=>typeof isWorkerAuthenticated==='function'?isWorkerAuthenticated():Boolean(selectedPersonnel);
  const swatch=colors=>{
    const h=v=>String(v||'').trim().replace(/^#/,'');
    const a=h(colors?.color1?.hex),b=h(colors?.color2?.hex);
    return `<span class="processSwatch"><i style="${/^[0-9A-Fa-f]{6}$/.test(a)?`background:#${a}`:''}"></i><i style="${/^[0-9A-Fa-f]{6}$/.test(b)?`background:#${b}`:''}"></i></span>`;
  };
  const qty=v=>Number(v||0).toFixed(3).replace('.',',');
  const key=p=>`${p?.warehouse||''}\u001f${p?.storageBin||''}\u001f${p?.batch||''}`;
  const modeLabel=value=>value===MODES.REPLENISH?'Pulver nachfüllen':value===MODES.TANK_OUT?'Pulver aus Tank auslagern':value===MODES.FILL_NEW?'Neues Pulver in Tank füllen':'Pulver auf Fertigungsauftrag buchen';

  function ensureUi(){
    if(el('processChoiceStep'))return;
    const login=el('loginStep');
    const choice=document.createElement('section');
    choice.id='processChoiceStep';choice.className='card stepCard lockedStep';
    choice.innerHTML=`<h2>Vorgang auswählen</h2><div class="processGrid">
      <button type="button" class="processChoice" data-mode="${MODES.REPLENISH}"><b>Pulver nachfüllen</b><span>Vorhandenes Pulver im Tank mit Charge(n) ergänzen</span></button>
      <button type="button" class="processChoice" data-mode="${MODES.TANK_OUT}"><b>Pulver aus Tank auslagern</b><span>Tank scannen und kompletten Tankbestand auf Lagerort / Lagerplatz vorbereiten</span></button>
      <button type="button" class="processChoice" data-mode="${MODES.FILL_NEW}"><b>Neues Pulver in Tank füllen</b><span>Leeren Tank scannen, Charge(n) scannen und Mengen eingeben</span></button>
      <button type="button" class="processChoice" data-mode="${MODES.FA_CONSUMPTION}"><b>Pulver auf Fertigungsauftrag buchen</b><span>Tank scannen, Fertigungsauftrag scannen und tatsächlichen Verbrauch eingeben</span></button>
    </div><div id="processSelected" class="status neutral hidden"></div>`;
    login.after(choice);

    const out=document.createElement('section');out.id='tankOutProcess';out.className='processPanel hidden';
    out.innerHTML=`<section class="card stepCard"><h2>2 · Maschinentank scannen</h2><div class="actions"><button id="outTankScan" class="primary" type="button">Maschinentank scannen</button></div><div id="outTankStatus" class="status neutral">Tank-QR enthält nur den Tanklagerort.</div><div id="outTankData" class="processData hidden"></div></section>
      <section id="outDestinationStep" class="card stepCard lockedStep"><h2>3 · Ziel im Pulverlager</h2><label class="importantLabel">Lagerort<input id="outWarehouse" class="importantInput" autocomplete="off" placeholder="Lagerort"></label><label class="importantLabel">Lagerplatz<input id="outStorageBin" class="importantInput" autocomplete="off" placeholder="Lagerplatz"></label><div class="footerNote">Die komplette aktuell in Oxaion gefundene Tankmenge wird für die Auslagerung vorbereitet.</div></section>
      <section class="card stepCard lockedStep"><h2>4 · Buchen</h2><div id="outSummary" class="workerBookSummary">Tank und Ziel erfassen.</div><div class="status warn processWriteBoundary">Buchung noch nicht freigegeben: Der schreibende Oxaion-Ablauf Maschinentank → Pulverlager ist noch nicht bestätigt.</div><div class="actions"><button class="primary" type="button" disabled>Auslagerung buchen</button></div></section>`;
    choice.after(out);

    const fill=document.createElement('section');fill.id='fillNewProcess';fill.className='processPanel hidden';
    fill.innerHTML=`<section class="card stepCard"><h2>2 · Leeren Maschinentank scannen</h2><div class="actions"><button id="fillTankScan" class="primary" type="button">Maschinentank scannen</button></div><div id="fillTankStatus" class="status neutral">Für diesen Vorgang muss der Tank eindeutig leer sein.</div><div id="fillTankData" class="processData hidden"></div></section>
      <section id="fillSourcesStep" class="card stepCard lockedStep"><h2>3 · Neue Pulvercharge(n)</h2><div id="fillArticlePanel" class="processArticle hidden"></div><div id="fillSources"></div><div class="actions"><button id="fillChargeScan" class="primary" type="button" disabled>Charge scannen</button></div><div id="fillSourceStatus" class="status neutral">Zuerst leeren Tank scannen.</div></section>
      <section class="card stepCard lockedStep"><h2>4 · Buchen</h2><div id="fillSummary" class="workerBookSummary">Tank und mindestens eine Charge erfassen.</div><div class="status warn processWriteBoundary">Buchung noch nicht freigegeben: Der schreibende Oxaion-Ablauf Pulverlager → leerer Maschinentank ist noch nicht bestätigt.</div><div class="actions"><button class="primary" type="button" disabled>Neue Befüllung buchen</button></div></section>`;
    out.after(fill);

    const fa=document.createElement('section');fa.id='faConsumptionProcess';fa.className='processPanel hidden';
    fa.innerHTML=`<section class="card stepCard"><h2>2 · Maschinentank scannen</h2><div class="actions"><button id="faTankScan" class="primary" type="button">Maschinentank scannen</button></div><div id="faTankStatus" class="status neutral">Tank-QR enthält nur den Tanklagerort.</div><div id="faTankData" class="processData hidden"></div></section>
      <section id="faOrderStep" class="card stepCard lockedStep"><h2>3 · Fertigungsauftrag scannen</h2><div class="actions"><button id="faOrderScan" class="primary" type="button" disabled>Fertigungsauftrag scannen</button></div><div id="faOrderStatus" class="status neutral">Zuerst Maschinentank scannen.</div><div id="faOrderData" class="processData hidden"></div><div class="footerNote">FA-QR: <b>Rohmaterial+++Fertigungsauftrag+++Maschinen-ID</b>. Der Rohmaterialartikel muss dem aktuellen Tankartikel entsprechen.</div></section>
      <section id="faAmountStep" class="card stepCard lockedStep"><h2>4 · Verbrauch eingeben</h2><label class="importantLabel">Tatsächlicher Verbrauch kg<input id="faConsumptionAmount" class="importantInput" inputmode="decimal" enterkeyhint="done" autocomplete="off" placeholder="Verbrauch in kg"></label><div id="faAmountStatus" class="status neutral">Zuerst Fertigungsauftrag scannen.</div></section>
      <section id="faBookStep" class="card stepCard lockedStep"><h2>5 · Buchen</h2><div id="faSummary" class="workerBookSummary">Tank, Fertigungsauftrag und Verbrauch erfassen.</div><div class="status warn processWriteBoundary">Buchung noch nicht freigegeben: Die Oxaion-FA-Materialrückmeldung / der BDE-PPS-Schreibablauf ist noch nicht technisch bestätigt.</div><div class="actions"><button class="primary" type="button" disabled>Verbrauch auf Fertigungsauftrag buchen</button></div></section>`;
    fill.after(fa);

    document.querySelectorAll('.processChoice').forEach(b=>b.onclick=()=>selectMode(b.dataset.mode));
    el('outTankScan').onclick=scanOutTank;
    el('fillTankScan').onclick=scanFillTank;
    el('fillChargeScan').onclick=scanFillCharge;
    el('faTankScan').onclick=scanFaTank;
    el('faOrderScan').onclick=scanFaOrder;
    el('faConsumptionAmount').addEventListener('input',()=>{faState.amount=el('faConsumptionAmount').value;refreshFa()});
    el('outWarehouse').addEventListener('input',refreshOut);el('outStorageBin').addEventListener('input',refreshOut);
    const style=document.createElement('style');style.textContent=`.processModeHidden{display:none!important}.processGrid{display:grid;gap:10px}.processChoice{text-align:left;padding:16px;border:2px solid #bdd1dd;border-radius:14px;background:#fff;color:#162734}.processChoice b{display:block;font-size:18px}.processChoice span{display:block;margin-top:4px;color:#5b6e79}.processChoice.active{border-color:#1671ad;background:#eaf5fc}.processPanel>.card{margin-top:14px}.processData,.processArticle{padding:14px;border-radius:12px;background:#eef3f6;margin-top:10px}.processSwatch{display:inline-flex;width:78px;height:58px;border:3px solid #263b48;border-radius:9px;overflow:hidden;vertical-align:middle;margin-right:10px}.processSwatch i{width:50%;background:repeating-linear-gradient(135deg,#edf1f4 0,#edf1f4 7px,#d8e0e5 7px,#d8e0e5 14px)}.processSource{margin-top:12px;padding:13px;border:1px solid #cedbe2;border-radius:12px}.processSource select,.processSource input{width:100%;box-sizing:border-box}.processWriteBoundary{margin-top:12px}.status.warn{background:#fff5d8;color:#745000}.processSourceLine{font-weight:800;margin:5px 0}.processRemove{float:right}.processDetailGrid{display:grid;grid-template-columns:1fr 1fr;gap:8px;margin-top:8px}.processDetailGrid span{display:block;color:#61737e;font-size:12px}.processDetailGrid b{display:block}@media(max-width:620px){.processDetailGrid{grid-template-columns:1fr}}`;document.head.appendChild(style);
  }

  function clearProcessInputs(){
    for(const id of ['outWarehouse','outStorageBin','faConsumptionAmount'])if(el(id))el(id).value='';
    if(el('fillSources'))el('fillSources').innerHTML='';
    for(const id of ['outTankData','fillTankData','fillArticlePanel','faTankData','faOrderData'])el(id)?.classList.add('hidden');
    for(const id of ['outDestinationStep','fillSourcesStep','faOrderStep','faAmountStep','faBookStep'])el(id)?.classList.add('lockedStep');
    if(el('fillChargeScan'))el('fillChargeScan').disabled=true;
    if(el('faOrderScan'))el('faOrderScan').disabled=true;
  }

  function selectMode(next){
    if(!authenticated())return;
    mode=next;
    outState=null;
    fillState={tank:null,article:'',articleText:'',colors:null,sources:[],targetBatch:''};
    faState={tank:null,row:null,colors:null,order:null,amount:''};
    clearProcessInputs();
    document.querySelectorAll('.processChoice').forEach(b=>b.classList.toggle('active',b.dataset.mode===mode));
    el('processSelected').classList.remove('hidden');el('processSelected').textContent=modeLabel(mode);
    el('tankOutProcess').classList.toggle('hidden',mode!==MODES.TANK_OUT);
    el('fillNewProcess').classList.toggle('hidden',mode!==MODES.FILL_NEW);
    el('faConsumptionProcess').classList.toggle('hidden',mode!==MODES.FA_CONSUMPTION);
    hideLegacy(mode!==MODES.REPLENISH);refreshProcessUi();
    const target=mode===MODES.REPLENISH?el('machineScanBtn'):mode===MODES.TANK_OUT?el('outTankScan'):mode===MODES.FILL_NEW?el('fillTankScan'):el('faTankScan');
    setTimeout(()=>target?.scrollIntoView({behavior:'smooth',block:'center'}),30);
  }

  function refreshProcessUi(){
    ensureUi();const logged=authenticated();el('processChoiceStep').classList.toggle('lockedStep',!logged);
    document.querySelectorAll('.processChoice').forEach(b=>b.disabled=!logged);
    if(!logged){mode=null;hideLegacy(true);el('tankOutProcess').classList.add('hidden');el('fillNewProcess').classList.add('hidden');el('faConsumptionProcess').classList.add('hidden');el('processSelected').classList.add('hidden');return}
    if(!mode){hideLegacy(true);if(el('workerNextInstruction'))el('workerNextInstruction').textContent='Vorgang auswählen.';return}
    if(mode===MODES.TANK_OUT)refreshOut();
    if(mode===MODES.FILL_NEW)refreshFill();
    if(mode===MODES.FA_CONSUMPTION)refreshFa();
  }

  async function scanTankCommon(){
    const raw=await scanQrCode({title:'Maschinentank scannen',help:'Erwartet wird nur der Tank-Lagerort, z. B. EOS1.'});
    const code=String(raw||'').trim();if(!code||code.includes('+++'))throw new Error('Ungültiger Maschinentank-QR.');
    const allowed=machines.find(m=>m.warehouse.toUpperCase()===code.toUpperCase());if(!allowed)throw new Error(`Maschinentank ${code} ist nicht freigegeben.`);
    const r=await api('/api/machines/'+encodeURIComponent(allowed.warehouse)+'/stock');if(!r.ok)throw new Error(r.body?.detail||r.body?.error||'Tankbestand konnte nicht gelesen werden.');return{option:allowed,stock:r.body};
  }

  async function scanOutTank(){
    try{const x=await scanTankCommon();if(x.stock?.status!=='UNIQUE'||x.stock?.rows?.length!==1)throw new Error(x.stock?.status==='EMPTY'?'Der Tank ist leer. Es gibt nichts auszulagern.':x.stock?.message||'Tankbestand ist nicht eindeutig.');const row=x.stock.rows[0];outState={warehouse:x.option.warehouse,warehouseText:x.option.warehouseText||x.option.warehouse,row,colors:x.stock.recognitionColors};el('outTankStatus').className='status ok';el('outTankStatus').textContent='✓ Tankbestand eindeutig aus oxaion gelesen.';el('outTankData').classList.remove('hidden');el('outTankData').innerHTML=`${swatch(outState.colors)}<b>${esc(row.article)} ${esc(row.articleText||'')}</b><br>Charge ${esc(row.batch)} · kompletter Tankbestand ${qty(row.quantityKg)} kg`;el('outDestinationStep').classList.remove('lockedStep');el('outWarehouse').focus();refreshOut()}catch(e){if(e?.name!=='AbortError'){el('outTankStatus').className='status bad';el('outTankStatus').textContent='⛔ '+e.message}}
  }
  function refreshOut(){if(!outState)return;const w=el('outWarehouse').value.trim(),b=el('outStorageBin').value.trim();el('outSummary').innerHTML=`<b>${esc(outState.warehouse)} → ${esc(w||'Ziel fehlt')}</b><br>${esc(outState.row.article)} · Charge ${esc(outState.row.batch)} · ${qty(outState.row.quantityKg)} kg<br>Lagerplatz: ${esc(b||'fehlt')}`;if(mode===MODES.TANK_OUT&&el('workerNextInstruction'))el('workerNextInstruction').textContent=w&&b?'Auslagerung vorbereitet. Oxaion-Buchungsablauf noch offen.':'Ziel-Lagerort und Lagerplatz eingeben.';}

  async function scanFillTank(){
    try{const x=await scanTankCommon();if(x.stock?.status!=='EMPTY')throw new Error(x.stock?.status==='UNIQUE'?'Der Tank ist nicht leer. Für vorhandenes Pulver „Pulver nachfüllen“ verwenden oder den Tank zuerst auslagern.':x.stock?.message||'Tankleerstand ist nicht eindeutig.');fillState={tank:{warehouse:x.option.warehouse,warehouseText:x.option.warehouseText||x.option.warehouse},article:'',articleText:'',colors:null,sources:[],targetBatch:''};el('fillTankStatus').className='status ok';el('fillTankStatus').textContent=`✓ Maschinentank ${x.option.warehouse} ist in oxaion eindeutig leer.`;el('fillTankData').classList.remove('hidden');el('fillTankData').innerHTML=`<b>${esc(x.option.warehouse)} · ${esc(x.option.warehouseText||x.option.warehouse)}</b><br>Tank leer`;el('fillSourcesStep').classList.remove('lockedStep');el('fillChargeScan').disabled=false;el('fillSourceStatus').textContent='Erste Pulvercharge scannen.';refreshFill()}catch(e){if(e?.name!=='AbortError'){el('fillTankStatus').className='status bad';el('fillTankStatus').textContent='⛔ '+e.message}}
  }

  async function findPositions(article,batch){
    const wr=await api('/api/source-stock/warehouses?'+new URLSearchParams({article,excludeWarehouse:fillState.tank.warehouse}));if(!wr.ok)throw new Error(wr.body?.detail||'Lagerorte konnten nicht gelesen werden.');const matches=[];
    for(const w of Array.isArray(wr.body)?wr.body:[]){const pr=await api('/api/source-stock/positions?'+new URLSearchParams({article,warehouse:w.warehouse}));if(!pr.ok)throw new Error(`${w.warehouse}: Bestandspositionen konnten nicht gelesen werden.`);for(const p of Array.isArray(pr.body)?pr.body:[])if(String(p.batch||'')===batch&&!fillState.sources.some(s=>s.selected&&key(s.selected)===key(p)))matches.push(p)}
    return[...new Map(matches.map(p=>[key(p),p])).values()];
  }
  async function scanFillCharge(){
    try{const raw=await scanQrCode({title:'Neue Pulvercharge scannen',help:'Erwartet: Artikel+++Charge'});const scan=parseChargeQr(raw);if(fillState.article&&scan.article.toUpperCase()!==fillState.article.toUpperCase())throw new Error(`Falscher Artikel: ${scan.article}. Für diesen Tankvorgang ist ${fillState.article} festgelegt.`);const positions=await findPositions(scan.article,scan.batch);if(!positions.length)throw new Error(`Charge ${scan.batch} wurde auf keinem zulässigen positiven Oxaion-Bestand gefunden.`);if(!fillState.article){fillState.article=scan.article;fillState.targetBatch=generatedBatch(scan.article);el('fillArticlePanel').classList.remove('hidden');el('fillArticlePanel').innerHTML=`<b>${esc(scan.article)}</b><br>Artikel der neuen Tankbefüllung`;}const source={scan,positions,selected:positions.length===1?positions[0]:null,amount:''};fillState.sources.push(source);renderFillSources();refreshFill()}catch(e){if(e?.name!=='AbortError'){el('fillSourceStatus').className='status bad';el('fillSourceStatus').textContent='⛔ '+e.message}}
  }
  function renderFillSources(){
    const box=el('fillSources');box.innerHTML='';fillState.sources.forEach((s,i)=>{const d=document.createElement('div');d.className='processSource';const opts=s.positions.map((p,n)=>`<option value="${n}" ${s.selected===p?'selected':''}>${esc(p.warehouse)}${p.storageBin?' / '+esc(p.storageBin):''} · ${qty(p.quantityKg)} kg</option>`).join('');d.innerHTML=`<b>Charge ${i+1}: ${esc(s.scan.batch)}</b>${s.positions.length>1?`<label>Entnahmeort<select data-pos="${i}"><option value="">Auswählen …</option>${opts}</select></label>`:`<div class="processSourceLine">${esc(s.selected.warehouse)}${s.selected.storageBin?' / '+esc(s.selected.storageBin):''} · ${qty(s.selected.quantityKg)} kg verfügbar</div>`}<label>Einfüllmenge kg<input data-amount="${i}" inputmode="decimal" enterkeyhint="done" placeholder="Menge in kg" value="${esc(s.amount)}"></label>`;box.appendChild(d)});
    box.querySelectorAll('[data-pos]').forEach(x=>x.onchange=()=>{const s=fillState.sources[Number(x.dataset.pos)];s.selected=x.value===''?null:s.positions[Number(x.value)];refreshFill()});box.querySelectorAll('[data-amount]').forEach(x=>x.oninput=()=>{fillState.sources[Number(x.dataset.amount)].amount=x.value;refreshFill()});
  }
  function fillComplete(){return fillState.sources.length>0&&fillState.sources.every(s=>{const n=Number(String(s.amount).replace(',','.'));return s.selected&&Number.isFinite(n)&&n>0&&n<=Number(s.selected.quantityKg)+.0005})}
  function refreshFill(){if(!fillState.tank)return;const complete=fillComplete();el('fillChargeScan').disabled=fillState.sources.length>0&&!complete;el('fillSourceStatus').className='status '+(complete?'ok':'neutral');el('fillSourceStatus').textContent=complete?'✓ Charge(n), Entnahmeort und Mengen vollständig vorbereitet.':'Charge scannen bzw. Entnahmeort und Menge vollständig erfassen.';const lines=fillState.sources.map((s,i)=>`${i+1}. ${esc(s.scan.batch)} · ${esc(s.amount||'Menge fehlt')} kg · ${s.selected?esc(s.selected.warehouse+(s.selected.storageBin?' / '+s.selected.storageBin:'')):'Entnahmeort offen'}`).join('<br>');const target=fillState.targetBatch||'wird nach erstem Scan erzeugt';el('fillSummary').innerHTML=`<b>${esc(fillState.tank.warehouse)} · ${esc(fillState.article||'Artikel noch offen')}</b><br>${lines||'Noch keine Charge.'}<br>Neue Mix-Charge: <b>${esc(target)}</b>`;if(mode===MODES.FILL_NEW&&el('workerNextInstruction'))el('workerNextInstruction').textContent=complete?'Neue Befüllung vorbereitet. Oxaion-Buchungsablauf noch offen.':'Neue Pulvercharge scannen bzw. Menge vollständig erfassen.';}

  function parseFaQr(raw){
    const parts=String(raw||'').trim().split('+++').map(x=>x.trim());
    if(parts.length!==3||parts.some(x=>!x))throw new Error('Ungültiger Fertigungsauftrag-QR. Erwartet wird genau: Rohmaterial+++Fertigungsauftrag+++Maschinen-ID.');
    return{article:parts[0],orderNo:parts[1],plannedMachineId:parts[2],raw:String(raw||'').trim()};
  }

  async function scanFaTank(){
    try{
      const x=await scanTankCommon();
      if(x.stock?.status!=='UNIQUE'||x.stock?.rows?.length!==1)throw new Error(x.stock?.status==='EMPTY'?'Der Tank ist leer. Es kann kein Pulververbrauch auf einen Fertigungsauftrag gebucht werden.':x.stock?.message||'Tankbestand ist nicht eindeutig.');
      const row=x.stock.rows[0];
      faState={tank:{warehouse:x.option.warehouse,warehouseText:x.option.warehouseText||x.option.warehouse},row,colors:x.stock.recognitionColors,order:null,amount:''};
      el('faConsumptionAmount').value='';
      el('faTankStatus').className='status ok';el('faTankStatus').textContent='✓ Tankbestand eindeutig aus oxaion gelesen.';
      el('faTankData').classList.remove('hidden');
      el('faTankData').innerHTML=`${swatch(faState.colors)}<b>${esc(row.article)} ${esc(row.articleText||'')}</b><br>Mix-Charge ${esc(row.batch)} · aktueller Tankbestand ${qty(row.quantityKg)} kg`;
      el('faOrderStep').classList.remove('lockedStep');el('faOrderScan').disabled=false;
      el('faOrderStatus').className='status neutral';el('faOrderStatus').textContent='Fertigungsauftrag scannen.';
      el('faOrderData').classList.add('hidden');el('faAmountStep').classList.add('lockedStep');el('faBookStep').classList.add('lockedStep');
      refreshFa();
    }catch(e){if(e?.name!=='AbortError'){el('faTankStatus').className='status bad';el('faTankStatus').textContent='⛔ '+e.message}}
  }

  async function scanFaOrder(){
    if(!faState.row)return;
    try{
      const raw=await scanQrCode({title:'Fertigungsauftrag scannen',help:'Erwartet: Rohmaterial+++Fertigungsauftrag+++Maschinen-ID'});
      const order=parseFaQr(raw);
      if(order.article.toUpperCase()!==String(faState.row.article||'').toUpperCase())throw new Error(`Falsches Rohmaterial im Fertigungsauftrag: ${order.article}. Im gescannten Tank liegt ${faState.row.article}.`);
      faState.order=order;faState.amount='';el('faConsumptionAmount').value='';
      el('faOrderStatus').className='status ok';el('faOrderStatus').textContent='✓ Fertigungsauftrag und Tankartikel passen zusammen.';
      el('faOrderData').classList.remove('hidden');
      el('faOrderData').innerHTML=`<div class="processDetailGrid"><div><span>Fertigungsauftrag</span><b>${esc(order.orderNo)}</b></div><div><span>Rohmaterial</span><b>${esc(order.article)}</b></div><div><span>Maschine laut FA-QR</span><b>${esc(order.plannedMachineId)}</b></div><div><span>Tatsächlicher Tank</span><b>${esc(faState.tank.warehouse)}</b></div></div><div class="footerNote">Die Maschinen-ID aus dem FA ist Plan-/Auftragsinformation. Eine kurzfristig abweichende tatsächliche Maschine bleibt zulässig; die noch offene Maschinen-ID↔Tank-Zuordnung wird hier nicht erfunden.</div>`;
      el('faAmountStep').classList.remove('lockedStep');el('faAmountStatus').className='status neutral';el('faAmountStatus').textContent='Tatsächlichen Pulververbrauch eingeben.';
      el('faConsumptionAmount').focus();refreshFa();
    }catch(e){if(e?.name!=='AbortError'){faState.order=null;el('faAmountStep').classList.add('lockedStep');el('faBookStep').classList.add('lockedStep');el('faOrderStatus').className='status bad';el('faOrderStatus').textContent='⛔ '+e.message;el('faOrderData').classList.add('hidden');refreshFa()}}
  }

  function faAmountValue(){
    const raw=String(faState.amount||'').trim();if(!raw)return null;
    const value=Number(raw.replace(',','.'));return Number.isFinite(value)?value:NaN;
  }

  function refreshFa(){
    if(mode!==MODES.FA_CONSUMPTION)return;
    const instruction=el('workerNextInstruction');
    if(!faState.row){if(instruction)instruction.textContent='Maschinentank scannen.';return}
    if(!faState.order){el('faSummary').innerHTML=`<b>${esc(faState.tank.warehouse)} · ${esc(faState.row.article)}</b><br>Fertigungsauftrag noch nicht gescannt.`;if(instruction)instruction.textContent='Fertigungsauftrag scannen.';return}
    const amount=faAmountValue();const available=Number(faState.row.quantityKg||0);
    const valid=Number.isFinite(amount)&&amount>0&&amount<=available+.0005;
    el('faBookStep').classList.toggle('lockedStep',!valid);
    if(amount===null){el('faAmountStatus').className='status neutral';el('faAmountStatus').textContent=`Verbrauch eingeben. Aktuell ${qty(available)} kg im Tank.`}
    else if(!Number.isFinite(amount)||amount<=0){el('faAmountStatus').className='status bad';el('faAmountStatus').textContent='⛔ Verbrauch muss größer als 0 kg sein.'}
    else if(amount>available+.0005){el('faAmountStatus').className='status bad';el('faAmountStatus').textContent=`⛔ Verbrauch ${qty(amount)} kg ist größer als der aktuelle Tankbestand ${qty(available)} kg.`}
    else{el('faAmountStatus').className='status ok';el('faAmountStatus').textContent=`✓ Verbrauch ${qty(amount)} kg ist innerhalb des aktuellen Tankbestands.`}
    const amountText=valid?`${qty(amount)} kg`:(String(faState.amount||'').trim()?esc(faState.amount)+' kg':'fehlt');
    const remaining=valid?qty(Math.max(0,available-amount))+' kg':'–';
    el('faSummary').innerHTML=`<b>${esc(faState.order.orderNo)} · ${esc(faState.row.article)}</b><br>Tank: ${esc(faState.tank.warehouse)} · Mix-Charge ${esc(faState.row.batch)}<br>Verbrauch: <b>${amountText}</b> · rechnerischer Rest: ${remaining}<br>Maschine laut FA: ${esc(faState.order.plannedMachineId)}`;
    if(instruction)instruction.textContent=valid?'FA-Verbrauch vollständig vorbereitet. Oxaion-FA-Buchungsablauf noch offen.':'Tatsächlichen Verbrauch eingeben.';
  }

  function installRefreshHook(){if(typeof refreshWorkerFlow!=='function'||baseRefresh)return;baseRefresh=refreshWorkerFlow;refreshWorkerFlow=function(){baseRefresh();refreshProcessUi()}}
  function init(){ensureUi();installRefreshHook();refreshProcessUi();}
  if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',()=>setTimeout(init,0));else setTimeout(init,0);
})();
