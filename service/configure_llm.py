"""Local-only, hidden-key configuration. Never sends keys through game UI or writes them to logs."""
from getpass import getpass
from pathlib import Path
from tempfile import NamedTemporaryFile
import os


def main():
    target = Path(__file__).resolve().parent / '.env'
    if target.exists() and input('已有配置。替换它？输入 yes 才继续：').strip() != 'yes':
        return
    print('密钥只保存在本机 service/.env，该文件不会被 Git 提交。')
    provider = input('提供商 [deepseek]：').strip() or 'deepseek'
    base = input('API 地址 [https://api.deepseek.com]：').strip() or 'https://api.deepseek.com'
    model = input('模型名称 [deepseek-chat]：').strip() or 'deepseek-chat'
    key = getpass('API 密钥（输入时不显示）：').strip()
    if not key:
        print('未输入密钥，未保存。'); return
    from app.config import Settings
    try:
        Settings(provider=provider, base_url=base, model=model, api_key=key)
        for value in (provider, base, model, key):
            if any(c in value for c in '\n\r\x00'):
                raise ValueError()
    except Exception:
        print('配置格式不正确，未保存。请检查地址和模型名称。'); return
    def quote(value):
        return "'" + value.replace('\\', '\\\\').replace("'", "\\'") + "'"
    fields = {'LLM_PROVIDER': provider, 'LLM_BASE_URL': base, 'LLM_MODEL': model,
              'LLM_API_KEY': key, 'LLM_TIMEOUT_SECONDS': '60', 'LLM_MAX_OUTPUT_TOKENS': '600',
              'LLM_RESPONSE_FORMAT': 'json_object'}
    with NamedTemporaryFile(mode='w', encoding='utf-8', dir=target.parent, prefix='.env-', delete=False) as f:
        temp = Path(f.name)
        os.chmod(temp, 0o600)
        for name, value in fields.items():
            f.write(f'{name}={quote(value)}\n')
    os.replace(temp, target)
    print('已保存。双击 start.command 启动，再回 Unity 连接模型。')


if __name__ == '__main__':
    main()
