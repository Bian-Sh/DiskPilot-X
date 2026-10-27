"""检查规格结构和改动边界；不代替产品验收。"""
from pathlib import Path
import hashlib
import json
import re
import subprocess
import sys

folder = Path(__file__).resolve().parent
root = folder.parent.parent
plan = root / '.planning/analysis-rule-orchestration-20261027'
errors = []
names = ['README.md', 'spec.md', 'design.md', 'tasks.md', 'acceptance.md', 'requirements-map.md']
texts = {name: (folder / name).read_text(encoding='utf-8') for name in names}

def check_links(document, text):
    for link in re.findall(r'\]\(([^)]+)\)', text):
        if '://' in link or link.startswith('#'):
            continue
        destination = (document.parent / link.split('#')[0]).resolve()
        if not destination.is_relative_to(root) or not destination.is_file():
            errors.append(f'无效引用：{document.name} → {link}')

for name, text in texts.items():
    if not text.strip() or re.search(r'\b(?:TODO|TBD)\b|\[NEEDS CLARIFICATION\]|\{\{', text):
        errors.append(f'文档为空或含未填写占位符：{name}')
    check_links(folder / name, text)

requirements = {f'FR{i:02}' for i in range(1, 19)}
acceptance = {f'A{i}' for i in range(1, 19)}
spec = texts['spec.md']
ids = re.findall(r'^### (FR\d{2})：', spec, re.M)
if len(ids) != 18 or set(ids) != requirements:
    errors.append('要求必须是唯一的 FR01—FR18')
ids = re.findall(r'^### Scenario: (A\d+) —', spec, re.M)
if len(ids) != 18 or set(ids) != acceptance:
    errors.append('场景必须是唯一的 A1—A18')
rows = re.findall(r'^\| (A\d+) \| (FR\d{2}) \|', texts['acceptance.md'], re.M)
if len(rows) != 18 or {a for a, _ in rows} != acceptance or {b for _, b in rows} != requirements:
    errors.append('验收矩阵映射不完整')
if any(int(a[1:]) != int(b[2:]) for a, b in rows):
    errors.append('验收映射编号不一致')
ids = re.findall(r'^- \[[ x]\] \[(T\d{3})\]', texts['tasks.md'], re.M)
if len(ids) != 16 or set(ids) != {f'T{i:03}' for i in range(1, 17)}:
    errors.append('任务必须是唯一的 T001—T016')
if requirements - set(re.findall(r'FR\d{2}', texts['tasks.md'])):
    errors.append('实施计划未覆盖全部要求')
ids = re.findall(r'^\| (S\d{2}) \|', texts['requirements-map.md'], re.M)
if len(ids) != 12 or set(ids) != {f'S{i:02}' for i in range(1, 13)}:
    errors.append('需求来源映射不完整')
if '本次明确不改' not in texts['design.md'] or '规则 CRUD' not in spec:
    errors.append('缺少并行模块禁改边界')

active_dir = folder / 'comet/changes/analysis-rule-orchestration'
archive_dirs = sorted((folder / 'comet/archive').glob('*-analysis-rule-orchestration'))
change_dir = active_dir if active_dir.is_dir() else (archive_dirs[-1] if archive_dirs else None)
if change_dir is None:
    sys.exit('规格检查未通过：缺少正式变更或归档目录。')
formal_path = change_dir / 'specs/disk-analysis-orchestration/spec.md'
brief_path = change_dir / 'brief.md'
formal = formal_path.read_text(encoding='utf-8')
brief = brief_path.read_text(encoding='utf-8')
state = (change_dir / 'comet-state.yaml').read_text(encoding='utf-8')
blocks = r'^### (FR\d{2})：[^\n]*\n([\s\S]*?)(?=^### FR|^## |\Z)'
source_blocks = {k: v.strip() for k, v in re.findall(blocks, spec, re.M)}
formal_blocks = {k: v.strip() for k, v in re.findall(blocks, formal, re.M)}
if source_blocks != formal_blocks or len(formal_blocks) != 18:
    errors.append('正式规格与十八项要求不一致')
ids = re.findall(r'^验收：(A\d+)$', formal, re.M)
if len(ids) != 18 or set(ids) != acceptance:
    errors.append('正式场景引用不完整')
examples = brief.split('# 验收示例\n', 1)[1].split('\n# ', 1)[0]
if len(re.findall(r'^- ', examples, re.M)) != 18:
    errors.append('brief 顶层验收数量不是十八项')
ids = re.findall(r'^  - id: (A\d+)$', state, re.M)
if len(ids) != 18 or set(ids) != acceptance:
    errors.append('Runtime 落盘验收不一致')
check_links(formal_path, formal)
check_links(brief_path, brief)

if '--documents-only' in sys.argv:
    if not (plan / 'boundary-before-registration.json').is_file():
        sys.exit('历史纯文档边界检查需要原工作树过程记录；默认文档检查不依赖该记录。')
    before = json.loads((plan / 'boundary-before-registration.json').read_text(encoding='utf-8'))
    for name, expected_hash in before.items():
        current = root / name
        if not current.is_file():
            errors.append(f'原有文件缺失：{name}')
            continue
        raw = current.read_bytes()
        if name == '.comet/config.yaml':
            new = b'artifact_root: docs/analysis-rule-orchestration'
            if raw.count(new) != 1:
                errors.append('独立规格根不符合授权')
            raw = raw.replace(new, b'artifact_root: docs', 1)
        if hashlib.sha256(raw).hexdigest() != expected_hash:
            errors.append(f'原有内容发生未授权变化：{name}')
    allowed = ('docs/analysis-rule-orchestration/', '.planning/analysis-rule-orchestration-20261027/')
    tracked = subprocess.check_output(['git', 'diff', '--name-only', '-z', 'HEAD'], cwd=root).decode('utf-8').split('\0')
    untracked = subprocess.check_output(['git', 'ls-files', '--others', '--exclude-standard', '-z'], cwd=root).decode('utf-8').split('\0')
    for name in set(tracked + untracked):
        if name and name != '.comet/config.yaml' and not name.startswith(allowed):
            errors.append(f'超出纯文档阶段边界：{name}')

if errors:
    print('规格检查未通过：\n' + '\n'.join('- ' + error for error in errors))
    sys.exit(1)
print('规格检查通过：6 份文档、18 项要求与验收、16 项任务、12 项来源映射；正式规格引用和边界有效。')
print('此结果不是产品实现或产品验收通过。')
