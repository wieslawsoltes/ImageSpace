import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {settledLayerList, revealLayer} from '../browser/layer-list.mjs';

const snapshot = (y = 20, height = 100, name = 'Target') => [
  {name:'Layer list',type:'ScrollViewer',x:100,y:10,width:200,height,enabled:true},
  {name,type:'StudioButton',x:110,y,width:170,height:41,enabled:true},
  {name:'Elsewhere',type:'StudioButton',x:0,y:0,width:40,height:30,enabled:true}
];

// Execute the exact browser-side function in its own JS realm. Each frame costs
// 100 simulated milliseconds; undefined frames deliberately reuse an old array.
function sampledPage(initial, publications) {
  let frames = 0;
  const context = vm.createContext({
    imageSpaceControls: initial,
    performance:{now:()=>frames*100},
    requestAnimationFrame:callback=>queueMicrotask(()=>{
      const next = publications[frames++];
      if (next !== undefined) context.imageSpaceControls = next;
      callback(frames*100);
    })
  });
  return {
    frames:()=>frames,
    async evaluate(fn, timeout) {
      context.timeout=timeout;
      return structuredClone(await vm.runInContext(`(${fn})(timeout)`,context));
    }
  };
}

test('reading the same old snapshot repeatedly cannot establish stability',async()=>{
  const old=snapshot();
  const page=sampledPage(old,Array(10).fill(old));
  await assert.rejects(settledLayerList(page,500),/did not settle/);
  assert.equal(page.frames(),5);
});

test('moving bounds reset the count and require three fresh equal publications',async()=>{
  const first=snapshot(20);
  const page=sampledPage(snapshot(),[first,first,first,snapshot(30),undefined,snapshot(40),snapshot(40),snapshot(40)]);
  const view=await settledLayerList(page,1500);
  assert.equal(page.frames(),8);
  assert.equal(view.rows.length,1);
  assert.equal(view.rows[0].y,40);
});

test('hiding the layer list resets prior geometry stability',async()=>{
  const page=sampledPage(snapshot(),[snapshot(),snapshot(),[],snapshot(),snapshot(),snapshot()]);
  const view=await settledLayerList(page,1000);
  assert.equal(page.frames(),6);
  assert.equal(view.list.name,'Layer list');
});

test('inspector-driven viewport resize also resets geometry stability',async()=>{
  const page=sampledPage(snapshot(),[snapshot(),snapshot(),snapshot(20,120),snapshot(20,120),snapshot(20,120)]);
  const view=await settledLayerList(page,1000);
  assert.equal(page.frames(),5);
  assert.equal(view.list.height,120);
});

test('reveal waits after every wheel step and never clicks or edits the model',async()=>{
  const views=[{list:snapshot()[0],rows:[]},
    {list:snapshot()[0],rows:[]},{list:snapshot()[0],rows:[snapshot()[1]]}];
  const calls=[];
  const page={
    evaluate:async()=>{calls.push('settle');return views.shift();},
    mouse:{move:async()=>calls.push('move'),wheel:async(_,y)=>calls.push(['wheel',y])}
  };
  const row=await revealLayer(page,'Target');
  assert.equal(row.name,'Target');
  assert.deepEqual(calls,['settle','move',['wheel',-10000],'settle','move',['wheel',50],'settle']);
});

test('an unreachable layer fails without unbounded scrolling',async()=>{
  const view={list:snapshot()[0],rows:[]};
  let wheels=0;
  const page={evaluate:async()=>view,mouse:{move:async()=>{},wheel:async()=>wheels++}};
  await assert.rejects(revealLayer(page,'Missing'),/not reachable/);
  assert.equal(wheels,2);
});
