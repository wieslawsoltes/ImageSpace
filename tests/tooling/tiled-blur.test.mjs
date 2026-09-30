import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { runInNewContext } from 'node:vm';

const source = await readFile(new URL('../../src/ImageSpace.WebGpu/WebGpu.js', import.meta.url), 'utf8');

// A descriptor-only test double captures shipped shader text. It never executes
// WGSL and cannot establish GPU output or performance correctness.
async function capture() {
  const shaders = new Map();
  const device = {
    pushErrorScope() {}, async popErrorScope() { return null; },
    createBindGroupLayout: descriptor => descriptor,
    createPipelineLayout: descriptor => descriptor,
    createShaderModule: descriptor => {
      shaders.set(descriptor.label.replace('ImageSpace ', ''), descriptor.code);
      return { getCompilationInfo: async () => ({ messages: [] }) };
    },
    createComputePipelineAsync: async descriptor => descriptor,
    lost: new Promise(() => {}), destroy() {}
  };
  const context = {
    Uint8Array, GPUShaderStage: { COMPUTE: 4 },
    navigator: { gpu: { requestAdapter: async () => ({ requestDevice: async () => device }) } }
  };
  runInNewContext(source, context);
  await context.imageSpaceGpu.initialize();
  return { shaders: new Map(Object.entries(context.imageSpaceGpu.shaderSources)), engine: context.imageSpaceGpu };
}

test('tiled shader declarations stay within core workgroup/storage budgets', async () => {
  const { shaders } = await capture();
  assert.equal(shaders.size, 7);
  for (const [name, elementBytes] of [['tiledHorizontal', 4], ['tiledVertical', 16]]) {
    const code = shaders.get(name);
    const [, x, y] = code.match(/@workgroup_size\((\d+),(\d+)\)/);
    const [, count] = code.match(/var<workgroup> tile:array<[^,]+,(\d+)>;/);
    assert.ok(Number(x) * Number(y) <= 256);
    assert.ok(Number(count) * elementBytes <= 16384);
    assert.equal(Number(count), 4 * (32 + 2 * 96));
    assert.ok(code.indexOf('workgroupBarrier();') < code.indexOf('return;'),
      'No tail lane may exit before the collective barrier.');
  }
});

test('cooperative tiles initialize each declared halo element once', async () => {
  const { shaders } = await capture();
  for (const name of ['tiledHorizontal', 'tiledVertical']) {
    const [, x, y] = shaders.get(name).match(/@workgroup_size\((\d+),(\d+)\)/);
    const lanes = Number(x) * Number(y);
    for (let radius = 1; radius <= 96; radius++) {
      const length = (32 + 2 * radius) * 4, writes = new Uint8Array(length);
      for (let lane = 0; lane < lanes; lane++) for (let at = lane; at < length; at += lanes) writes[at]++;
      assert.ok(writes.every(count => count === 1), `${name} radius ${radius}`);
    }
  }
});

test('tile-index arithmetic matches clamped direct sampling at image/workgroup edges', async () => {
  // This verifies address arithmetic only; the real GPU suite tests the float math.
  const { shaders } = await capture();
  for (const horizontal of [true, false]) {
    const name = horizontal ? 'tiledHorizontal' : 'tiledVertical';
    const [, a, b] = shaders.get(name).match(/@workgroup_size\((\d+),(\d+)\)/);
    const wx = Number(a), wy = Number(b);
    for (const [width, height] of [[1, 1], [3, 5], [31, 33], [65, 35]]) {
      const sample = (x, y) => Math.min(height - 1, Math.max(0, y)) * width +
        Math.min(width - 1, Math.max(0, x));
      for (const radius of [1, 3, 6, 24, 95, 96]) {
        for (let top = 0; top < height; top += wy) for (let left = 0; left < width; left += wx) {
          const pitch = horizontal ? 32 + 2 * radius : 4;
          const tile = Array.from({ length: (32 + 2 * radius) * 4 }, (_, n) =>
            sample(left + n % pitch - (horizontal ? radius : 0),
              top + Math.floor(n / pitch) - (horizontal ? 0 : radius)));
          for (let y = 0; y < wy && top + y < height; y++)
            for (let x = 0; x < wx && left + x < width; x++)
              for (let tap = 0; tap <= 2 * radius; tap++) {
                const at = horizontal ? y * pitch + x + tap : (y + tap) * 4 + x;
                assert.ok(at >= 0 && at < tile.length);
                assert.equal(tile[at], sample(left + x + (horizontal ? tap - radius : 0),
                  top + y + (horizontal ? 0 : tap - radius)));
              }
        }
      }
    }
  }
});

test('invalid strategy values cannot request an adapter', async () => {
  let requested = 0;
  const context = { Uint8Array, navigator: { gpu: { requestAdapter() { requested++; throw new Error('Must not run.'); } } } };
  runInNewContext(source, context);
  for (const gaussianBlur of ['', false, 0, null, [], {}, 'TILED'])
    await assert.rejects(context.imageSpaceGpu.createSession(new Uint8Array(4), 1, 1, { gaussianBlur }),
      error => error.name === 'TypeError');
  assert.equal(requested, 0);
});
