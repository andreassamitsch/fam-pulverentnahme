'use strict';

// Regression for the actual process-mode inventory comparator.
// RP powder should be listed before PB customer-supplied powder;
// grouping is display-only and must not alter Oxaion stock rows.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');

const source=fs.readFileSync(
 path.resolve(__dirname,'../../src/Fam.Pulverentnahme.Web/wwwroot/process-mode.js'),
 'utf8'
);
const match=source.match(/^\s*const compareInventoryArticles=.*;$/m);
assert.ok(match,'Inventory article comparator is missing');
assert.match(source,/sort\(\(\[left\],\[right\]\)=>compareInventoryArticles\(left,right\)\)/,
 'Inventory rendering must use the group-specific comparator');

const articles=['PB.00002','RP.00010','PB.00001','RP.00002','RP.00001','PB.00003'];
const result=vm.runInNewContext(
 match[0]+'\n['+articles.map(x=>JSON.stringify(x)).join(',')+'].sort(compareInventoryArticles)',
 {}
);
assert.deepEqual(Array.from(result),[
 'RP.00001','RP.00002','RP.00010','PB.00001','PB.00002','PB.00003'
]);
assert.equal(result.length,articles.length,'No stock article must disappear');
console.log('PASS inventory article order: RP before PB, order within groups retained');
