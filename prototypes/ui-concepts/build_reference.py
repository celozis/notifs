"""Build the review fragment and a fully local standalone preview from the approved source.

No production application files or databases are touched.
"""
from pathlib import Path
import argparse
import json
import re

ROOT = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument('--inline-output', type=Path)
args = parser.parse_args()
source = (ROOT / 'notifs-workspace-prototype.html').read_text(encoding='utf-8')
css = (ROOT / 'workspace-additions.css').read_text(encoding='utf-8')
js = (ROOT / 'workspace-additions.js').read_text(encoding='utf-8')
marker = " root.addEventListener('input',()=>{if(state.page==='new')saveDraft();});"
assert source.count(marker) == 1
fragment = source.replace(marker, js + '\n' + marker)
fragment = fragment.replace('</style>', css + '\n</style>', 1)
fragment = fragment.replace('Промотать 15 минут', 'Промотать 30 минут')
demo = '<button type="button" class="btn" data-demo="personal">Личное напоминание</button><button type="button" class="btn" data-demo="signal">Простой сигнал</button><button type="button" class="btn" data-demo="summary">Сводка</button><button type="button" class="btn" data-demo="sleep">Сон компьютера</button><button type="button" class="btn" data-demo="restart">Перезапуск</button><button type="button" class="btn" data-demo="reset">Сбросить демо</button>'
fragment = fragment.replace('Дойти до напоминания после оценки</button></div>', 'Дойти до напоминания после оценки</button>' + demo + '</div>', 1)
fragment = fragment.replace('<!-- Throwaway Notifs workspace, 2026-11-18.', '<!-- Review prototype after Q1-Q46, not the production WPF application. 2026-11-18.')
fragment = fragment.replace('All data and time are in memory.', 'Demo storage is separate from production data.')
(ROOT / 'notifs-approved-workspace.html').write_text(fragment, encoding='utf-8', newline='\n')
token_names = {'background':'bg','navigation':'rail','surface':'panel','elevated':'raised','hover':'hover','foreground':'ink','muted-foreground':'muted','border':'line','primary':'accent','on-primary':'on','primary-soft':'soft','selected':'selected','danger':'error','scrim':'scrim'}
raw_tokens = dict(re.findall(r'--nx-([a-z-]+):([^;]+);', source.split('.nx-app{', 1)[1].split('background:', 1)[0]))
colors = {name:{'$type':'color','$value':raw_tokens[key]} for name,key in token_names.items()}
colors['primary-hover'] = {'$type':'color','$value':'#f0c57f'}
for rank_name in 'sabcd':
    colors['rank-'+rank_name] = {'$type':'color','$value':raw_tokens[rank_name]}
    colors['on-rank-'+rank_name] = {'$type':'color','$value':raw_tokens[rank_name+'-ink']}
dimensions = {'sidebar':178,'sidebar-compact':58,'page-gutter':25,'page-top':30,'split-gap':20,'card-radius':12,'button-radius':8,'action-height':44,'body-font':16,'page-title-font':29,'detail-title-font':22,'list-title-font':15,'label-font':13,'metadata-font':11,'rank-size':26,'icon-size':18,'icon-stroke':1.7,'toast-width':365,'toast-gap':10,'toast-offset':22}
tokens = {'$description':'Graphite/amber selected by the user; Q1-Q46 behavior confirmed 2026-11-19. Extended screens await visual review. CSS pixels map to WPF DIP at equal logical size.','color':colors,'font':{'body':{'$type':'fontFamily','$value':'Segoe UI'},'code':{'$type':'fontFamily','$value':'Consolas'}},'dimension':{k:{'$type':'dimension','$value':{'value':v,'unit':'px'}} for k,v in dimensions.items()}}
(ROOT.parents[1] / 'design-system/notifs/tokens.json').write_text(json.dumps(tokens,ensure_ascii=False,indent=2)+'\n',encoding='utf-8', newline='\n')
if args.inline_output:
    args.inline_output.write_text(fragment, encoding='utf-8', newline='\n')
vendor = ROOT / 'vendor' / 'lucide.js'
if vendor.exists():
    icons = vendor.read_text(encoding='utf-8').replace('</script', '<\\/script')
    wrapper = '<!doctype html><html lang="ru"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>Notifs — эталон после интервью</title><style>body{margin:0;padding:24px;background:#131511;color:#f1f0e6;font-family:"Segoe UI",sans-serif}#notifs-next{max-width:1280px;margin:auto}.viz-row{display:flex;gap:8px;flex-wrap:wrap}.btn{font:13px "Segoe UI";padding:10px 12px;border:1px solid #505646;border-radius:8px;background:#292c27;color:#f1f0e6;cursor:pointer}.btn:disabled{opacity:.5}button:focus-visible{outline:2px solid #e5b666;outline-offset:3px}body>p{max-width:1280px;margin:0 auto 16px;font-size:12px;color:#bdc0b1}@media(max-width:600px){body{padding:8px}}</style><p>Интерактивный прототип · Данные демонстрационные. Кнопки под приложением управляют проверочными сценариями.</p><script>'
    standalone = wrapper + icons + '</script>' + fragment + '<script>lucide.createIcons({attrs:{width:18,height:18,"stroke-width":1.7}});document.querySelectorAll("[data-tooltip]").forEach(e=>e.title=e.dataset.tooltip);</script></html>'
    (ROOT / 'notifs-reference.html').write_text(standalone, encoding='utf-8', newline='\n')
print(json.dumps({'fragment': str(ROOT / 'notifs-approved-workspace.html'), 'standalone': str(ROOT / 'notifs-reference.html'), 'bytes': len(fragment.encode('utf-8'))}, ensure_ascii=False))
