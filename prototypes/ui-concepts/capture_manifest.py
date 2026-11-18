"""Record the exact source, environment and PNG sizes of a reviewed capture run."""
from pathlib import Path
import hashlib
import json
import platform
import struct

root = Path(__file__).resolve().parents[2]
reference = root / 'design-system/notifs/reference'
sources = ['prototypes/ui-concepts/notifs-workspace-prototype.html',
           'prototypes/ui-concepts/workspace-additions.js',
           'prototypes/ui-concepts/workspace-additions.css',
           'prototypes/ui-concepts/notifs-approved-workspace.html',
           'prototypes/ui-concepts/notifs-reference.html',
           'design-system/notifs/tokens.json']
digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
screens = []
for path in sorted((reference / 'screens').glob('*.png')):
    width, height = struct.unpack('>II', path.read_bytes()[16:24])
    screens.append({'file':path.relative_to(reference).as_posix(), 'width':width,
                    'height':height, 'sha256':digest(path)})
report = json.loads((reference / 'screens/inspection.json').read_text(encoding='utf-8-sig'))
manifest = {'status':'extended screens awaiting visual review; behavior Q1-Q46 approved',
            'behaviorConfirmed':'2026-11-19','fixtureCivilTime':'2026-11-18T12:00:00',
            'locale':'ru-RU','demoDisplayTimeZone':'UTC; production deadlines use local civil time',
            'baseViewport':{'width':1060,'height':1240,'deviceScaleFactor':1},
            'narrowWidths':[768,375,320],
            'capture':'whole mock window to bottom; viewport width; preview padding 24 (8 below 600)',
            'offline':True,'python':platform.python_version(),
            'sources':{name:digest(root / name) for name in sources},
            'checks':{'passed':sum(r['ok'] for r in report['results']),
                      'total':len(report['results']),'javascriptErrors':report['errors']},
            'screens':screens}
(reference / 'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2)+'\n',encoding='utf-8',newline='\n')
print(json.dumps({'screens':len(screens),'checks':manifest['checks']},ensure_ascii=False))
