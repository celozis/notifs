"""Serve the offline review prototype; --concepts opens the historical Visualize concepts."""
from pathlib import Path
import argparse
import subprocess
import sys
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from functools import partial

directory = Path(__file__).resolve().parent
parser = argparse.ArgumentParser()
parser.add_argument("--concepts", action="store_true", help="Предыдущие четыре варианта вместо нового прототипа")
args = parser.parse_args()
if not args.concepts:
    subprocess.run([sys.executable, str(directory / 'build_reference.py')], check=True)
    print('Notifs: http://127.0.0.1:8768/notifs-reference.html', flush=True)
    print('Ctrl+C — остановить предпросмотр. Можно также открыть notifs-reference.html напрямую.', flush=True)
    try:
        ThreadingHTTPServer(('127.0.0.1', 8768), partial(SimpleHTTPRequestHandler, directory=str(directory))).serve_forever()
    except KeyboardInterrupt:
        pass
    raise SystemExit(0)
filename = "notifs-design-concepts.html" if args.concepts else "notifs-workspace-prototype.html"
cache = Path.home() / ".codex/plugins/cache/openai-curated-remote/visualize"
renderers = sorted(cache.glob("*/skills/visualize/scripts/render.py"))
if not renderers:
    raise SystemExit("Нужен установленный плагин Visualize для предпросмотра этих концептов.")
raise SystemExit(subprocess.call([
    sys.executable, str(renderers[-1]), str(directory / filename),
    "--serve", "--port", "8768", "--title", "Notifs — варианты дизайна",
]))
