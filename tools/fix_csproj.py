import io
path = r"D:\github\chanjing\src\ChanJing.App\ChanJing.App.csproj"
with io.open(path, "r", encoding="utf-8-sig") as f:
    content = f.read()

old = '''  <ItemGroup>
    <ProjectReference Include="..\\ChanJing.Core\\ChanJing.Core.csproj" />
  </ItemGroup>'''

new = '''  <ItemGroup>
    <ProjectReference Include="..\\ChanJing.Core\\ChanJing.Core.csproj" />
  </ItemGroup>

  <!-- 国际化资源文件 -->
  <ItemGroup>
    <PRIResource Include="Strings\\**\\*.resw" />
  </ItemGroup>'''

if old in content:
    content = content.replace(old, new, 1)
    with io.open(path, "w", encoding="utf-8-sig", newline="") as f:
        f.write(content)
    print("Done")
else:
    print("Not found")
    # 打印相关行
    for i, line in enumerate(content.split("\n")):
        if "ProjectReference" in line or "ItemGroup" in line:
            print(f"{i}: {line}")
