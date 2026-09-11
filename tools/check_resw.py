import io
for lang in ['en-US', 'zh-CN']:
    path = rf'D:\github\chanjing\src\ChanJing.App\Strings\{lang}\Resources.resw'
    with io.open(path, 'r', encoding='utf-8-sig') as f:
        content = f.read()
    print(f'{lang}: starts with {content[:60]!r}')
    has_root = '<root>' in content
    print(f'{lang}: has root tag: {has_root}')
    print(f'{lang}: length: {len(content)}')
