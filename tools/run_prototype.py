"""Launch the local prototype. --test is an explicit deterministic fixture, never an LLM."""
import argparse,json,os,subprocess,time,urllib.request,urllib.error
from pathlib import Path
root=Path(__file__).resolve().parents[1]
parser=argparse.ArgumentParser();parser.add_argument('--test',action='store_true');parser.add_argument('--check',action='store_true');parser.add_argument('--smoke',action='store_true');args=parser.parse_args()
if args.smoke and not args.test:parser.error('--smoke requires --test; it never calls a live model.')
python=root/'service/.venv/Scripts/python.exe';game=root/'Builds/Windows/Before1800.exe'
if not python.exists():raise SystemExit('Run service/setup.ps1 first.')
if not game.exists():raise SystemExit('Windows build is not available yet. Open the Unity project.')
port=8001 if args.test else 8000
def health(port):
    try:
        try:r=urllib.request.build_opener(urllib.request.ProxyHandler({})).open(f'http://127.0.0.1:{port}/health',timeout=2)
        except urllib.error.HTTPError as e:r=e
        return json.load(r)
    except (OSError,ValueError):return None
if args.check:
    print(json.dumps({'game_exists':game.exists(),'python_exists':python.exists(),'test_mode':args.test,'health':health(port)},ensure_ascii=False));raise SystemExit()
owned=[];handles=[]
def start(module,port,env=None):
    old=health(port)
    if old is not None:
        if not ('mock_mode' in old and 'provider' in old):raise RuntimeError(f'Port {port} belongs to an unrecognized service.')
        if port in (8001,8011) and old.get('provider')!='TEST_FIXTURE':raise RuntimeError('Test port is not a fixture.')
        if port==8000 and old.get('mock_mode'):raise RuntimeError('Normal mode refuses a test-fixture gateway.')
        return
    (root/'artifacts').mkdir(exist_ok=True)
    log=(root/'artifacts'/f'launcher-{port}.log').open('a',encoding='utf-8');handles.append(log)
    proc=subprocess.Popen([str(python),'-m','uvicorn',module,'--host','127.0.0.1','--port',str(port),'--workers','1','--no-access-log'],cwd=root/'service',env=env,stdout=log,stderr=log,creationflags=subprocess.CREATE_NO_WINDOW)
    owned.append(proc)
    for _ in range(60):
        if health(port) is not None:return
        if proc.poll() is not None:raise RuntimeError(f'Service {port} failed. See artifacts/launcher-{port}.log')
        time.sleep(.25)
    raise RuntimeError(f'Service {port} did not start.')
try:
    if args.test:
        print('TEST FIXTURE — NOT A REAL LLM. No external model request will be made.')
        start('tests.fixture_provider:app',8011)
        env=os.environ.copy();env.update(LLM_BASE_URL='http://127.0.0.1:8011/v1',LLM_API_KEY='test-fixture-not-a-secret',LLM_MODEL='deterministic-test-not-llm',LLM_PROVIDER='TEST_FIXTURE',LLM_RESPONSE_FORMAT='json_object',LLM_TOKEN_LIMIT_PARAMETER='max_tokens',LOG_DIR='logs/test-fixture')
        start('app.main:app',8001,env)
    else:start('app.main:app',8000)
    print('Local gateway ready. Missing API configuration is reported inside the game.')
    game_args=[str(game),f'--service-url=http://127.0.0.1:{port}']
    if args.smoke:game_args+=['--fixture-smoke','-screen-width','1280','-screen-height','720','-logFile',str(root/'artifacts/player-smoke-final.log')]
    result=subprocess.run(game_args,cwd=game.parent,check=False)
    raise SystemExit(result.returncode)
finally:
    for p in reversed(owned):
        if p.poll() is None:p.terminate();p.wait(timeout=10)
    for f in handles:f.close()
