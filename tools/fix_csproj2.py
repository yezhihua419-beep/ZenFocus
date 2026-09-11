import io
path = r"D:\github\chanjing\src\ChanJing.App\ChanJing.App.csproj"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

old = '''
  <!-- 国际化资源文件 -->
  <ItemGroup>
    <PRIResource Include="Strings\\**\\*.resw" />
  </ItemGroup>'''

if old in content:
    content = content.replace(old, "", 1)
    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(content)
    print("Done: removed duplicate PRIResource")
else:
    print("Not found")
