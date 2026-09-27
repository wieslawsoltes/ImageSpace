import {mkdir,copyFile,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const root=new URL('../',import.meta.url),pkg=new URL('packages/webgpu/',root),dist=new URL('dist/',pkg);
await mkdir(dist,{recursive:true});await mkdir(new URL('artifacts/packages/',root),{recursive:true});
await copyFile(new URL('src/ImageSpace.WebGpu/WebGpu.js',root),new URL('kernels.js',dist));await copyFile(new URL('LICENSE',root),new URL('LICENSE',pkg));
await writeFile(new URL('index.js',dist),`import './kernels.js';\nexport const initialize=()=>globalThis.imageSpaceGpu.initialize();\nexport const capabilities=()=>globalThis.imageSpaceGpu.describe();\nexport const supports=kind=>globalThis.imageSpaceGpu.supports(kind);\nexport const applyFilter=(rgba,width,height,kind,amount=0,secondary=0)=>globalThis.imageSpaceGpu.apply(rgba,width,height,kind,amount,secondary);\n`);
await writeFile(new URL('index.d.ts',dist),`export type FilterKind='Invert'|'Grayscale'|'Sepia'|'BrightnessContrast'|'Saturation'|'Gamma'|'Threshold'|'Posterize';\nexport interface Capabilities {available:boolean;backend:string;maximumBufferSize:number;}\nexport declare function initialize():Promise<unknown|null>;\nexport declare function capabilities():Capabilities;\nexport declare function supports(kind:string):boolean;\nexport declare function applyFilter(rgba:Uint8Array,width:number,height:number,kind:FilterKind,amount?:number,secondary?:number):Promise<Uint8Array|null>;\n`);
console.log('Built standalone WebGPU package at '+fileURLToPath(pkg));
