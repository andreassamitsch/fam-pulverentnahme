'use strict';

// The backend re-reads the selected machine tank, personnel and all source positions immediately
// before the first write-capable Oxaion call. Do not refresh the tank here: a tank refresh
// intentionally rebuilds the dependent scanned source selections and would discard the user's selections.
async function submitPreparedReplenishment(){
  if(active)return;
  if(machineStock?.status!=='UNIQUE'||machineStock?.rows?.length!==1){alert('Maschinentank-Bestand nicht eindeutig.');return}
  const ok=await refreshSelectedSources();
  if(!ok){alert('Nachfüllchargen bzw. Entnahmeorte erneut prüfen.');return}
  let r;
  try{r=values();validate(r)}catch(e){alert(e.message);return}
  const msg=`ECHTE STAGING-BUCHUNG?\n\nMaschinentank: ${r.oldMixWarehouse}\nArtikel: ${r.article} ${r.articleText}\nTank: ${r.oldMixBatch} · ${r.oldMixAmountKg.toFixed(3)} kg\n\nNachfüllchargen:\n${sourceConfirm(r)}\n\nNeue Mix-Charge: ${r.targetBatch}\nBuchungstext: ${r.bookingText}\n\nMitarbeiter: ${r.personnelNo} - ${r.personnelName}`;
  if(confirm(msg))await sendRequest(r);
}
$('bookBtn').onclick=submitPreparedReplenishment;
