import {mkdir,copyFile,writeFile} from 'node:fs/promises';
import {fileURLToPath} from 'node:url';
const root=new URL('../',import.meta.url),pkg=new URL('packages/webgpu/',root),dist=new URL('dist/',pkg);
await mkdir(dist,{recursive:true});await mkdir(new URL('artifacts/packages/',root),{recursive:true});
await copyFile(new URL('src/ImageSpace.WebGpu/WebGpu.js',root),new URL('kernels.js',dist));
await copyFile(new URL('LICENSE',root),new URL('LICENSE',pkg));
await writeFile(new URL('index.js',dist),`import './kernels.js';
export const initialize=()=>globalThis.imageSpaceGpu.initialize();
export const capabilities=()=>globalThis.imageSpaceGpu.describe();
export const supports=kind=>globalThis.imageSpaceGpu.supports(kind);
export const applyFilter=(rgba,width,height,kind,amount=0,secondary=0)=>globalThis.imageSpaceGpu.apply(rgba,width,height,kind,amount,secondary);
export const applyChain=(rgba,width,height,operations)=>globalThis.imageSpaceGpu.applyChain(rgba,width,height,operations);
export const createSession=(rgba,width,height)=>globalThis.imageSpaceGpu.createSession(rgba,width,height);
`);
await writeFile(new URL('index.d.ts',dist),`export type FilterKind='Invert'|'Grayscale'|'Sepia'|'BrightnessContrast'|'Saturation'|'Gamma'|'Threshold'|'Posterize'|'GaussianBlur'|'Sharpen'|'Emboss'|'Edges'|'Pixelate';
export interface FilterOperation {readonly kind:FilterKind;readonly amount?:number;readonly secondary?:number;readonly enabled?:boolean;}
export interface Capabilities {available:boolean;backend:string;maximumBufferSize:number;residentBytes:number;residentBudgetBytes:number;liveSessions:number;pipelineCount:number;}
export interface SessionStatistics {sourceUploads:number;uploadedBytes:number;submissions:number;dispatches:number;readbacks:number;bufferAllocations:number;bindGroupBuilds:number;residentBytes:number;disposed:boolean;}
export interface FilterSession {
  readonly width:number;readonly height:number;readonly generation:number;
  /** Restarts from the captured immutable source and reads the final RGBA8 output once. */
  apply(operations:readonly FilterOperation[]):Promise<Uint8Array>;
  /** Restarts from the captured immutable source; leaves the final output on the GPU. */
  execute(operations:readonly FilterOperation[]):Promise<void>;
  read():Promise<Uint8Array>;
  statistics():SessionStatistics;
  dispose():void;
}
export declare function initialize():Promise<unknown|null>;
export declare function capabilities():Capabilities;
export declare function supports(kind:string):boolean;
export declare function applyFilter(rgba:Uint8Array,width:number,height:number,kind:FilterKind,amount?:number,secondary?:number):Promise<Uint8Array|null>;
export declare function applyChain(rgba:Uint8Array,width:number,height:number,operations:readonly FilterOperation[]):Promise<Uint8Array|null>;
export declare function createSession(rgba:Uint8Array,width:number,height:number):Promise<FilterSession|null>;
`);
console.log('Built standalone WebGPU package at '+fileURLToPath(pkg));
