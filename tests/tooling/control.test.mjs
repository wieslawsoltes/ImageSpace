import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import {waitForControl} from '../browser/control.mjs';
const snapshot = (x = 10, enabled = true, value = '1') => [
  {name:'Target', type:'StudioButton', x, y:20, width:80, height:28, enabled, value}
];
function page(initial, publications) {
  let frames = 0;
  const context = vm.createContext({
    imageSpaceControls:initial,
    performance:{now:()=>frames*100},
    requestAnimationFrame:callback=>queueMicrotask(()=>{
      const next=publications[frames++];
      if (next !== undefined) context.imageSpaceControls=next;
      callback(frames*100);
    })
  });
  return {frames:()=>frames, async evaluate(fn,arg){
    context.argument=arg;
    return structuredClone(await vm.runInContext(`(${fn})(argument)`,context));
  }};
}
test('stale presence is insufficient; two new publications are required',async()=>{
  const old=snapshot(), p=page(old,[old,snapshot(),undefined,snapshot()]);
  const found=await waitForControl(p,'Target',1000);
  assert.equal(p.frames(),4);assert.equal(found.x,10);
});
test('a disappearing popup clears prior geometry and returns the replacement snapshot',async()=>{
  const p=page(snapshot(),[snapshot(),[],snapshot(40),snapshot(40)]);
  const found=await waitForControl(p,'Target',1000);
  assert.equal(p.frames(),4);assert.equal(found.x,40);
});
test('changing geometry restarts stability without retrying any user input',async()=>{
  const p=page([],[snapshot(10),snapshot(20),snapshot(30),snapshot(30)]);
  assert.equal((await waitForControl(p,'Target',1000)).x,30);
  assert.equal(p.frames(),4);
});
test('disabled or invalid clipped controls are never actionable',async()=>{
  const p=page([],[snapshot(10,false),snapshot(NaN),[{...snapshot()[0],height:0}],snapshot(),snapshot()]);
  assert.equal((await waitForControl(p,'Target',1000)).height,28);assert.equal(p.frames(),5);
});
test('unchanged coordinates may return the latest numeric value, not stale text',async()=>{
  const p=page([],[snapshot(10,true,'142'),snapshot(10,true,'143')]);
  assert.equal((await waitForControl(p,'Target',1000)).value,'143');
});
test('repeated reads of a single array time out with a named diagnostic',async()=>{
  const old=snapshot(),p=page(old,Array(10).fill(old));
  await assert.rejects(waitForControl(p,'Target',500),/Target.*did not settle/);
  assert.equal(p.frames(),5);
});
test('invalid arguments fail without evaluating the browser',async()=>{
  const p={evaluate:()=>assert.fail('Unexpected browser evaluation')};
  for(const [name,timeout] of [['',20],['Target',0],['Target',Infinity]])
    await assert.rejects(waitForControl(p,name,timeout),TypeError);
});
