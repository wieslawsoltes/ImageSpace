import {defineConfig} from '@playwright/test';
const external=process.env.IMAGESPACE_URL;
const baseURL=external ? external.replace(/\/+$/, '')+'/' : 'http://127.0.0.1:4173/ImageSpace/';
export default defineConfig({
  testDir:'tests/browser',timeout:180000,expect:{timeout:20000},workers:1,retries:0,
  reporter:[['list'],['json',{outputFile:'artifacts/browser-results.json'}]],outputDir:'artifacts/browser-tests',
  use:{baseURL,viewport:{width:1440,height:1000},deviceScaleFactor:1,acceptDownloads:true,trace:'retain-on-failure',screenshot:'only-on-failure',launchOptions:{args:['--enable-unsafe-webgpu','--enable-unsafe-swiftshader','--use-angle=swiftshader']}},
  webServer:external?undefined:{command:'python3 scripts/serve-site.py --port 4173 --root site',url:'http://127.0.0.1:4173/ImageSpace/',reuseExistingServer:false,timeout:30000}
});
