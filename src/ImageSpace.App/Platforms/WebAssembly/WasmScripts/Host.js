/* Browser file, recovery and compute bridge. MIT. No network upload or cloud service. */
(() => {
  'use strict';
  const testMode=new URLSearchParams(location.search).get('test')==='1';
  const limit=128*1024*1024;let dirty=false;
  const from64=value=>{const decoded=atob(value);const bytes=new Uint8Array(decoded.length);for(let i=0;i<decoded.length;i++)bytes[i]=decoded.charCodeAt(i);return bytes;};
  const to64=bytes=>{const parts=[];for(let i=0;i<bytes.length;i+=32768)parts.push(String.fromCharCode(...bytes.subarray(i,i+32768)));return btoa(parts.join(''));};
  function database(){return new Promise((resolve,reject)=>{const request=indexedDB.open('ImageSpace',1);request.onupgradeneeded=()=>request.result.createObjectStore('recovery');request.onsuccess=()=>resolve(request.result);request.onerror=()=>reject(request.error);request.onblocked=()=>reject(new Error('Recovery database is blocked by another tab.'));});}
  async function store(mode,operation){const db=await database();try{return await new Promise((resolve,reject)=>{const transaction=db.transaction('recovery',mode);let value;const request=operation(transaction.objectStore('recovery'));request.onsuccess=()=>value=request.result;transaction.oncomplete=()=>resolve(value);transaction.onerror=()=>reject(transaction.error);transaction.onabort=()=>reject(transaction.error||new Error('Recovery transaction aborted.'));});}finally{db.close();}}
  globalThis.imageSpaceHost={
    isTestMode:()=>testMode,
    setDirty:value=>{dirty=!!value;},
    publishState:(state,controls)=>{if(testMode){globalThis.imageSpaceDiagnostics=JSON.parse(state);globalThis.imageSpaceControls=JSON.parse(controls);}},
    open:()=>new Promise((resolve,reject)=>{
      const input=document.createElement('input');input.type='file';input.accept='.imagespace,.psd,.png,.jpg,.jpeg,.webp,.bmp,.gif';input.style.display='none';document.body.append(input);let finished=false;
      const done=(value,error)=>{if(finished)return;finished=true;input.remove();if(error)reject(error);else resolve(value);};
      input.addEventListener('cancel',()=>done(''),{once:true});input.addEventListener('change',async()=>{try{const file=input.files?.[0];if(!file){done('');return;}if(file.size>limit)throw new Error('File exceeds 128 MiB.');done(JSON.stringify({name:file.name,data:to64(new Uint8Array(await file.arrayBuffer()))}));}catch(error){done('',error);}},{once:true});input.click();
    }),
    download:async(name,base64,type)=>{const bytes=from64(base64);const url=URL.createObjectURL(new Blob([bytes],{type}));const anchor=document.createElement('a');anchor.href=url;anchor.download=name;document.body.append(anchor);anchor.click();anchor.remove();setTimeout(()=>URL.revokeObjectURL(url),30000);return 'ok';},
    load:async()=>{const bytes=await store('readonly',s=>s.get('active'));if(!bytes)return '';if(bytes.byteLength>limit)throw new Error('Recovery file exceeds 128 MiB.');return to64(new Uint8Array(bytes));},
    save:async base64=>{const bytes=from64(base64);if(bytes.byteLength>limit)throw new Error('Recovery file exceeds 128 MiB.');await store('readwrite',s=>s.put(bytes,'active'));return 'ok';},
    clear:async()=>{await store('readwrite',s=>s.delete('active'));return 'ok';},
    initializeGpu:async()=>{if(!globalThis.imageSpaceGpu)return 'CPU kernels (WebGPU module not loaded)';await imageSpaceGpu.initialize();return imageSpaceGpu.describe().backend;},
    filter:async(base64,width,height,kind,amount,secondary)=>{if(!globalThis.imageSpaceGpu?.supports(kind))return '';const result=await imageSpaceGpu.apply(from64(base64),width,height,kind,amount,secondary);return result?to64(result):'';}
  };
  addEventListener('beforeunload',event=>{if(dirty&&!testMode){event.preventDefault();event.returnValue='';}});
  addEventListener('keydown',event=>{
    const target=event.target;const editing=target instanceof HTMLInputElement||target instanceof HTMLTextAreaElement||target?.isContentEditable;
    if(editing)return;
    const control=event.ctrlKey||event.metaKey;const key=event.key.toLowerCase();
    if((control&&['n','o','s','z','y','j','a','d','i','t','r','0','1','e'].includes(key))||[' ','tab','backspace','f1'].includes(key))event.preventDefault();
  },true);
})();
