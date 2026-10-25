"""仅使用回环地址的聊天 UI 协议验收夹具，不访问真实模型或删除文件。"""
import argparse
import json
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--port', type=int, default=39426)
parser.add_argument('--root', required=True)
parser.add_argument('--thinking-seconds', type=float, default=35)
options = parser.parse_args()
root = Path(options.root).resolve()
root.mkdir(parents=True, exist_ok=True)
(root / '只读验收.txt').write_text('本次聊天交互验收创建的文件。', encoding='utf-8')


class Handler(BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_GET(self):
        self.send_response(200)
        self.end_headers()
        self.wfile.write(b'{"ok":true}')

    def do_POST(self):
        request = json.loads(self.rfile.read(int(self.headers['Content-Length'])))
        messages = request.get('messages', [])
        latest = next((message.get('content', []) for message in reversed(messages)
                       if message.get('role') == 'user'), [])
        content = latest if isinstance(latest, list) else [{'type': 'text', 'text': latest}]
        prompt = ''.join(block.get('text', '') for block in content)
        if '失败测试' in prompt:
            self.send_error(503, 'Fixture failure')
            return
        self.send_response(200)
        self.send_header('Content-Type', 'text/event-stream; charset=utf-8')
        self.send_header('Connection', 'close')
        self.end_headers()
        try:
            self.event({'type': 'message_start', 'message': {'role': 'assistant'}})
            if any(block.get('type') == 'tool_result' for block in content):
                self.block(0, 'text', '## 检查完成\n\n已收到中文目录的真实扫描工具结果。\n\n- 本次过程包含思考、阶段性说明和工具调用。\n- 创建时间与完成耗时分别记录。\n- 可以展开过程回看中文参数。\n\n这是本地受控协议验收，未连接真实模型。')
                self.finish('end_turn')
                return
            self.event({'type': 'content_block_start', 'index': 0, 'content_block': {'type': 'thinking', 'thinking': ''}})
            for index in range(140):
                text = f'第 {index + 1:03d} 步：核对中文缓存目录，保留重要文件；这是用于检查独立滚动与阅读位置的思考内容。\n'
                self.event({'type': 'content_block_delta', 'index': 0, 'delta': {'type': 'thinking_delta', 'thinking': text}})
                time.sleep(options.thinking_seconds / 140)
            self.event({'type': 'content_block_stop', 'index': 0})
            self.block(1, 'text', '我会检查这个中文目录，接下来等待实际扫描结果。')
            self.event({'type': 'content_block_start', 'index': 2, 'content_block': {'type': 'tool_use', 'id': '中文目录扫描', 'name': 'scan_path', 'input': {}}})
            self.event({'type': 'content_block_delta', 'index': 2, 'delta': {'type': 'input_json_delta', 'partial_json': json.dumps({'path': str(root)}, ensure_ascii=True)}})
            self.event({'type': 'content_block_stop', 'index': 2})
            self.finish('tool_use')
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass

    def event(self, data):
        self.wfile.write(('data: ' + json.dumps(data, ensure_ascii=False) + '\n\n').encode('utf-8'))
        self.wfile.flush()

    def block(self, index, kind, text):
        self.event({'type': 'content_block_start', 'index': index, 'content_block': {'type': kind, kind: ''}})
        for offset in range(0, len(text), 14):
            self.event({'type': 'content_block_delta', 'index': index, 'delta': {'type': 'text_delta', 'text': text[offset:offset + 14]}})
            time.sleep(.08)
        self.event({'type': 'content_block_stop', 'index': index})

    def finish(self, reason):
        self.event({'type': 'message_delta', 'delta': {'stop_reason': reason}})
        self.event({'type': 'message_stop'})


ThreadingHTTPServer(('127.0.0.1', options.port), Handler).serve_forever()
