// Local, read-only UI review. Never connects to an application database.
// First run OwnerRegression --render. Ctrl+C stops the preview.
import http from 'node:http';
import fs from 'node:fs/promises';
import path from 'node:path';
const root=path.resolve('wwwroot'),fixtures=path.join(process.env.TEMP,'AriesOwnerSnapshots');
const aliases={'/':'Home-Index-Guest','/Home/Index':'Home-Index-Guest','/Bookings/Dashboard':'Bookings-ClientDashboard','/Bookings/Details':'Bookings-ClientDetails','/Bookings/CreateStepOne':'Bookings-Create','/Notifications/Index':'Notifications-Index-Client','/Communications/Index':'Communications-Index-Client'};
const types={'.css':'text/css','.js':'application/javascript','.svg':'image/svg+xml','.png':'image/png','.jpeg':'image/jpeg','.jpg':'image/jpeg','.woff':'font/woff','.woff2':'font/woff2'};
const server=http.createServer(async(req,res)=>{
 res.setHeader('Cache-Control','no-store');
 if(req.method!=='GET') {res.writeHead(405,{'Content-Type':'text/plain'});res.end('Read-only synthetic UI preview. Forms do not submit here.');return;}
 try {
  const url=new URL(req.url,'http://localhost'), pathname=decodeURIComponent(url.pathname);
  if(pathname.startsWith('/Communications/')&&pathname!=='/Communications/Index') {res.setHeader('Content-Type','application/json');res.end(pathname==='/Communications/MessagesSince'?'[]':'{"totalUnread":0,"conversations":[]}');return;}
  const key=pathname==='/Communications/Index'&&url.searchParams.get('view')==='inbox'?'Communications-Index-Client-inbox':aliases[pathname]||pathname.slice(1).replaceAll('/','-');
  if(/^[A-Za-z]+-[A-Za-z]+(-[A-Za-z]+)*$/.test(key)) {
   try {
    const html=await fs.readFile(path.join(fixtures,key+'.html'),'utf8');
    res.setHeader('Content-Type','text/html; charset=utf-8');
    res.end(html.replace(/<body([^>]*)>/,'<body$1><div style="padding:8px 20px;background:#302328;color:#fffdf8;font:12px Arial;text-align:center">Design review · synthetic fixture data · forms do not submit</div>'));return;
   }catch{}
  }
  const file=path.resolve(root,'.'+pathname);
  if(!file.startsWith(root+path.sep)){res.writeHead(404);res.end();return;}
  const content=await fs.readFile(file);
  res.setHeader('Content-Type',types[path.extname(file)]||'application/octet-stream');res.end(content);
 }catch {res.writeHead(404,{'Content-Type':'text/plain'});res.end('Preview page not available.');}
});
server.listen(Number(process.env.PORT||5294),'127.0.0.1',()=>console.log('Read-only client preview: http://127.0.0.1:'+server.address().port+' (PID '+process.pid+')'));
