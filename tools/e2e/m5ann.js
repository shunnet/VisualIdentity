
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,300));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});const p=await b.newPage({viewport:{width:1440,height:900}});p.on("pageerror",e=>console.log("[PAGEERROR] "+e.message));
try{
 await p.goto(baseUrl+"/",{waitUntil:"domcontentloaded",timeout:30000});
 await wf(async()=>p.url().includes("/login"),10000,"l"); await p.waitForTimeout(3500);
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(800);
 await p.locator(".ls-side-item",{hasText:"项目"}).first().click();
 await wf(async()=>p.url().includes("/projects"),10000,"p"); await p.waitForTimeout(600);
 await p.locator("button",{hasText:"新建图像工程"}).first().click();
 await p.waitForSelector(".modal",{state:"attached",timeout:8000});
 await p.locator(".modal input.form-control").first().fill("AnnM5-"+Date.now());
 await p.locator(".modal button",{hasText:"创建"}).first().click();
 await wf(async()=>p.url().includes("/project/"),15000,"proj");
 // add label
 await p.locator("#edit-labels-button").click();
 await wf(async()=>(await p.locator(".ls-label-ctl").count())>=1,8000,"lbl");
 const box=p.locator(".ls-label-ctl").first();
 await box.locator("button",{hasText:"添加标签"}).first().click();
 await wf(async()=>(await box.locator("input[type=text]").count())>=1,6000,"in");
 await box.locator("input[type=text]").first().fill("Car");
 await p.locator("button",{hasText:"保存"}).first().click();
 await wf(async()=>(await p.locator(".modal").count())===0,8000,"close");
 // import image
 const img="F:/Snet/VisualIdentity/src/Snet.Yolo.Tasks/wwwroot/samples/demo.jpg";
 await p.locator("#image-import-input").setInputFiles(img);
 await wf(async()=>(await p.locator("tbody tr").count())>=1,15000,"task");
 // open editor
 await p.locator("tbody tr button",{hasText:"开始标注"}).first().click();
 await wf(async()=>p.url().includes("/labeling/"),20000,"labeling");
 await p.waitForSelector(".ls-loading",{state:"detached",timeout:20000});await p.waitForTimeout(700);
 await p.locator(".ls-label-row",{hasText:"Car"}).first().click();
 await p.keyboard.press("r");
 const c=await p.locator("canvas").first().boundingBox();
 await p.mouse.move(c.x+c.width*0.25,c.y+c.height*0.35); await p.mouse.down(); await p.mouse.move(c.x+c.width*0.6,c.y+c.height*0.7,{steps:8}); await p.mouse.up();
 await wf(async()=>(await p.locator(".ls-editor-region-row").count())>=1,12000,"region");
 console.log("region after draw:", await p.locator(".ls-editor-region-row").count());
 // back (autosave)
 await p.locator("header button.ls-icon-btn").first().click();
 await wf(async()=>p.url().includes("/project/"),15000,"back"); await p.waitForTimeout(1500);
 // re-open editor -> region persisted?
 await p.locator("tbody tr button",{hasText:"开始标注"}).first().click();
 await wf(async()=>p.url().includes("/labeling/"),20000,"relabel");
 await p.waitForSelector(".ls-loading",{state:"detached",timeout:20000});await p.waitForTimeout(800);
 console.log("region after reopen:", await p.locator(".ls-editor-region-row").count());
 await p.screenshot({path:"F:/Snet/VisualIdentity/artifacts/m5ann.png"});
}catch(e){console.log("ERR "+e.message);}finally{await b.close();}})();
