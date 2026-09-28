import { test, expect } from '@playwright/test';
import { mkdir, writeFile } from 'node:fs/promises';
import { waitForWorkspace } from './readiness.mjs';

// Independent native-format fixture: one stored UTF-8 manifest, no runtime mutation hook.
function fixture() {
  const id = n => `81350322-2faa-41c6-9000-${String(n).padStart(12,'0')}`;
  const layers = Array.from({length:96},(_,i)=>({metadata:{
    id:id(i+1), name:`Tile ${String(i).padStart(2,'0')}`, kind:2,
    x:(i%8)*128+12, y:Math.floor(i/8)*64+8, width:104, height:48,
    color:{r:40+(i*29)%180,g:40+(i*53)%180,b:40+(i*17)%180,a:255}
  }}));
  const data=Buffer.from(JSON.stringify({version:1,id:id(999),name:'Selection stress',
    width:1024,height:768,dpi:72,activeLayerId:id(96),layers}));
  let crc=0xffffffff;
  for (const byte of data) {
    crc^=byte;
    for (let bit=0;bit<8;bit++) crc=(crc>>>1)^((crc&1)?0xedb88320:0);
  }
  crc=(crc^0xffffffff)>>>0;
  const name=Buffer.from('manifest.json');
  const local=Buffer.alloc(30);
  local.writeUInt32LE(0x04034b50,0);local.writeUInt16LE(20,4);
  local.writeUInt32LE(crc,14);local.writeUInt32LE(data.length,18);local.writeUInt32LE(data.length,22);
  local.writeUInt16LE(name.length,26);
  const central=Buffer.alloc(46);
  central.writeUInt32LE(0x02014b50,0);central.writeUInt16LE(20,4);central.writeUInt16LE(20,6);
  central.writeUInt32LE(crc,16);central.writeUInt32LE(data.length,20);central.writeUInt32LE(data.length,24);
  central.writeUInt16LE(name.length,28);
  const end=Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50,0);end.writeUInt16LE(1,8);end.writeUInt16LE(1,10);
  end.writeUInt32LE(central.length+name.length,12);
  end.writeUInt32LE(local.length+name.length+data.length,16);
  return Buffer.concat([local,name,data,central,name,end]);
}

const state=page=>page.evaluate(()=>globalThis.imageSpaceDiagnostics);
async function control(page,name) {
  await expect.poll(()=>page.evaluate(n=>globalThis.imageSpaceControls?.some(c=>c.name===n&&c.enabled),name)).toBe(true);
  return page.evaluate(n=>globalThis.imageSpaceControls.find(c=>c.name===n&&c.enabled),name);
}
async function click(page,name) {
  const c=await control(page,name);
  await page.mouse.click(c.x+c.width/2,c.y+c.height/2);
}

test('selecting across a ninety-six-layer drawing retains the UI and never starts an edit',async({page})=>{
  const errors=[];page.on('pageerror',error=>errors.push(error.message));
  await page.goto('./?test=1',{waitUntil:'domcontentloaded'});
  await waitForWorkspace(page);
  // The bootstrap may not yet have assigned keyboard focus to a Uno control.
  // Open through the actual File menu rather than an unfocused browser shortcut.
  await click(page,'File');
  const chooser=page.waitForEvent('filechooser',{timeout:30000});
  await click(page,'Open…');
  await (await chooser).setFiles({name:'Selection-stress.imagespace',mimeType:'application/x-imagespace',buffer:fixture()});
  await expect.poll(async()=> (await state(page)).layers,{timeout:60000}).toBe(96);
  await expect.poll(async()=> (await state(page)).name).toBe('Selection stress');
  await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
  await page.waitForTimeout(600);
  const before=await state(page), samples=[];
  const targets=[0,47,95,8,63,32,80,15,0,47,95,8,63,32,80,15];
  for (const target of targets) {
    const c=await control(page,'Image canvas'), view=await state(page);
    const x=(target%8)*128+64, y=Math.floor(target/8)*64+32;
    await page.mouse.click(c.x+view.panX+x*view.zoom,c.y+view.panY+y*view.zoom);
    await expect.poll(async()=> (await state(page)).activeLayer).toBe(`Tile ${String(target).padStart(2,'0')}`);
    const selected=await state(page);
    expect(selected.transaction).toBe(false);
    samples.push({target,refreshMs:selected.lastSelectionRefreshMs});
  }
  const after=await state(page);
  for (const key of ['buttonsCreated','buttonTemplateBuilds','layerRowsCreated','inspectorBuilds',
    'optionsBuilds','tabsCreated','historyButtonsCreated','contentRevision','history','sceneRenders',
    'toneHistogramBuilds','channelHistogramBuilds']) expect(after[key],key).toBe(before[key]);
  expect(after.dirty).toBe(false);
  expect(errors).toEqual([]);
  await mkdir('artifacts/browser-tests',{recursive:true});
  await mkdir('artifacts/screenshots',{recursive:true});
  await writeFile('artifacts/browser-tests/ui-selection-stress.json',JSON.stringify({
    scope:'Synchronous C# selection refresh across 96 layers; excludes presentation and GPU completion.',
    before,after,samples
  },null,2));
  await page.screenshot({path:'artifacts/screenshots/selection-stress.png'});
});
