import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';

// Descriptor/scheduling double only. Copies symbolic bytes and advances a synthetic
// clock; it never compiles WGSL or measures GPU performance. Real-GPU tests are separate.
export async function schedulingDouble({ directTime = 10, tiledTime = 20, mismatch = false, onCompleted, captureUniforms = false } = {}) {
  const source = await readFile(new URL('../../src/ImageSpace.WebGpu/WebGpu.js', import.meta.url), 'utf8');
  const compiled = [], submitted = [], uniformWrites = [];
  let clock = 0, reads = 0, requested = 0, completeCalls = 0;
  const createDevice = () => {
    let resolveLost;
    const lost = new Promise(resolve => { resolveLost = resolve; });
    const device = {
      limits: { minUniformBufferOffsetAlignment: 256, maxStorageBufferBindingSize: 128 * 1024 * 1024, maxBufferSize: 256 * 1024 * 1024 },
      pushErrorScope() {}, async popErrorScope() { return null; },
      createBindGroupLayout: descriptor => descriptor,
      createPipelineLayout: descriptor => descriptor,
      createShaderModule: descriptor => ({ ...descriptor, getCompilationInfo: async () => ({ messages: [] }) }),
      createComputePipelineAsync: async descriptor => { compiled.push(descriptor.label); return descriptor; },
      createBindGroup: descriptor => descriptor,
      createBuffer: descriptor => {
        const bytes = new Uint8Array(descriptor.size);
        return { ...descriptor, bytes, destroyed: false, destroy() { this.destroyed = true; },
          async mapAsync() { if (this.destroyed) throw new Error('Destroyed buffer'); reads++; },
          getMappedRange() { if (this.destroyed) throw new Error('Destroyed buffer'); return bytes.buffer; }, unmap() {} };
      },
      lost, destroy() { resolveLost({ message: 'Synthetic test loss' }); },
      createCommandEncoder() {
        const commands = [];
        return {
          copyBufferToBuffer(from, fromOffset, to, toOffset, length) {
            commands.push(() => to.bytes.set(from.bytes.subarray(fromOffset, fromOffset + length), toOffset));
          },
          beginComputePass() {
            let pipeline, binding, offset;
            return {
              setPipeline(value) { pipeline = value; },
              setBindGroup(_, value, dynamic) { binding = value; offset = dynamic[0]; },
              dispatchWorkgroups() {},
              end() {
                commands.push(() => {
                  const name = pipeline.label.replace('ImageSpace ', ''); submitted.push(name);
                  const from = binding.entries[0].resource.buffer, to = binding.entries[1].resource.buffer;
                  to.bytes.set(from.bytes.subarray(0, Math.min(from.size, to.size)));
                  if (name === 'point') {
                    const uniform = new Uint32Array(binding.entries[2].resource.buffer.bytes.buffer, offset, 8);
                    if (uniform[2] === 0) for (let i = 0; i < to.size; i++) if (i % 4 !== 3) to.bytes[i] ^= 255;
                  }
                  if (mismatch && name === 'tiledVertical') to.bytes[0] ^= 1;
                  if (name === 'vertical') clock += typeof directTime === 'function' ? directTime() : directTime;
                  if (name === 'tiledVertical') clock += typeof tiledTime === 'function' ? tiledTime() : tiledTime;
                });
              }
            };
          }, finish: () => commands
        };
      }
    };
    device.queue = {
      writeBuffer(buffer, offset, data, dataOffset = 0, size) {
        const bytes = data instanceof ArrayBuffer ? new Uint8Array(data) : new Uint8Array(data.buffer, data.byteOffset, data.byteLength);
        buffer.bytes.set(bytes.subarray(dataOffset, dataOffset + (size ?? bytes.length)), offset);
        if (captureUniforms && (buffer.usage & 64) !== 0)
          uniformWrites.push(bytes.slice(dataOffset, dataOffset + (size ?? bytes.length)));
      },
      submit(commands) { for (const buffer of commands) for (const execute of buffer) execute(); },
      async onSubmittedWorkDone() { onCompleted?.(++completeCalls); }
    };
    return device;
  };
  const context = { Uint8Array, ArrayBuffer, Uint32Array, Float32Array, setTimeout, clearTimeout, DOMException,
    performance: { now: () => clock }, GPUShaderStage: { COMPUTE: 4 }, GPUMapMode: { READ: 1 },
    GPUBufferUsage: { MAP_READ: 1, COPY_SRC: 4, COPY_DST: 8, STORAGE: 128, UNIFORM: 64 },
    navigator: { gpu: { requestAdapter: async () => { requested++; return { requestDevice: async () => createDevice() }; } } } };
  runInNewContext(source, context);
  return { engine: context.imageSpaceGpu, compiled, submitted, uniformWrites, requests: () => requested, reads: () => reads };
}
