import {inflateSync} from 'node:zlib';
/** Minimal lossless decoder for the non-interlaced 8-bit RGB(A) PNG screenshots Chromium emits. */
export function decodePng(buffer){
  if(buffer.toString('hex',0,8)!=='89504e470d0a1a0a')throw new Error('Not a PNG');
  let width,height,channels,offset=8;const parts=[];
  while(offset<buffer.length){const length=buffer.readUInt32BE(offset),type=buffer.toString('ascii',offset+4,offset+8),data=buffer.subarray(offset+8,offset+8+length);offset+=length+12;
    if(type==='IHDR'){width=data.readUInt32BE(0);height=data.readUInt32BE(4);if(data[8]!==8||![2,6].includes(data[9])||data[12]!==0)throw new Error('Unsupported screenshot PNG');channels=data[9]===6?4:3;}else if(type==='IDAT')parts.push(data);else if(type==='IEND')break;
  }
  const packed=inflateSync(Buffer.concat(parts)),stride=width*channels,pixels=new Uint8Array(width*height*channels);let source=0;
  const paeth=(a,b,c)=>{const p=a+b-c,pa=Math.abs(p-a),pb=Math.abs(p-b),pc=Math.abs(p-c);return pa<=pb&&pa<=pc?a:pb<=pc?b:c;};
  for(let y=0;y<height;y++){const filter=packed[source++];for(let x=0;x<stride;x++){const i=y*stride+x,a=x>=channels?pixels[i-channels]:0,b=y?pixels[i-stride]:0,c=y&&x>=channels?pixels[i-stride-channels]:0;let value=packed[source++];if(filter===1)value+=a;else if(filter===2)value+=b;else if(filter===3)value+=Math.floor((a+b)/2);else if(filter===4)value+=paeth(a,b,c);else if(filter!==0)throw new Error('Invalid PNG filter');pixels[i]=value&255;}}
  return {width,height,pixel(x,y){const i=(Math.floor(y)*width+Math.floor(x))*channels;return [pixels[i],pixels[i+1],pixels[i+2],channels===4?pixels[i+3]:255];}};
}
export function colorCount(image,rect){const colors=new Set();for(let y=Math.max(0,Math.floor(rect.y));y<Math.min(image.height,rect.y+rect.height);y+=3)for(let x=Math.max(0,Math.floor(rect.x));x<Math.min(image.width,rect.x+rect.width);x+=3){const p=image.pixel(x,y);colors.add((p[0]<<16)|(p[1]<<8)|p[2]);}return colors.size;}
