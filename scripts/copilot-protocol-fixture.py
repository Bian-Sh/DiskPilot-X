"""仅绑定回环地址的受控 Anthropic SSE 验收服务，不连接真实 Provider。"""
import argparse
import json
import pathlib
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

parser=argparse.ArgumentParser()
parser.add_argument('--port',type=int,default=39386)
parser.add_argument('--root',required=True)
parser.add_argument('--record',required=True)
options=parser.parse_args()
fixture_root=pathlib.Path(options.root).resolve()
fixture_root.mkdir(parents=True,exist_ok=True)
for name,size in [('cache-A',65536),('cache-B',32768)]:
    directory=fixture_root/name
    directory.mkdir(exist_ok=True)
    file=directory/'fixture.tmp'
    if not file.exists(): file.write_bytes(b'x'*size)
(fixture_root/'important.txt').write_text('这是验收创建的保留文件。',encoding='utf-8')
record_lock=threading.Lock()

def record(item):
    with record_lock:
        with open(options.record,'a',encoding='utf-8') as output:
            output.write(json.dumps(item,ensure_ascii=False)+'\n')

class Handler(BaseHTTPRequestHandler):
    protocol_version='HTTP/1.1'
    def log_message(self,*args): pass
    def do_GET(self):
        data=json.dumps({'ok':True,'fixture':True}).encode()
        self.send_response(200);self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)
    def do_POST(self):
        if self.path!='/v1/messages': self.send_error(404);return
        body=json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        names={}
        latest=''
        results=[]
        for message in body.get('messages',[]):
            content=message.get('content',[])
            if isinstance(content,str): content=[{'type':'text','text':content}]
            if message.get('role')=='user' and any(b.get('type')=='text' for b in content):
                last_text=max(i for i,b in enumerate(content) if b.get('type')=='text')
                latest=content[last_text].get('text','')
                content=content[last_text+1:]
                results=[]
            for block in content:
                if block.get('type')=='tool_use': names[block['id']]=block['name']
                elif block.get('type')=='tool_result':
                    try: data=json.loads(block.get('content','{}'))
                    except (ValueError,TypeError): data={}
                    results.append((names.get(block.get('tool_use_id')),data))
        record({'model':body.get('model'),'tool_results':[name for name,_ in results],'thinking':body.get('thinking',{}).get('type')})
        self.send_response(200)
        self.send_header('Content-Type','text/event-stream; charset=utf-8')
        self.send_header('Connection','close')
        self.end_headers()
        self.close_connection=True
        try:
            if '停止测试' in latest:
                self.event({'type':'message_start','message':{'role':'assistant'}})
                self.event({'type':'content_block_start','index':0,'content_block':{'type':'text','text':''}})
                self.event({'type':'content_block_delta','index':0,'delta':{'type':'text_delta','text':'正在进行可停止的流式任务。'}})
                for number in range(150):
                    time.sleep(.2)
                    self.event({'type':'content_block_delta','index':0,'delta':{'type':'text_delta','text':' ·'}})
                self.event({'type':'content_block_stop','index':0});self.finish('end_turn');return
            completed=[name for name,_ in results]
            last=results[-1][1] if results else {}
            if results and last.get('ok') is False:
                self.text('已取消，未删除任何项目。' if last.get('cancelled') else '工具返回失败，未继续执行。');return
            if '清理' in latest and ('执行清理' in latest or '确认清理' in latest):
                if 'list_recommendations' not in completed:
                    self.tool('list_recommendations',{});return
                state=next(data for name,data in reversed(results) if name=='list_recommendations')
                ids=[r['id'] for r in state.get('recommendations',[])]
                if not ids:self.text('当前没有可执行的清理项。');return
                if 'cleanup_recommendations' not in completed:
                    self.tool('cleanup_recommendations',{'ids':ids,'version':state['version']});return
                self.text('清理已完成。结果来自真实清理服务，推荐列表和磁盘视图已同步。');return
            if '询问测试' in latest:
                if 'ask_user' not in completed:self.tool('ask_user',{'question':'本次验收希望保留哪个目录？'});return
                self.text('已收到你的回答：'+str(last.get('answer','')));return
            if 'get_copilot_context' not in completed:
                self.tool('get_copilot_context',{});return
            context=next(data for name,data in results if name=='get_copilot_context')
            if '扫描' not in latest and '分析' not in latest:
                self.text('你好，我可以结合当前磁盘工作区查询、扫描和分析。');return
            if str(context.get('root','')).lower()!=str(fixture_root).lower() and 'scan_path' not in completed:
                self.tool('scan_path',{'path':str(fixture_root)});return
            scan=next((data for name,data in results if name=='scan_path'),context)
            if 'get_analysis_evidence' not in completed:
                self.tool('get_analysis_evidence',{'path':str(fixture_root)});return
            if 'publish_recommendations' not in completed:
                candidates=[{'path':str(fixture_root/name),'category':'TemporaryFiles','safety_rating':'ReviewFirst','explanation':'仅本次验收创建的临时缓存，清理前需要确认。'} for name in ['cache-A','cache-B'] if (fixture_root/name).exists()]
                self.tool('publish_recommendations',{'scan_id':scan['scan_id'],'recommendations':candidates});return
            if 'navigate_path' not in completed and any(tool.get('name')=='navigate_path' for tool in body.get('tools',[])):
                self.tool('navigate_path',{'path':str(fixture_root),'view':'tree'});return
            self.text('## 扫描与分析完成\n\n已根据实际扫描数据发布 **2 项**缓存建议，文件树已定位到本次验收目录。\n\n- 目录和大小来自本地扫描。\n- 保留 important.txt。\n- 清理需要你确认具体清单。\n\n这是受控协议验收，未连接真实模型。')
        except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError):
            record({'event':'client_cancelled'})
    def event(self,data):
        self.wfile.write(('data: '+json.dumps(data,ensure_ascii=False)+'\n\n').encode('utf-8'));self.wfile.flush()
    def finish(self,reason):
        self.event({'type':'message_delta','delta':{'stop_reason':reason}})
        self.event({'type':'message_stop'})
    def text(self,text):
        self.event({'type':'message_start','message':{'role':'assistant'}})
        self.event({'type':'content_block_start','index':0,'content_block':{'type':'text','text':''}})
        for index in range(0,len(text),12):
            self.event({'type':'content_block_delta','index':0,'delta':{'type':'text_delta','text':text[index:index+12]}})
            time.sleep(.025)
        self.event({'type':'content_block_stop','index':0});self.finish('end_turn')
    def tool(self,name,args):
        record({'event':'tool_use','tool':name})
        self.event({'type':'message_start','message':{'role':'assistant'}})
        self.event({'type':'content_block_start','index':0,'content_block':{'type':'tool_use','id':'fixture-'+name+'-'+str(time.time_ns()),'name':name,'input':{}}})
        text=json.dumps(args,ensure_ascii=False)
        cut=max(1,len(text)//2)
        for part in [text[:cut],text[cut:]]:
            self.event({'type':'content_block_delta','index':0,'delta':{'type':'input_json_delta','partial_json':part}})
        self.event({'type':'content_block_stop','index':0});self.finish('tool_use')

record({'event':'fixture_started','port':options.port})
ThreadingHTTPServer(('127.0.0.1',options.port),Handler).serve_forever()
