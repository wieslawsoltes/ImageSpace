// Independent scalar reference for the established ImageSpace RGBA8 filter semantics.
const clamp=(v,a,b)=>Math.min(b,Math.max(a,v));
function byte(v) { v=clamp(v,0,255);const lo=Math.floor(v),f=v-lo;return lo+(f>.5||(f===.5&&lo%2===1)?1:0); }
export function reference(source,width,height,{kind,amount=0,secondary=0}) {
  const out=new Uint8Array(source.length);
  const index=(x,y)=>(clamp(y,0,height-1)*width+clamp(x,0,width-1))*4;
  if(kind==='GaussianBlur') {
    if(amount<=0)return source.slice();
    const sigma=clamp(amount,.1,32),radius=Math.ceil(sigma*3),weights=[];
    for(let x=-radius;x<=radius;x++)weights.push(Math.exp(-x*x/(2*sigma*sigma)));
    const total=weights.reduce((a,b)=>a+b,0);for(let i=0;i<weights.length;i++)weights[i]/=total;
    const mid=new Float32Array(source.length);
    for(let y=0;y<height;y++)for(let x=0;x<width;x++)for(let d=-radius;d<=radius;d++){
      const at=index(x+d,y),to=index(x,y),a=Math.fround(source[at+3]/255),w=Math.fround(weights[d+radius]);
      for(let c=0;c<3;c++)mid[to+c]=Math.fround(mid[to+c]+Math.fround(Math.fround(source[at+c]*a)*w));
      mid[to+3]=Math.fround(mid[to+3]+Math.fround(a*w));
    }
    for(let y=0;y<height;y++)for(let x=0;x<width;x++){
      const to=index(x,y),sum=[0,0,0,0];
      for(let d=-radius;d<=radius;d++){const at=index(x,y+d),w=weights[d+radius];for(let c=0;c<4;c++)sum[c]+=mid[at+c]*w;}
      if(sum[3]>0){for(let c=0;c<3;c++)out[to+c]=byte(sum[c]/sum[3]);out[to+3]=byte(sum[3]*255);}
    }
    return out;
  }
  if(kind==='Pixelate'){
    const size=clamp(Math.trunc(amount),2,128);
    for(let y=0;y<height;y+=size)for(let x=0;x<width;x+=size){
      const sums=[0,0,0,0];let count=0;
      for(let py=y;py<Math.min(y+size,height);py++)for(let px=x;px<Math.min(x+size,width);px++){
        const at=index(px,py),a=source[at+3];for(let c=0;c<3;c++)sums[c]+=source[at+c]*a;sums[3]+=a;count++;
      }
      const v=sums[3]?[byte(sums[0]/sums[3]),byte(sums[1]/sums[3]),byte(sums[2]/sums[3]),byte(sums[3]/count)]:[0,0,0,0];
      for(let py=y;py<Math.min(y+size,height);py++)for(let px=x;px<Math.min(x+size,width);px++)out.set(v,index(px,py));
    }
    return out;
  }
  for(let y=0;y<height;y++)for(let x=0;x<width;x++){
    const at=index(x,y),[r,g,b,a]=source.subarray(at,at+4);if(!a)continue;
    const l=r*.2126+g*.7152+b*.0722;let rgb=[r,g,b];
    switch(kind){
      case 'Invert':rgb=rgb.map(c=>255-c);break;
      case 'Grayscale':rgb=[l,l,l];break;
      case 'Sepia':rgb=[r*.393+g*.769+b*.189,r*.349+g*.686+b*.168,r*.272+g*.534+b*.131];break;
      case 'BrightnessContrast':rgb=rgb.map(c=>(c-127.5)*(1+clamp(secondary,-99,300)/100)+127.5+amount*2.55);break;
      case 'Saturation':rgb=rgb.map(c=>l+(c-l)*Math.max(0,1+amount/100));break;
      case 'Gamma':rgb=rgb.map(c=>255*Math.pow(c/255,1/clamp(amount,.1,10)));break;
      case 'Threshold':rgb=Array(3).fill(l>=clamp(amount,0,255)?255:0);break;
      case 'Posterize':{const levels=clamp(Math.trunc(amount),2,256)-1;rgb=rgb.map(c=>byte(c/255*levels)*255/levels);break;}
      default:{
        const strength=clamp(amount===0?1:amount,.1,5);
        const kernel=kind==='Emboss'?[-2,-1,0,-1,1,1,0,1,2]:kind==='Edges'?[-1,-1,-1,-1,8,-1,-1,-1,-1]:[0,-strength,0,-strength,1+4*strength,-strength,0,-strength,0];
        rgb=[0,0,0];for(let ky=-1;ky<=1;ky++)for(let kx=-1;kx<=1;kx++){
          const src=index(x+kx,y+ky),w=kernel[(ky+1)*3+kx+1];for(let c=0;c<3;c++)rgb[c]+=source[src+c]*w;
        }
        if(kind==='Emboss')rgb=rgb.map(c=>c+128);
      }
    }
    out.set([...rgb.map(byte),a],at);
  }
  return out;
}
export function fixture(width,height){
  const result=new Uint8Array(width*height*4);
  for(let y=0;y<height;y++)for(let x=0;x<width;x++)result.set([(x*71+y*13)%256,(x*19+y*89)%256,(x*101+y*7)%256,[0,1,64,127,192,254,255][(x+y)%7]],(y*width+x)*4);
  return result;
}
