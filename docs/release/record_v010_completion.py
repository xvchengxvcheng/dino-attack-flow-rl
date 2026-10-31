from pathlib import Path
import json

ROOT = Path(__file__).resolve().parents[2]
url = 'https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0'
head = 'cacab8ee5653a6a7704e20ec8a4376bd9ba78515'
verification = json.loads((ROOT / 'docs/release/v010-remote-verification.json').read_text())
assert verification['all_download_hashes_match'] and verification['asset_count'] == 7
assets = json.loads((ROOT / 'release-staging/artifacts/v0.1.0/artifacts.json').read_text())['release_attachments']
rows = '\n'.join(f"| `{a['name']}` | {a['bytes']:,} | `{a['sha256']}` |" for a in assets)
report = f'''# v0.1.0 上传完成记录

日期：2026-09-06。**源码与附件上传：已完成；完整人工游玩验收：未完成。**

- 仓库：https://github.com/xvchengxvcheng/dino-attack-flow-rl
- Release：{url}（公开，prerelease研究演示）
- `main` 与 `v0.1.0`：`{head}`。
- 游戏生产源码对应构建来源`79b13c2`；后续提交增加文档、下载脚本、示例配置、FPO测试和忽略规则，未改生产玩法。

## 实际验证

- 新建Python 3.10环境：`D:/job/codex-validation/dino-flow-rl-v010/env`，按`requirements.lock`安装，PyTorch2.2.1+cu121；pip check通过，CUDA可用，三CLI帮助通过。
- 非集成测试：374 passed、1 skipped、3 deselected，14.15秒。XML：`reports/windows-rebuild-20260906/release-python-fresh.xml`。
- Unity定向：此前EditMode4/4、PPO/PolicyFlow PlayMode2/2；本次新增FPO真实地图三开始/认输/重试1/1通过。
- 冻结训练Player：六流/4D，325决策、4自然终局、0截断；日志覆盖两张训练地图。未启动新训练。
- 三ONNX与解压checkpoint batch6数值对照通过，最大误差2.63e-6，动作有限/有界；Flow BC best严格加载通过。
- 五包本地逐文件hash验证通过；下载脚本空目录安装、幂等重跑、损坏ZIP拒绝通过；四发布示例配置在仅解压附件的目录解析通过。
- GitHub新clone`release-staging/verify-v010-source`：2,621个清单文件、61处文档本地链接通过，连同清单共2,622个Git文件；固定ML-Agents依赖下载及幂等重跑通过。
- 在新clone运行公开`download-artifacts.ps1 -All`：五包全部匿名下载、解压、逐文件校验通过。随后匿名下载索引/校验和，七附件与本地SHA-256全部一致；公开Release页面HTTP200，正式标签指向上述commit。
- 使用独立Python环境重新加载远端解压模型通过；远端下载游戏PID54452有响应，图形/脚本/输入正常，启动日志错误模式0。隐藏窗口无法正常关闭，检查后仅停止本次进程；未验证正常退出。

关键命令：`docs/release/prepare_v010.py`、`validate_v010.py`、`prepare_example_configs.py`、`finalize_source_manifest.py`、`verify_github_clone.py`、`github_v010.py`；执行记录与XML在`reports/windows-rebuild-20260906/`，远端记录`docs/release/v010-remote-verification.json`。

## 附件

| 文件 | 字节 | SHA-256 |
|---|---:|---|
{rows}

另有`artifacts.json`与`SHA256SUMS.txt`，共七项Release资产。可选研究checkpoint包未纳入首版；PolicyFlow后续step3196659未替换部署step2147452。

## 保留的限制与过程异常

- 桌面交互工具初始化失败，成品三地图三策略的完整人工游玩未验证。发布为研究演示，不将完整Unity/发布验收记为通过。科研阶段8、严格确定性、多seed、Dino ReinFlow和action chunk状态不变。
- C盘首次安装空间不足，仅清理本任务失败环境/重复下载，迁至D盘成功。原研究环境未修改。
- 新clone依赖首次GitHub TLS中断，失败副本保存在`release-staging/failed-v010-dependency`，重新下载成功。公开操作也遇到一次TLS中断，重试同一Release成功。
- GitHub曾为草稿生成临时untagged标签，已将同一Release关联到正确`v0.1.0`，七附件URL均经实际下载复验。
- 匿名GitHub API返回403，未将其解释为私有仓库；公开页面与直接附件下载实际通过。
- 原Unity项目/旧构建/checkpoint未覆盖。发布副本仅保留Unity运行自动生成的Timers与ProjectSettings差异，均未提交。
'''
(ROOT / 'docs/release/V010_PUBLICATION_REPORT.md').write_text(report, encoding='utf-8')
plan_path = ROOT / 'docs/release/GITHUB_UPLOAD_PLAN.md'
plan = plan_path.read_text(encoding='utf-8')
plan = plan.replace('可执行程序及数据附件发布：进行中。', '可执行程序及数据附件发布：已完成。')
plan = plan.replace('- [ ] Release附件发布及匿名下载复验进行中，完成后记录实际URL及最终commit。',
                    f'- [x] Release已公开：{url}；最终commit/tag为`{head}`。新clone中五包匿名下载/解压/逐文件校验通过，索引和SHA256SUMS亦匿名下载一致，共七附件；下载版游戏启动与解压checkpoint加载通过。完整记录`docs/release/V010_PUBLICATION_REPORT.md`。')
lines = plan.splitlines()
section = ''
for i, line in enumerate(lines):
    if line.startswith('### ') and len(line) > 5 and line[4:6] in ('A.', 'B.', 'C.', 'D.', 'E.'):
        section = line[4]
        lines[i] = line.replace('未开始', '进行中' if section == 'C' else '已完成')
    elif line.startswith('## '): section = ''
    elif section and line.startswith('- [ ]'):
        if section == 'C' and '在三地图分别检查' in line:
            lines[i] = line + ' **新构建已完成；完整人工交互检查因桌面工具不可用未完成。**'
        else: lines[i] = line.replace('- [ ]', '- [x]', 1)
plan_path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
main = ROOT / 'docs/PROJECT_PLAN.md'
with main.open('a', encoding='utf-8') as f:
    f.write(f'''\n### 2026-09-06：剩余上传任务交付

- 阶段8并列发布任务：上传已完成；完整人工游玩验收未完成，科研阶段状态不变。
- [x] `main`/`v0.1.0`最终commit `{head}`；公开研究演示Release：{url}，共五ZIP与两校验文件。发布过程临时标签已纠正，实际附件URL均验证可下载。
- [x] 新clone的2621清单文件/61链接、固定依赖安装和幂等重跑通过；公开下载脚本`-All`安装五包通过，七附件匿名下载hash全部匹配。远端解压checkpoint/Flow BC加载及数值对照通过；下载版游戏启动有响应，错误模式0，仅停止本次检查进程。
- 证据：`docs/release/V010_PUBLICATION_REPORT.md`、`v010-remote-verification.json`、`reports/windows-rebuild-20260906/remote-player-smoke-result.json`。五附件校验值见报告和`release-staging/artifacts/v0.1.0/SHA256SUMS.txt`。
- 保留：桌面工具不可用导致成品三地图三策略人工全流程未验；隐藏窗口正常退出未验；历史Unity失败、严格确定性和统一算法比较未完成。上传计划C仍进行中，A/B/D/E已完成，不以成功上传伪装完整验收。
''')
print('Publication report and project/upload plans updated.')
