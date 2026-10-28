"""发布脚本的标准库回归测试；所有Git夹具均在独立临时目录。"""
import json, shutil, subprocess, tempfile, unittest
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
PWSH = shutil.which('pwsh') or shutil.which('powershell')
class ReleaseTests(unittest.TestCase):
    def test_tag_workflow_contract(self):
        text = (ROOT/'.github/workflows/release.yml').read_text(encoding='utf-8-sig')
        for part in ['tags:', 'v*', 'windows-latest', '10.0.x', 'contents: write', 'publish-release.ps1', 'new-release-notes.ps1', 'gh release create', '--verify-tag']:
            self.assertIn(part, text)
        self.assertNotIn('PublishSingleFile=true', text)
    def test_publish_preserves_full_folder(self):
        text=(ROOT/'scripts/publish-release.ps1').read_text(encoding='utf-8-sig')
        for value in ['PublishSingleFile=false','PublishTrimmed=false','--self-contained','requireAdministrator','asInvoker','SHA256','DiskPilotX.Core.dll','System.Private.CoreLib.dll','-t:Rebuild']:
            self.assertIn(value,text)
        installer=(ROOT/'installer/DiskPilotX.iss').read_text(encoding='utf-8-sig')
        for value in ['recursesubdirs','PrivilegesRequired=admin','CloseApplications=yes','setup','createallsubdirs']:
            self.assertIn(value,installer)
    def test_notes_cover_direct_commits_and_override(self):
        with tempfile.TemporaryDirectory(prefix='diskpilot-release-') as directory:
            repo=Path(directory)
            def git(*args): return subprocess.run(['git','-C',str(repo),*args],check=True,capture_output=True,text=True,encoding="utf-8",errors="replace").stdout.strip()
            git('init');git('config','user.name','发布测试');git('config','user.email','fixture@example.invalid')
            def commit(message):
                with (repo/'fixture.txt').open('a',encoding='utf-8') as file:file.write(message+'\\n')
                git('add','.');git('commit','-m',message)
            commit('初始化基线');git('tag','v0.9.0');commit('fix: 修复更新浮层滚动');commit('feat: 新增更新设置');commit('perf: 收紧日志布局');git('tag','v1.0.0')
            output=repo/'notes.md'
            def generate():
                return subprocess.run([PWSH,'-NoProfile','-File',str(ROOT/'scripts/new-release-notes.ps1'),'-RepositoryRoot',str(repo),'-Version','1.0.0','-Ref','v1.0.0','-OutputPath',str(output)],capture_output=True,text=True,encoding='utf-8',errors='replace')
            result=generate();self.assertEqual(0,result.returncode,result.stdout+result.stderr)
            text=output.read_text(encoding='utf-8-sig')
            for part in ['## 🐞 修复问题','## ✨ 新增功能','## 🚀 优化改进','修复更新浮层滚动','新增更新设置','收紧日志布局','compare/v0.9.0...v1.0.0']:
                self.assertIn(part,text)
            self.assertNotIn('初始化基线',text)
            folder=repo/'docs/releases';folder.mkdir(parents=True);(folder/'v1.0.0.md').write_text('## ✨ 新增功能\\n\\n- 人工审校首版说明\\n',encoding='utf-8')
            self.assertEqual(0,generate().returncode)
            text=output.read_text(encoding='utf-8-sig');self.assertIn('人工审校首版说明',text);self.assertNotIn('修复更新浮层滚动',text)
if __name__=='__main__': unittest.main()

