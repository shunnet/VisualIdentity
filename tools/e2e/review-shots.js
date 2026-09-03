
const {chromium}=require("playwright");
const baseUrl="http://127.0.0.1:5206";
const OUT="F:/Snet/VisualIdentity/artifacts/review";
async function wf(fn,t,l){const s=Date.now();while(Date.now()-s<t){try{const v=await fn();if(v)return v;}catch(e){}await new Promise(r=>setTimeout(r,250));}throw new Error("timeout "+l);}
(async()=>{const b=await chromium.launch({headless:true,channel:"msedge"});
const shot=async(p,name)=>{await p.waitForTimeout(1600); await p.screenshot({path:OUT+"/"+name+".png"}); console.log("shot",name);};
try{
 const p=await b.newPage({viewport:{width:1440,height:900}}); p.on("pageerror",e=>console.log("PAGEERROR: "+e.message));
 await p.goto(baseUrl+"/login",{waitUntil:"domcontentloaded",timeout:30000}); await p.waitForTimeout(4000); await shot(p,"01-login");
 await p.locator("input").first().fill("admin");await p.locator("input[type=password]").fill("123456");
 await p.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!p.url().includes("/login"),15000,"in"); await p.waitForTimeout(800);
 await p.goto(baseUrl+"/projects",{waitUntil:"domcontentloaded"}); await shot(p,"02-projects");
 await p.goto(baseUrl+"/project/de01917b58164dfabfb9cbc0677df8e0",{waitUntil:"domcontentloaded"}); await shot(p,"03-project-classify");
 await p.goto(baseUrl+"/validation",{waitUntil:"domcontentloaded"}); await shot(p,"04-validation");
 await p.goto(baseUrl+"/users",{waitUntil:"domcontentloaded"}); await shot(p,"05-users");
 await p.goto(baseUrl+"/train/de01917b58164dfabfb9cbc0677df8e0",{waitUntil:"domcontentloaded"}); await shot(p,"06-train");
 await p.goto(baseUrl+"/labeling/de01917b58164dfabfb9cbc0677df8e0/0",{waitUntil:"domcontentloaded"}); await shot(p,"07-editor-classify");
 await p.goto(baseUrl+"/nope-404",{waitUntil:"domcontentloaded"}); await shot(p,"08-404");
 // create a detect project for editor review
 await p.goto(baseUrl+"/projects",{waitUntil:"domcontentloaded"}); await p.waitForTimeout(1200);
 await p.locator("button",{hasText:"新建图像工程"}).first().click();
 await p.waitForSelector(".modal",{state:"attached",timeout:8000});
 const opt = await p.locator(".modal select option").allTextContents(); console.log("types:", opt.join("/"));
 await p.locator(".modal select").selectOption(opt[1]||"detect").catch(e=>console.log("sel:",e.message.slice(0,60)));
 await p.locator(".modal input").first().fill("review-det");
 await p.locator(".modal button",{hasText:"创建"}).first().click();
 await wf(async()=>p.url().includes("/project/"),20000,"det-proj"); await p.waitForTimeout(1600);
 const detUrl = p.url(); console.log("det url:", detUrl);
 // import images (non classify: label input)
 await p.locator("label",{hasText:"导入图片"}).first().click().catch(()=>p.locator("#image-import-input").first().setInputFiles(["F:/Snet/VisualIdentity/Snet.Yolo.Tasks/wwwroot/samples/demo.jpg"]));
 await p.waitForTimeout(2500);
 const tasks = await p.locator(".ls-table tbody tr").count(); console.log("tasks rows:", tasks);
 await p.waitForTimeout(800);
 // editor
 const m = detUrl.match(/project\/([\w-]+)/); const id = m?m[1]:"";
 await p.goto(baseUrl+"/labeling/"+id+"/0",{waitUntil:"domcontentloaded"}); await shot(p,"09-editor-detect");
 // mobile set on key pages
 const m2=await b.newPage({viewport:{width:390,height:844}});
 await m2.goto(baseUrl+"/login",{waitUntil:"domcontentloaded"}); await m2.waitForTimeout(3800); await m2.screenshot({path:OUT+"/m01-login.png"}); console.log("shot m01");
 await m2.locator("input").first().fill("admin");await m2.locator("input[type=password]").fill("123456");
 await m2.locator("button",{hasText:"登录"}).first().click();
 await wf(async()=>!m2.url().includes("/login"),15000,"min"); await m2.waitForTimeout(900);
 await m2.goto(baseUrl+"/projects",{waitUntil:"domcontentloaded"}); await m2.waitForTimeout(1800); await m2.screenshot({path:OUT+"/m02-projects.png"}); console.log("shot m02");
 await m2.goto(baseUrl+"/project/de01917b58164dfabfb9cbc0677df8e0",{waitUntil:"domcontentloaded"}); await m2.waitForTimeout(2200); await m2.screenshot({path:OUT+"/m03-classify.png"}); console.log("shot m03");
 await m2.goto(baseUrl+"/train/de01917b58164dfabfb9cbc0677df8e0",{waitUntil:"domcontentloaded"}); await m2.waitForTimeout(1800); await m2.screenshot({path:OUT+"/m06-train.png"}); console.log("shot m06");
 console.log("DONE");
}catch(e){console.log("ERR "+e.message.slice(0,400));}finally{await b.close();}})();