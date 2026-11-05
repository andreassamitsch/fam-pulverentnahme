'use strict';

// Runtime regression for 0.1.11: DOMContentLoaded happens before process-mode
// creates the dynamic charge-origin form, so controls must bind afterwards.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(
  path.resolve(__dirname, '../../src/Fam.Pulverentnahme.Web/wwwroot/charge-origin-ui.js'),
  'utf8'
);

const nodes = new Map();
const listeners = new Map();
const requests = [];
function node(id) {
  return {
    id,
    value: '',
    innerHTML: '',
    textContent: '',
    className: '',
    disabled: false,
    dataset: {},
    listeners: {},
    addEventListener(name, callback) { this.listeners[name] = callback; },
    classList: {
      contains() { return false; },
      add() {},
      remove() {}
    }
  };
}
function put(id) {
  const element = node(id);
  nodes.set(id, element);
  return element;
}
const document = {
  readyState: 'loading',
  head: { appendChild(element) { nodes.set(element.id, element); } },
  body: { classList: { add() {}, remove() {} } },
  getElementById(id) { return nodes.get(id) ?? null; },
  createElement() { return node(''); },
  addEventListener(name, callback) { listeners.set(name, callback); }
};
const window = {};
const context = {
  window,
  document,
  parseChargeQr(raw) {
    const parts = raw.split('+++');
    if(parts.length !== 2) throw new Error('invalid');
    return { article: parts[0], batch: parts[1] };
  },
  async scanQrCode() { return 'PB.00001+++KUNDE_123'; },
  async api(url) {
    requests.push(url);
    return {
      ok: true,
      body: {
        article: 'PB.00001',
        batch: 'KUNDE_123',
        baseBatches: [{
          article: 'PB.00001',
          batch: 'KUNDE_123',
          supplier: '3001399 000',
          supplierName: 'IMR metal powder technologies GmbH',
          externalBatch: 'WZ_17551102+WZ_1761113_m4p_BS2',
          purchaseOrder: '',
          goodsReceipt: 'FA24WE00027',
          deliveryDate: '2024-05-06',
          productionOrder: 'FA23FI00006'
        }]
      }
    };
  }
};

vm.runInNewContext(source, context, { filename: 'charge-origin-ui.js' });
assert.ok(nodes.has('famChargeOriginStyles'),
  'Result cards must be styled before the asynchronously created panel exists');
assert.equal(typeof window.FamChargeOriginUi.bind, 'function');

listeners.get('DOMContentLoaded')();
assert.equal(nodes.has('chargeOriginSearch'), false);

const search = put('chargeOriginSearch');
const scan = put('chargeOriginScan');
const article = put('chargeOriginArticle');
const batch = put('chargeOriginBatch');
const status = put('chargeOriginStatus');
const output = put('chargeOriginResult');
window.FamChargeOriginUi.bind(); // Called immediately after process-mode.ensureUi creates the form.

assert.equal(search.dataset.originBound, 'true');
assert.equal(typeof search.onclick, 'function');
assert.equal(typeof scan.onclick, 'function');
assert.equal(typeof article.listeners.keydown, 'function');
window.FamChargeOriginUi.bind();
assert.equal(nodes.has('famChargeOriginStyles'), true);
assert.equal(typeof search.onclick, 'function');

async function tick() {
  await new Promise(resolve => setImmediate(resolve));
}

(async () => {
  await scan.onclick();
  await tick();
  assert.equal(article.value, 'PB.00001');
  assert.equal(batch.value, 'KUNDE_123');
  assert.ok(requests.at(-1).includes('/api/charge-origin?article=PB.00001&batch=KUNDE_123'));
  assert.match(status.textContent, /1 Grundcharge aus Oxaion ermittelt/);
  assert.match(output.innerHTML, /KUNDE_123/);
  assert.match(output.innerHTML, /Lieferant/);
  assert.match(output.innerHTML, /IMR metal powder technologies GmbH/);
  assert.match(output.innerHTML, /<span>Lieferant<\/span><b>IMR metal powder technologies GmbH<\/b>/);
  assert.doesNotMatch(output.innerHTML, /Lieferantenname|3001399 000|Bestellung|purchaseOrder/,
    'Supplier number and purchase order must not appear in origin cards');
  assert.match(output.innerHTML, /Externe Charge/);
  assert.match(output.innerHTML, /WZ_17551102/);
  assert.match(output.innerHTML, /Lieferdatum/);
  assert.match(output.innerHTML, /06\.05\.2024/);
  assert.doesNotMatch(output.innerHTML, /Bestelldatum/);
  assert.doesNotMatch(output.innerHTML, /FA23FI00006|Fertigungsauftrag/,
    'A consuming production order must not be presented as a base-batch origin');

  article.value = 'RP.00010';
  batch.value = 'RP00010MIX_20261006_144459';
  search.onclick();
  await tick();
  assert.ok(requests.at(-1).includes(
    '/api/charge-origin?article=RP.00010&batch=RP00010MIX_20261006_144459'
  ));

  article.value = 'VK.00010';
  const previousCount = requests.length;
  search.onclick();
  await tick();
  assert.equal(requests.length, previousCount, 'Invalid articles must not trigger the Oxaion lookup');
  assert.match(status.textContent, /RP\.\* oder PB\.\*/);

  console.log('PASS charge-origin UI: late binding, scan, manual entry, metadata, invalid article');
})().catch(error => {
  console.error(error);
  process.exitCode = 1;
});
