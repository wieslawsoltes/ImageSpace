import {defineConfig} from '@playwright/test';
export default defineConfig({
  testDir:'tests/gpu', timeout:120000, workers:1, retries:0,
  reporter:[['list'],['json',{outputFile:'artifacts/resident-gpu-results.json'}]],
  use:{baseURL:'http://127.0.0.1:4174/',launchOptions:{args:['--enable-unsafe-webgpu','--enable-unsafe-swiftshader','--use-angle=swiftshader']}},
  webServer:{command:'python3 -m http.server 4174 --bind 127.0.0.1 --directory src/ImageSpace.WebGpu',url:'http://127.0.0.1:4174/',timeout:15000}
});
